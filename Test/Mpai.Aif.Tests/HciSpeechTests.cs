using System.Text.Json;
using System.Text.Json.Nodes;

using AIF.Controller;
using Mpai.Aif.Api;
using Mpai.Cav.Hci;
using Mpai.Cav.Map;
using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Aif.Tests;

// STEP 3 (M3243 3.3): HCI HEARING AND SPEAKING. The Module 1CAV-HCI-V2.0-I01 under
// its Controller, every Sub-AIM built; given, turn by turn, what the passenger says -
// recorded speech, synthesised with a voice that is not the CAV's, as the cabin's
// microphone gives it - and what the AMS knows, as Domain Knowledge (MMC-DKN): the
// places of its Offline Map, the Route it proposed, the state of the Route driven.
// The speech path runs: Audio Qualifier Conversion, the audio scene, Audio Source
// separation, Automatic Speech Recognition, Natural Language Understanding, Domain
// Knowledge Retrieval, EDP, and Response and Scene Rendering speaking the answer. Two
// passengers, two voices.
// REWRITTEN 2026-11-28 FOR RAG (M3328 3.7): what the passenger asks leaves HCI as the
// Passenger Request - the items of the CAV's Domain Knowledge the request names
// (Relevance 1): an action of CAV-TEC (HCIMessage.RequestedRoutes, ...RouteCommand:
// Execute) and what it is about (the Way Point of a place). Before, HCI sent an
// AMS-HCI Message made by JSI, and the AMS told it the Routes the same way.
// Judged: a place asked for named in the request; a place named ambiguously not
// chosen; the Route proposed spoken with its time, the arrival with the place - what
// the CAV says, heard by speech recognition; yes understood as Execute; every
// Passenger Request valid. Reported: what was recognised, what was answered, what was
// requested, whether the CAV asked which station (the language model's choice), the
// time of a turn.
[Trait("Group", "Models")]
[Trait("Blocks", "Yes")]
[Collection(Timing.Name)]
public class HciSpeechTests
{
    private const string Hci = HciProvider.Hci;
    private static readonly string[] Voices = ["en_GB-alan-medium", "en_US-lessiac-medium"];

    // The settings of an AIM as its Controller gives them: the settings with the files of Access.
    private static Dictionary<string, string> Settings(string aim) => TestModels.Settings(aim);

    // What a passenger says, as the cabin's microphone gives it: a Basic Audio Object
    // of PCM samples, at the rate the voice speaks.
    public static string Passenger(string text, string voice)
    {
        var settings = TestModels.Voice(voice);
        var wav = Mpai.Aims.Tts.TtsFactory.Create(settings).ProcessAsync(BasicTextObject.FromText(text)).GetAwaiter().GetResult().Data;
        var (rate, pcm) = Pcm(wav);
        var qualifier = new AudioQualifier
        {
            AudioQualifierID = Guid.NewGuid().ToString(),
            Formats = new AudioFormats { ContentFormat = new AudioContentFormat { RawData = new AudioRawData { SampleSpace = new Pcm { SamplingFrequency = rate, Precision = 16 } } } },
            Attributes = new AudioAttributes { Source = "Real", Device = new AudioDevice { DeviceRole = "Capture", DeviceType = "Microphone", CaptureConfiguration = new CaptureConfiguration { ChannelCount = 1, SamplingMode = "Mono" } } }
        };
        return MpaiJson.ToJson(BasicAudioObject.FromData(pcm, qualifier));
    }

    // The sampling rate and samples of a 16-bit mono WAV.
    private static (int Rate, byte[] Pcm) Pcm(byte[] wav)
    {
        var rate = BitConverter.ToInt32(wav, 24);
        for (var pos = 12; pos + 8 <= wav.Length;)
        {
            var id = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
            var length = BitConverter.ToInt32(wav, pos + 4);
            if (id == "data") return (rate, wav[(pos + 8)..Math.Min(wav.Length, pos + 8 + length)]);
            pos += 8 + length + (length & 1);
        }
        return (rate, wav[44..]);
    }

    private sealed class Cabin : IDisposable
    {
        private readonly ControllerApi api;
        private readonly string location = Path.Combine(Path.GetTempPath(), "mpai-p11-hci-" + Guid.NewGuid().ToString("N"));
        public readonly List<string> Turns = [];
        public readonly List<JsonNode> Sent = [];
        public readonly List<double> Seconds = [];

        public Cabin()
        {
            api = new ControllerApi(Repository.Amds, Path.Combine(Repository.Root, "AIMs", "aim-settings.json"), store => new HciProvider(store, Repository.Root));
            var started = api.StartFlow(Hci, out moduleId);
            if (started != AifError.OK) throw new InvalidOperationException($"{Hci} did not start: {started}");
            api.SharedStorageInit(moduleId, location);
        }

        // The MODULE_ID the Controller returned when it started HCI.
        private readonly int moduleId;

        public (string? Recognised, string? Reply, BasicSpeechObject? Speech, JsonNode? Sent) Turn(string said, params ControllerApi.Datum[] inputs)
        {
            // What the cabin senses goes with its LiDAR frame, as the HCI App's does - empty here.
            var all = inputs.Any(i => i.DataType == "OSD-BAO-V1.5")
                ? inputs.Append(new ControllerApi.Datum("OSD-BLO-V1.5", 1, MpaiJson.ToJson(new BasicLiDARObject { BasicLiDARObjectID = Guid.NewGuid().ToString("N"), BasicLiDARData = [] }))).ToList()
                : inputs.ToList();
            all.Add(TestAvatar.Datum);   // the Avatar the reply animates
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var r = api.Advance(moduleId, all);
            Seconds.Add(clock.Elapsed.TotalSeconds);
            if (r.Error != AifError.OK) throw new InvalidOperationException($"{said}: {r.Error} {JsonSerializer.Serialize(api.Status(moduleId).Aims)}");
            var recognised = r.ByType("OSD-BTO-V1.5", 2) is { } t2 ? MpaiJson.FromJson<BasicTextObject>(t2)?.GetText() : null;
            var reply = r.ByType("OSD-BTO-V1.5", 1) is { } t1 ? MpaiJson.FromJson<BasicTextObject>(t1)?.GetText() : null;
            var speech = TestAvatar.Speech(r);
            var sent = r.ByType("MMC-DKN-V2.5") is { } a ? JsonNode.Parse(a) : null;          // the Passenger Request
            if (sent is not null) Sent.Add(sent);
            Turns.Add($"{said} | heard: {recognised ?? "-"} | answered: {reply ?? "-"} ({speech?.Data.Length ?? 0} bytes of speech){(sent is null ? "" : " | requested " + Asked(sent))} | {clock.Elapsed.TotalSeconds:0.0} s");
            return (recognised, reply, speech, sent);
        }

        // What the AMS knows, given with the next thing said (the Domain Instance of Domain Knowledge Retrieval).
        private string? knowledge;
        public void Know(JsonObject domainKnowledge) => knowledge = domainKnowledge.ToJsonString();

        public (string? Recognised, string? Reply, BasicSpeechObject? Speech, JsonNode? Sent) Hears(string text, string voice)
        {
            var inputs = new List<ControllerApi.Datum> { new("OSD-BAO-V1.5", 1, Passenger(text, voice)) };
            if (knowledge is not null) { inputs.Add(new("MMC-DKN-V2.5", 1, knowledge)); knowledge = null; }
            return Turn($"\"{text}\" ({voice})", [.. inputs]);
        }

        public void Dispose()
        {
            api.StopFlow(moduleId);
            api.Dispose();
            try { Directory.Delete(location, recursive: true); } catch { }
        }
    }

    // What the CAV said, heard by speech recognition.
    private static string Heard(BasicSpeechObject? speech) =>
        speech is null || speech.Data.Length < 100 ? "" : Mpai.Aims.Asr.AsrFactory.Create(Settings("1MMC-ASR-V2.5-I01")).ProcessAsync(speech).GetAwaiter().GetResult().GetText();

    // What a Passenger Request names: the References of its items of Relevance 1.
    private static List<string> Named(JsonNode? request) =>
        (request?["Items"] as JsonArray ?? []).OfType<JsonObject>().Where(i => (double?)i["Relevance"] >= 1).Select(i => (string?)i["Reference"]).OfType<string>().ToList();
    private static string Asked(JsonNode? request) => Named(request) is { Count: > 0 } n ? string.Join(", ", n) : "nothing";

    // What the AMS knows, as Route Selection Planning gives it: the places of the map and, where there is one, what it proposed or the state of the Route driven.
    private static JsonObject Knowledge(RoadMap map, JsonObject? now = null)
    {
        var k = Mpai.Cav.Ams.RouteSelectionPlanning.Knowledge(map);
        if (now is not null) k["Items"]!.AsArray().Add(now);
        return k;
    }

    [SkippableFact]
    public void Step3HearingAndSpeaking()
    {
        Skip.IfNot(TestModels.Can("ggml-small.bin"), "the Whisper model is absent: the model files are obtained separately.");
        var result = new Dictionary<string, string>();
        var report = new Dictionary<string, object>();
        var sent = new List<JsonNode>();
        var map = RoadMap.Grid(3).Named(CavDialogueTests.Places);
        foreach (var voice in Voices)
        {
            using var cabin = new Cabin();
            cabin.Know(Knowledge(map));
            var (heard, reply, _, s) = cabin.Hears("Please take me to the hospital.", voice);
            var n = Named(s);
            result[$"{voice}: a place"] = n.Contains("HCIMessage.RequestedRoutes") && n.Contains("W20") ? "Routes to its Way Point requested" : $"heard \"{heard}\"; requested {Asked(s)}";
            (heard, reply, _, s) = cabin.Hears("No, take me to the station.", voice);
            n = Named(s);
            result[$"{voice}: a place named ambiguously"] = n.Contains("HCIMessage.RequestedRoutes") && !n.Contains("W21") && !n.Contains("W02") ? "Routes requested, no station chosen" : $"heard \"{heard}\"; requested {Asked(s)}";
            report[$"{voice}: asked which station"] = reply?.Contains('?') == true ? "yes" : $"no: \"{reply}\"";          // what the language model chose to say: not judged
            (heard, reply, _, s) = cabin.Hears("Take me to Central Station.", voice);
            n = Named(s);
            result[$"{voice}: the place named"] = n.Contains("HCIMessage.RequestedRoutes") && n.Contains("W21") ? "Routes to its Way Point requested" : $"heard \"{heard}\"; requested {Asked(s)}";
            // the AMS proposes one Route, 44 s
            cabin.Know(Knowledge(map, Mpai.Cav.Ams.RouteSelectionPlanning.RouteItem("RTE0001", 0, "Central Station", 320, 44)));
            var (_, _, speech, _) = cabin.Hears("How long will it take?", voice);
            var spoken = Heard(speech);
            result[$"{voice}: the Route spoken"] = spoken.Contains("44", StringComparison.Ordinal) || spoken.Contains("forty", StringComparison.OrdinalIgnoreCase) ? "heard with its time" : $"heard \"{spoken}\"";
            (heard, reply, _, s) = cabin.Hears("Yes, let's go.", voice);
            result[$"{voice}: yes"] = Named(s).Contains("HCIMessage.RouteCommand:Execute") ? "the Route executed" : $"heard \"{heard}\"; requested {Asked(s)}";
            // the AMS: arrived
            cabin.Know(Knowledge(map, Mpai.Cav.Ams.RouteSelectionPlanning.StateItem("Arrived", "Central Station", "RTE0001")));
            (_, _, speech, _) = cabin.Hears("Where are we?", voice);
            spoken = Heard(speech);
            result[$"{voice}: the arrival spoken"] = spoken.Contains("Central Station", StringComparison.OrdinalIgnoreCase) ? "heard with the place" : $"heard \"{spoken}\"";
            report[voice] = cabin.Turns;
            report[$"{voice}: a turn"] = $"median {cabin.Seconds.Order().ElementAt(cabin.Seconds.Count / 2):0.0} s, longest {cabin.Seconds.Max():0.0} s";
            sent.AddRange(cabin.Sent);
        }
        var schemas = AIF.Metadata.PublishedSchemas.At(Repository.Schemas);
        var schema = schemas[Path.GetFullPath(Path.Combine(Repository.Schemas, "MMC", "V2.5", "data", "DomainKnowledge.json"))];
        var valid = sent.Count(t => { using var doc = JsonDocument.Parse(t.ToJsonString()); lock (AIF.Metadata.PublishedSchemas.Lock) return schema.Evaluate(doc.RootElement).IsValid; });
        result["every Passenger Request against the schema of Domain Knowledge"] = valid == sent.Count ? "valid" : $"{valid} of {sent.Count} valid";
        File.WriteAllText(Path.Combine(Repository.Root, "Test", "Reports", "cav-stage1-hci-speech.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + Environment.NewLine);
        Expected.Match("cav-stage1-hci-speech.json", result);
    }
}
