using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AIF.Controller;
using Mpai.Core;
using Mpai.Core.OSD;
namespace Mpai.Aims.Tts;
// MMC-TTS-V2.5 - self-contained IAimProcessor.
// Reads its own port names from 1MMC-TTS-V2.5-I01.json at startup.
//
// When a Speech Personal Status (MMC-SPS) is supplied, its emotion shapes the
// Piper prosody (speaking rate + variation) so the voice carries the feeling.
// The port is optional: absent => neutral synthesis (the Apps that give no Personal Status unchanged).
public sealed class TtsAimProcessor : IAimProcessor
{
    private readonly string      _inputPort;
    private readonly string      _outputPort;
    private readonly string      _spsPort;
    private readonly string      _modelPort;
    private readonly PiperTtsAim _tts;
    public string InstanceId { get; }
    public TtsAimProcessor(
        string      instanceId,
        PiperTtsAim tts,
        AimPortReader    ports)
    {
        InstanceId  = instanceId;
        _tts        = tts;
        _inputPort  = ports.Input("OSD-BTO-V1.5");   // dual-typed port [OSD-BTO, OSD-TXO]
        _outputPort = ports.Output("OSD-BSO-V1.5");  // dual-typed port [OSD-BSO, OSD-SPO]
        _spsPort    = ports.InputOrDefault("MMC-SPS-V2.5", 1, string.Empty);   // optional, dual-typed port [MMC-SPS, MMC-EPS]: the Emotion to convey
        _modelPort  = ports.InputOrDefault("AIP-MLO-V1.0", 1, string.Empty);   // optional Speech Model
    }
    public async Task<Message> ProcessAsync(Message message)
    {
        // A SPEECH MODEL THAT IS A NETWORK OF THE DEPLOYMENT (named by its ID, MLModelData DataID): each text of the Text Object said
        // with it, one speech file for each (the Speech Restoration System puts each in its place). A Speech Model of another kind is
        // not used, as before: this Text To Speech speaks in its own voice.
        if (_modelPort.Length > 0 && message.Ports.TryGetValue(_modelPort, out var modelJson) && !string.IsNullOrWhiteSpace(modelJson) && modelJson.Contains("\"DataID\"", StringComparison.Ordinal))
        {
            try { return await SpeakWithNetworkAsync(message, NetworkOf(modelJson)); }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or IOException)
            { return Message.Error(message.MessageId, InstanceId, "MMC-TTS-V2.5: " + ex.Message); }
        }
        var text = MpaiJson.FromJson<BasicTextObject>(message.Ports[_inputPort]);

        SpeechPersonalStatus? sps = null;
        if (!string.IsNullOrEmpty(_spsPort) &&
            message.Ports.TryGetValue(_spsPort, out var spsJson) &&
            !string.IsNullOrWhiteSpace(spsJson))
        {
            sps = SpeechStatusOf(spsJson);
        }

        var prosody = ProsodyArgs(sps);
        MpaiDiag.Emotion("TTS", $"{sps?.SpeechEmotion?.Category ?? "no emotion"} {sps?.SpeechEmotion?.Degree:0.0} -> Piper {(prosody.Length == 0 ? "defaults" : prosody.Trim())}");
        var speech = await _tts.ProcessAsync(text, prosody);
        var json   = MpaiJson.ToJson(speech);
        return new Message
        {
            MessageId   = message.MessageId,
            MessageType = "BasicSpeechObject",
            DataType    = speech.Header,
            Payload     = json,
            Ports       = new Dictionary<string, string> { [_outputPort] = json }
        };
    }

    private async Task<Message> SpeakWithNetworkAsync(Message message, string network)
    {
        var root = JsonNode.Parse(message.Ports[_inputPort]) as JsonObject ?? throw new FormatException("The Text is a JSON object.");
        var texts = root["BasicTextData"] is JsonArray items
            ? items.OfType<JsonObject>().Select(i => i["Data"]?.GetValue<string>() ?? "").ToList()
            : new List<string> { MpaiJson.FromJson<BasicTextObject>(message.Ports[_inputPort]).GetText() };
        var data = new JsonArray();
        foreach (var text in texts)
            data.Add(new JsonObject { ["Data"] = Convert.ToBase64String(await _tts.SpeakWithNetworkAsync(network, text)) });
        var id = "tts-" + Guid.NewGuid().ToString("N")[..12];
        var json = new JsonObject
        {
            ["Header"] = "OSD-BSO-V1.5", ["BasicSpeechObjectID"] = id,
            ["BasicSpeechDataQualifier"] = new JsonObject
            {
                ["Header"] = "TFA-SPQ-V1.5", ["SpeechQualifierID"] = "q-" + id, ["SubTypes"] = new JsonObject(),
                ["Formats"] = new JsonObject { ["TransportFormats"] = new JsonObject { ["FileFormat"] = "WAV" } },
                ["Attributes"] = new JsonObject { ["Source"] = "Synthetic" }
            },
            ["BasicSpeechObjectData"] = data,
            ["DescrMetadata"] = "Said with the network of the Speech Model: " + Path.GetFileNameWithoutExtension(network)
        }.ToJsonString();
        return new Message
        {
            MessageId = message.MessageId, MessageType = "BasicSpeechObject", DataType = "OSD-BSO-V1.5", Payload = json,
            Ports = new Dictionary<string, string> { [_outputPort] = json }
        };
    }

    // The ID of the network an ML Model Object (AIP-MLO-V1.0) names: MLModelData {DataID}. The network itself is model data of this deployment (TtsFactory.NetworkFile):
    // no file named in the data is opened. A Speech Model that is not a network (numbers in its Data) is refused: this Text To Speech cannot speak with it.
    public static string NetworkOf(string modelJson)
    {
        JsonObject? root;
        try { root = JsonNode.Parse(modelJson) as JsonObject; }
        catch (System.Text.Json.JsonException ex) { throw new FormatException("The Speech Model is not JSON: " + ex.Message); }
        var item = (root?["MLModelData"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(o => o["DataID"] is not null)
                   ?? throw new FormatException("The Speech Model is not a network of this deployment (it has no DataID): this Text To Speech cannot speak with it.");
        var id = item["DataID"]!.GetValue<string>();
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains("..", StringComparison.Ordinal))
            throw new FormatException($"'{id}' is not the name of a network.");
        return id;
    }

    // Map the Speech Personal Status emotion to Piper prosody flags (speaking rate
    // + variation), blended toward Piper's defaults by the emotion Degree. Absent
    // or neutral => no flags, so neutral synthesis is unchanged.
    // THE PERSONAL STATUS PORT TAKES A SPEECH PERSONAL STATUS OR A PERSONAL STATUS (e.g. the Machine Emotion of Entity Dialogue Processing in Conversation with
    // Emotion, a Personal Status with only the Emotion). Of a Personal Status the speech conveys the Emotion of its Speech modality, else of the first modality
    // that has one (Text, Face, Body).
    private static SpeechPersonalStatus? SpeechStatusOf(string json)
    {
        if (!json.Contains("MMC-EPS-V2.5", StringComparison.Ordinal))
            return MpaiJson.FromJson<SpeechPersonalStatus>(json);
        var ps = MpaiJson.FromJson<EntityPersonalStatus>(json);
        if (ps is null) return null;
        if (ps.SpeechPersonalStatus is { } speech) return speech;
        var emotion = ps.TextPersonalStatus?.TextEmotion ?? ps.FacePersonalStatus?.FaceEmotion ?? ps.BodyPersonalStatus?.BodyEmotion;
        return emotion is null ? null : new SpeechPersonalStatus { SpeechEmotion = emotion };
    }

    private static string ProsodyArgs(SpeechPersonalStatus? sps)
    {
        var em  = sps?.SpeechEmotion;
        var cat = em?.Category?.ToUpperInvariant();
        if (string.IsNullOrEmpty(cat) || cat == "CALMNESS" || cat == "NEUTRAL")
            return "";

        double d = em!.Degree ?? 0.7; d = 0.7 + 0.3 * d;
        if (d < 0.0) d = 0.0;
        if (d > 1.0) d = 1.0;

        (double len, double noise, double noisew) t = cat switch
        {
            "HAPPINESS" => (0.80, 0.95, 1.00),
            "ANGER"     => (0.78, 1.00, 0.90),
            "FEAR"      => (0.82, 0.95, 0.95),
            "SADNESS"   => (1.35, 0.35, 0.50),
            _           => (1.0, 0.667, 0.8)
        };

        double L = 1.0   + (t.len    - 1.0)   * d;
        double N = 0.667 + (t.noise  - 0.667) * d;
        double W = 0.8   + (t.noisew - 0.8)   * d;

        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return "--length_scale " + L.ToString("0.###", ci) +
               " --noise_scale " + N.ToString("0.###", ci) +
               " --noise_w "     + W.ToString("0.###", ci);
    }
}
