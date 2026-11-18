using System.Text;
using System.Text.Json.Nodes;

using Mpai.Aif.PortData;
using Mpai.Cae.Usc.Smc;
using Mpai.Cae.Usc.Voice;
using Mpai.Ua;

namespace Mpai.Aif.Tests;

// THE SPEECH RESTORATION SYSTEM IN THE MAS APP (N-27): one workflow for the stand-alone App and the MAS App; ready-made damaged recordings of the LJ Speech reader
// with their sections; the networks trained offline on 1, 5, 15, 30 and 60 minutes of her speech, chosen by name; the table of damaged sections.
[Trait("Group", "Fast")]
public class MasSrsAppTests
{
    private static IEnumerable<Mpai.Wdl.Step> All(IEnumerable<Mpai.Wdl.Step> s) => s.SelectMany(x => new[] { x }.Concat(All(x.Body)).Concat(All(x.Else)).Concat(All(x.Alternatives)));
    private static readonly string Srs = Path.Combine(Repository.Root, "Apps", "CAE", "SRS");

    // The workflow reads; it offers a WAV file or six ready-made damaged recordings, each a file of the App (a WAV a client can read) with its sections beside it,
    // then the six networks by name, all with the speech of the reader; it asks for the Damaged List and the words of the table.
    [Fact]
    public void TheWorkflowOffersReadyMadeRecordingsWithTheirSectionsAndTheSixNetworks()
    {
        var workflow = new Mpai.Wdl.WorkflowReader().Read(File.ReadAllText(Path.Combine(Srs, "SRS.orch")));
        var acquired = All(workflow.OnStart).Where(s => s.Kind == Mpai.Wdl.StepKind.Acquire).ToList();
        var examples = acquired.Select(s => SpeechWav.Example(s.Qualifier)).OfType<(string Label, string Uri)>().Distinct().ToList();      // an acquisition with alternatives holds its first one too
        var recordings = examples.Where(e => e.Uri.Contains("/damaged/")).ToList();
        var networks = examples.Where(e => e.Uri.Contains("/voice/")).Select(e => e.Label).ToList();
        Assert.Equal(6, recordings.Count);
        Assert.Equal(["LJ-01min", "LJ-05min", "LJ-15min", "LJ-30min", "LJ-60min", "LJ-00min"], networks);
        foreach (var (label, uri) in examples)
        {
            Assert.StartsWith("Apps/SRS/Files/", uri);
            var file = Path.Combine(Srs, "Files", uri["Apps/SRS/Files/".Length..].Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(file), $"{label}: {file}");
            Assert.True(SpeechWav.Pcm16kMono(File.ReadAllBytes(file))!.Length > 16000 * 2 * 3, $"{label}: more than three seconds of speech");
        }
        foreach (var (label, uri) in recordings)
        {
            var rows = DamagedSections.FromSidecar(File.ReadAllText(Path.Combine(Srs, "Files", DamagedSections.SidecarOf(uri)["Apps/SRS/Files/".Length..])));
            Assert.Equal(2, rows.Count);
            Assert.Null(DamagedSections.Problem(rows));
        }
        Assert.Contains(acquired, s => s.Port!.DataType == "CAE-DLS-V2.5");
        Assert.Contains(acquired, s => s.Port!.DataType == "OSD-BTO-V1.5" && (s.Qualifier ?? "").Contains(DamagedSections.WordsMarker));
        Assert.Contains(acquired, s => (s.Qualifier ?? "").Contains("\"WAV\""));
    }

    // The table gives a Damaged List valid against its schema, which the Speech Restoration Assembly reads back as the same sections, and a Text List of the words in
    // the same order; a table that is not right says why.
    [Fact]
    public void TheTableGivesAValidDamagedListAndTheWordsInOrder()
    {
        var rows = new List<DamagedSections.Row> { new(0.52, 1.21, "proved by"), new(4.8, 5.92, "realistic article") };
        var list = DamagedSections.DamagedList(rows);
        Assert.Empty(PortDataSchema.Violations("CAE-DLS-V2.5", list) ?? []);
        var read = Mpai.Cae.Usc.Sra.SraAimProcessor.Sections(list);
        Assert.Equal([0.52, 4.8], read.Select(s => s.Start));
        Assert.Equal([1.21, 5.92], read.Select(s => s.End));
        var texts = JsonNode.Parse(DamagedSections.TextList(rows))!;
        Assert.Equal(["proved by", "realistic article"], texts["BasicTextData"]!.AsArray().Select(t => t!["Data"]!.GetValue<string>()));
        Assert.Empty(PortDataSchema.Violations("OSD-BTO-V1.5", DamagedSections.TextList(rows)) ?? []);

        Assert.Contains("end after it starts", DamagedSections.Problem([new(2, 1, "x")]));
        Assert.Contains("words", DamagedSections.Problem([new(1, 2, " ")]));
        Assert.Contains("left to right", DamagedSections.Problem([new(1, 3, "a"), new(2, 4, "b")]));
        Assert.Contains("at least one", DamagedSections.Problem([]));
    }

    // The Damaged List crosses the MPAI-MAS Service as the instance of its schema.
    [Fact]
    public void ADamagedListCrossesTheServiceWhole()
    {
        var codecs = PortDataCodecs.Default();
        Assert.True(codecs.Knows("CAE-DLS-V2.5"));
        var list = DamagedSections.DamagedList([new(1, 2, "a b")]);
        var codec = new JsonObjectCodec("CAE-DLS-V2.5");
        Assert.Equal(JsonNode.Parse(list)!.ToJsonString(), JsonNode.Parse(codec.ToInternal(codec.ToWire(list)))!.ToJsonString());
    }

    // SPEECH MODEL CREATION DONE OFFLINE: Speech Segments for Modelling named as a network of the folder are given that network, named by its ID (MLModelData DataID:
    // model data of the deployment - no AIM opens a file named in data), in an ML Model Object valid against its schema; the Text To Speech finds the network by that ID
    // in its own folder of networks; segments of another name are modelled as the deployment says (here the stand-in); a name with no network is refused.
    [Fact]
    public void SpeechModelCreationGivesTheNetworkTrainedOfflineOnTheNamedSegments()
    {
        var folder = Path.Combine(Path.GetTempPath(), "srs-networks-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(folder, "LJ-05min"));
        File.WriteAllBytes(Path.Combine(folder, "LJ-05min", "LJ-05min.onnx"), new byte[1234]);
        File.WriteAllText(Path.Combine(folder, "LJ-05min", "LJ-05min.onnx.json"), "{}");
        File.WriteAllText(Path.Combine(folder, "LJ-05min", "times.txt"), "start 2026-11-18 steps=2200\n");
        try
        {
            var speech = new Mpai.Osd.Speech.Audio(new short[16000], 16000);
            var trainer = new OfflineNetworkOr(folder, new StandInSpeechTrainer());
            var model = trainer.Train([speech], "LJ-05min");
            var json = SmcAimProcessor.ModelObject(model).ToJsonString();
            Assert.Empty(PortDataSchema.Violations("AIP-MLO-V1.0", json) ?? []);
            Assert.Equal("LJ-05min", (string)JsonNode.Parse(json)!["MLModelData"]![0]!["DataID"]!);
            Assert.Equal("LJ-05min", Mpai.Aims.Tts.TtsAimProcessor.NetworkOf(json));
            Assert.Equal(Path.Combine(folder, "LJ-05min", "LJ-05min.onnx"),                                                     // the Text To Speech finds it in its folder
                         Mpai.Aims.Tts.TtsFactory.NetworkFile(new Dictionary<string, string> { ["SpeechNetworks"] = folder }, "LJ-05min"));

            Assert.Equal(StandInSpeechTrainer.MethodName, trainer.Train([speech], "voice").Method);
            Assert.Throws<ArgumentException>(() => new NetworkSpeechTrainer(folder).Train([speech], "LJ-99min"));
        }
        finally { Directory.Delete(folder, true); }
    }

    // The Text To Speech takes a network only by a plain name, of a network its folder holds; a model in its Data is not a network.
    [Fact]
    public void TheTextToSpeechTakesOnlyANetworkOfItsFolderByName()
    {
        string Model(string id) => new JsonObject { ["Header"] = "AIP-MLO-V1.0", ["MLModelObjectID"] = "m", ["MLModelData"] = new JsonArray(new JsonObject { ["DataID"] = id }) }.ToJsonString();
        Assert.Equal("LJ-60min", Mpai.Aims.Tts.TtsAimProcessor.NetworkOf(Model("LJ-60min")));
        Assert.Throws<FormatException>(() => Mpai.Aims.Tts.TtsAimProcessor.NetworkOf(Model("../../secret")));
        Assert.Throws<FormatException>(() => Mpai.Aims.Tts.TtsAimProcessor.NetworkOf("""{ "Header": "AIP-MLO-V1.0", "MLModelObjectID": "m", "MLModelData": [ { "Data": "AAAA" } ] }"""));
        Assert.Throws<InvalidOperationException>(() => Mpai.Aims.Tts.TtsFactory.NetworkFile(new Dictionary<string, string> { ["SpeechNetworks"] = Path.GetTempPath() }, "LJ-99min"));
    }
}

// THE STAND-ALONE APP WITH A NETWORK TRAINED OFFLINE (N-27): the workflow of the MAS App answered from the command line; a held-out damaged clip of the reader
// restored with LJ-00min (the generator before it heard her) and, where it is there, LJ-60min. Skipped where the networks (Models\SRS-LJ) are not installed.
[Trait("Group", "Slow")]
public class SrsAppNetworkTests
{
    [SkippableTheory]
    [InlineData("LJ-00min")]
    [InlineData("LJ-60min")]
    public void TheAppRestoresAHeldOutClipWithTheNetworkNamed(string network)
    {
        var folder = Path.Combine(Repository.Root, "Models", "SRS-LJ", network);
        Skip.IfNot(File.Exists(Path.Combine(folder, network + ".onnx")), $"the network {network} is not in Models/SRS-LJ");
        var data = Path.Combine(Repository.Root, "Test", "Data", "Speech", "LJSpeech");
        var damaged = Path.Combine(data, "damaged", "LJ016-0277.wav");
        var output = Path.Combine(Path.GetTempPath(), $"srs-{network}-{Guid.NewGuid():N}.wav");
        var info = new System.Diagnostics.ProcessStartInfo(Path.Combine(Repository.Root, "Apps", "CAE", "SRS", "App", "bin", "Debug", "net10.0", "SrsApp.exe"))
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = Repository.Root };
        foreach (var a in new[] { "--network", network, "--voice" }.Concat(Directory.GetFiles(Path.Combine(data, "validation"), "*.wav").Take(2))
                     .Concat(["--damaged", damaged, "--sections", "0.52-1.21,4.8-5.92", "--say", "proved by", "--say", "realistic article", "--out", output])) info.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(info)!;
        var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(TimeSpan.FromMinutes(3)), "the App did not finish");
        Assert.True(p.ExitCode == 0, o.Result + e.Result);
        try
        {
            var restored = Mpai.Osd.Speech.Audio.FromWav(File.ReadAllBytes(output));
            var before = Mpai.Osd.Speech.Audio.FromWav(File.ReadAllBytes(damaged));
            Assert.InRange(restored.Seconds, before.Seconds - 1.81 + 0.5, before.Seconds - 1.81 + 3.5);     // less the two sections (1.81 s), plus the two spoken texts
            Assert.Equal(before.Samples[1000], restored.Samples[1000]);                                     // before the first section it is the recording
        }
        finally { File.Delete(output); }
    }
}
