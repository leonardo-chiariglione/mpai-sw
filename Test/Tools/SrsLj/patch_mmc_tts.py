"""N-27: MMC Text To Speech speaks with a network given as its Speech Model (the optional AIP-MLO-V1.0 input of its L3). Edits three files, CRLF kept."""
from pathlib import Path

D = Path(r"D:\AI\AIMs\MMC\V2.5\TTS")


def edit(name, pairs):
    p = D / name
    s = p.read_bytes().decode("utf-8")
    for old, new in pairs:
        old = old.replace("\n", "\r\n"); new = new.replace("\n", "\r\n")
        assert s.count(old) == 1, (name, old[:60])
        s = s.replace(old, new)
    p.write_bytes(s.encode("utf-8"))


edit("TtsFactory.cs", [(
"""        return new PiperTtsAim(
            defaultVoice.Engine,
            defaultVoice.Profile,
            voices);
    }""",
"""        return new PiperTtsAim(
            defaultVoice.Engine,
            defaultVoice.Profile,
            voices)
        {
            // A NETWORK GIVEN AS THE SPEECH MODEL (a Piper/VITS voice made from the
            // speech of a speaker) is spoken with the same piper program.
            NetworkVoice = path => BuildVoice(runner, path, path + ".json")
        };
    }""")])

edit("PiperTtsAim.cs", [
("""    public Task<BasicSpeechObject> ProcessAsync(BasicTextObject text) => ProcessAsync(text, "");
""",
"""    public Task<BasicSpeechObject> ProcessAsync(BasicTextObject text) => ProcessAsync(text, "");

    // THE VOICE OF A NEURAL NETWORK SPEECH MODEL. When the Speech Model input of the
    // AIM carries a network (a Piper/VITS voice made from the speech of a speaker,
    // CAE-USC Speech Model Creation), the text is said with it - and with nothing
    // else: a network that fails is an error, never the default voice in its place.
    public Func<string, PiperVoice>? NetworkVoice { get; init; }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PiperVoice> _networks = new();

    public async Task<byte[]> SpeakWithNetworkAsync(string network, string text)
    {
        var voice = _networks.GetOrAdd(network, path => (NetworkVoice ?? throw new InvalidOperationException("This Text To Speech cannot speak with a network."))(path));
        var wav = await voice.Engine.GenerateAsync(text, "");
        return wav.SpeechData;
    }
""")])

edit("TtsAimProcessor.cs", [
("""using System.Collections.Generic;
using System.Threading.Tasks;
""",
"""using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
"""),
("""    private readonly string      _spsPort;
""",
"""    private readonly string      _spsPort;
    private readonly string      _modelPort;
"""),
("""        _spsPort    = ports.InputOrDefault("MMC-SPS-V2.5", 1, string.Empty);   // optional emotion
""",
"""        _spsPort    = ports.InputOrDefault("MMC-SPS-V2.5", 1, string.Empty);   // optional emotion
        _modelPort  = ports.InputOrDefault("AIP-MLO-V1.0", 1, string.Empty);   // optional Speech Model
"""),
("""    public async Task<Message> ProcessAsync(Message message)
    {
""",
"""    public async Task<Message> ProcessAsync(Message message)
    {
        // A SPEECH MODEL THAT IS A NETWORK: each text of the Text Object said with it, one
        // speech file for each (the Speech Restoration System puts each in its place).
        if (_modelPort.Length > 0 && message.Ports.TryGetValue(_modelPort, out var modelJson) && !string.IsNullOrWhiteSpace(modelJson))
        {
            try { return await SpeakWithNetworkAsync(message, NetworkOf(modelJson)); }
            catch (Exception ex) when (ex is FormatException or InvalidOperationException or IOException)
            { return Message.Error(message.MessageId, InstanceId, "MMC-TTS-V2.5: " + ex.Message); }
        }
"""),
("""    // Map the Speech Personal Status emotion to Piper prosody flags""",
"""    private async Task<Message> SpeakWithNetworkAsync(Message message, string network)
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

    // The file of the network an ML Model Object (AIP-MLO-V1.0) points to: MLModelData {DataLength, DataURI}, a file: URI of this machine, the voice's
    // .onnx with its .onnx.json beside it. A Speech Model that is not a network (numbers in its Data) is refused: this Text To Speech cannot speak with it.
    // Fetching a payload that is not on this machine is the Controller's (MPAI_AIFM_Payload_Get), not done here.
    public static string NetworkOf(string modelJson)
    {
        JsonObject? root;
        try { root = JsonNode.Parse(modelJson) as JsonObject; }
        catch (System.Text.Json.JsonException ex) { throw new FormatException("The Speech Model is not JSON: " + ex.Message); }
        var item = (root?["MLModelData"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(o => o["DataURI"] is not null)
                   ?? throw new FormatException("The Speech Model is not a network (it has no DataURI): this Text To Speech cannot speak with it.");
        if (!Uri.TryCreate(item["DataURI"]!.GetValue<string>(), UriKind.Absolute, out var uri) || !uri.IsFile)
            throw new FormatException($"The network of the Speech Model is at {item["DataURI"]}: this Implementation reads a network only from a file of this machine.");
        var path = uri.LocalPath;
        if (!File.Exists(path) || !File.Exists(path + ".json")) throw new FormatException($"The network {path} (or its {Path.GetFileName(path)}.json) does not exist.");
        if (item["DataLength"] is JsonValue length && length.GetValue<long>() != new FileInfo(path).Length)
            throw new FormatException($"The network {path} is not the one the Speech Model describes (DataLength {length}, the file has {new FileInfo(path).Length} bytes).");
        return path;
    }

    // Map the Speech Personal Status emotion to Piper prosody flags""")])
print("ok")
