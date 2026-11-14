using System.Text;
using System.Text.Json.Nodes;

using Mpai.Aif.PortData;
using Mpai.Core;

namespace Mpai.Aif.Tests;

// ADD EMOTION TO SPEECH AND SPEAK MODEL WITH EMOTION IN THE MAS APP: what crosses the MPAI-MAS Service between a client and the Modules of CAE-USC V2.5 arrives whole.
[Trait("Group", "Fast")]
public class MasCaeAppsTests
{
    // The speech a client captures (16 kHz PCM, in the form of Mpai.Core) reaches a CAE-USC AIM as the Service gives it, and the AIM reads it.
    [Fact]
    public void SpeechAClientCapturesIsReadByTheAimsOfCaeUsc()
    {
        var pcm = new byte[16000 * 2];                                          // one second
        for (var i = 0; i < 16000; i++) BitConverter.TryWriteBytes(pcm.AsSpan(i * 2), (short)(3000 * Math.Sin(i * 0.1)));
        var captured = MpaiJson.ToJson(BasicSpeechObject.FromData(pcm, new SpeechQualifier
        {
            SpeechQualifierID = "q",
            Format = new SpeechFormat { ContentFormats = new SpeechContentFormats { RawData = new Pcm { SamplingFrequency = 16000, Precision = 16 } } }
        }));
        var codec = new BasicSpeechObjectCodec();
        var given = codec.ToInternal(codec.ToWire(captured));                 // client -> Service -> Module
        var audio = Assert.Single(Mpai.Osd.Speech.SpeechObject.Read(given));
        Assert.Equal(16000, audio.Samples.Length);
        Assert.Equal(16000, audio.Rate);
    }

    // The Mode Selector a workflow writes keeps its SelectorID through the Service; a Language Selector keeps its languages in both forms.
    [Fact]
    public void ASelectorWrittenAsASchemaInstanceCrossesTheServiceWhole()
    {
        var codec = new SelectorCodec();
        var mode = """{ "Header": "OSD-SEL-V1.5", "SelectorID": "neural" }""";
        var onWire = JsonNode.Parse(Encoding.UTF8.GetString(codec.ToWire(mode)))!;
        Assert.Equal("neural", onWire["SelectorID"]!.GetValue<string>());
        Assert.Equal("neural", JsonNode.Parse(codec.ToInternal(codec.ToWire(mode)))!["SelectorID"]!.GetValue<string>());

        var language = """{ "Header": "OSD-SEL-V1.5", "Language": { "InputLanguage": "en", "OutputLanguage": "en" } }""";
        var inside = JsonNode.Parse(codec.ToInternal(codec.ToWire(language)))!;
        Assert.Equal("en", inside["Language"]!["OutputLanguage"]!.GetValue<string>());       // as CAE-USC reads it
        Assert.Equal("en", MpaiJson.FromJson<BasicSelectorObject>(inside.ToJsonString())!.OutputLanguage);   // as MPAI-MMC reads it
        Assert.Null(JsonNode.Parse(Encoding.UTF8.GetString(codec.ToWire(inside.ToJsonString())))!["OutputLanguage"]);   // the inside form never goes on the wire

        var old = MpaiJson.ToJson(BasicSelectorObject.Languages("it", "en"));     // what the MMC Apps give, as before
        Assert.Equal("it", JsonNode.Parse(Encoding.UTF8.GetString(codec.ToWire(old)))!["Language"]!["InputLanguage"]!.GetValue<string>());
    }

    // THE READY-MADE MODEL UTTERANCES OF SME: the workflow offers four, by the address where CREMA-D publishes them, each the file kept in Test/Data/Speech/CREMA-D
    // (a WAV a client can read), named Enraged, Sad, Happy and Fearful.
    [Fact]
    public void SpeakModelWithEmotionOffersFourReadyMadeModelsThatAreTheClipsInTestData()
    {
        var workflow = new Mpai.Wdl.WorkflowReader().Read(File.ReadAllText(Path.Combine(Repository.Root, "Apps", "CAE", "SME", "SME.orch")));
        IEnumerable<Mpai.Wdl.Step> All(IEnumerable<Mpai.Wdl.Step> s) => s.SelectMany(x => new[] { x }.Concat(All(x.Body)).Concat(All(x.Else)).Concat(All(x.Alternatives)));
        var examples = All(workflow.OnStart).Where(s => s.Kind == Mpai.Wdl.StepKind.Acquire).Select(s => Mpai.Ua.SpeechWav.Example(s.Qualifier)).OfType<(string Label, string Uri)>().ToList();
        Assert.Equal(["Enraged", "Sad", "Happy", "Fearful"], examples.Select(e => e.Label));
        foreach (var (label, uri) in examples)
        {
            var file = Path.Combine(Repository.Root, "Test", "Data", "Speech", "CREMA-D", Path.GetFileName(new Uri(uri).AbsolutePath));
            Assert.True(File.Exists(file), $"{label}: {file}");
            Assert.True(Mpai.Ua.SpeechWav.Pcm16kMono(File.ReadAllBytes(file))!.Length > 16000 * 2, $"{label}: more than a second of speech");
        }
        Assert.Null(Mpai.Ua.SpeechWav.Example("""{ "BasicSpeechDataQualifier": { "Formats": { "TransportFormats": { "FileFormat": "WAV" } } } }"""));
    }

    // The Emotion an App gives crosses the Service as the instance of its schema.
    [Fact]
    public void AnEmotionCrossesTheService()
    {
        var codecs = PortDataCodecs.Default();
        Assert.True(codecs.Knows("MMC-EEM-V2.5"));
    }

    // Speech given back to hear, in either form a Module gives it, is one WAV; a WAV file a person chooses becomes 16 kHz mono samples.
    [Fact]
    public void SpeechToHearIsOneWavAndAChosenWavBecomes16kMono()
    {
        var stereo44 = Wav(44100, 2, 44100);
        var pcm = Mpai.Ua.SpeechWav.Pcm16kMono(stereo44)!;
        Assert.Equal(16000 * 2, pcm.Length);
        Assert.Null(Mpai.Ua.SpeechWav.Pcm16kMono([1, 2, 3]));

        var asFile = new JsonObject
        {
            ["Header"] = "OSD-BSO-V1.5",
            ["BasicSpeechObjectData"] = new JsonArray(new JsonObject { ["Data"] = Convert.ToBase64String(Wav(16000, 1, 8000)) }, new JsonObject { ["Data"] = Convert.ToBase64String(Wav(16000, 1, 8000)) }),
            ["BasicSpeechDataQualifier"] = new JsonObject { ["Formats"] = new JsonObject { ["TransportFormats"] = new JsonObject { ["FileFormat"] = "WAV" } } }
        }.ToJsonString();
        var wav = Mpai.Ua.SpeechWav.ToWav(asFile)!;
        Assert.Equal(44 + 16000 * 2, wav.Length);                                 // the two pieces, one after the other

        // the speech a CAE-USC Module gives, as the client receives it through the Service, is played
        var made = Mpai.Osd.Speech.SpeechObject.Make("result", new Mpai.Osd.Speech.Audio(new short[16000], 16000));
        var codec = new BasicSpeechObjectCodec();
        var received = codec.ToInternal(codec.ToWire(made));
        Assert.Equal(44 + 16000 * 2, Mpai.Ua.SpeechWav.ToWav(received)!.Length);
    }

    private static byte[] Wav(int rate, short channels, int frames)
    {
        using var m = new MemoryStream();
        using var w = new BinaryWriter(m);
        w.Write("RIFF"u8); w.Write(36 + frames * channels * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write(channels); w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write("data"u8); w.Write(frames * channels * 2);
        for (var i = 0; i < frames * channels; i++) w.Write((short)(1000 * Math.Sin(i * 0.05)));
        w.Flush();
        return m.ToArray();
    }
}
