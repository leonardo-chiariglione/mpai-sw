using System.Collections.Concurrent;
using System.Text.Json.Nodes;

using AIF.Controller;
using AIF.Store;
using Mpai.Aif.Api;
using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Osd.Tma;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Mpai.Aif.Tests;

// TELEVISION MEDIA ANALYSIS ON THE CONTROLLER (OSD-TMA V1.5, M-author 2026-11-05): the composite 1OSD-TMA-V1.5-I01 started by the Controller API on its L3s, the real AIMs behind it
// (the new ones of Mpai.Osd.Tma; audio segmentation, face and speaker recognition, speech recognition, identity reconciliation and the visual scene description of the repository).
//
// THE PROGRAMME is synthetic, as the author asked: three people, head and shoulders, speak in a random order, sometimes overlapping. The faces are pictures of Labeled Faces in the
// Wild and the voices are LibriSpeech clips (Test\Data); each person is enrolled in the gallery from one picture and from the start of one clip, and the programme is made of
// other pictures and of later pieces of the clips. The Auxiliary Text names the three. The audio is the mix of the turns, the video the picture of the one who begins to speak,
// at 10 frames a second.
[Trait("Group", "Slow")]
[Trait("Blocks", "No")]
public class TmaEndToEndTests : IDisposable
{
    private const string Module = "1OSD-TMA-V1.5-I01";
    private const double Fps = 10;
    private static readonly string[] SubAims =
    [
        "1CVE-VCD-V1.0-I01", "1CAE-AUS-V1.0-I01", "1OSD-FBO-V1.5-I01", "1PAF-FIR-V1.6-I02", "1MMC-SIR-V2.5-I02", "1MMC-ASR-V2.5-I02",
        "1OSD-IDR-V1.5-I01", "1OSD-BSS-V1.5-I01", "1OSD-BVS-V1.5-I01", "1OSD-MSM-V1.5-I01", "1OSD-MED-V1.5-I01"
    ];

    // The three people: who they are in the gallery, the picture and the clip they are enrolled from; the pictures after the first are the programme's.
    private static readonly (string Name, string Clip)[] People =
    [
        ("Bill_Clinton", "clip2803"), ("Angelina_Jolie", "clip422"), ("Arnold_Schwarzenegger", "clip3576")
    ];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tma-e2e-" + Guid.NewGuid().ToString("N"));
    private static string Data(params string[] parts) => Path.Combine([Repository.Root, "Test", "Data", .. parts]);
    private static string Models(params string[] parts) => Path.Combine([Repository.Root, "Models", .. parts]);

    public void Dispose() { try { Directory.Delete(_folder, recursive: true); } catch { } }

    // ---- what each AIM gave, as the Controller handed it on ----------------------------------------------------------------------------------------

    private sealed class Recorder(IAimProcessor inner, ConcurrentQueue<(string Aim, string Port, string Json)> log) : IAimProcessor
    {
        public string InstanceId => inner.InstanceId;
        public async Task<Message> ProcessAsync(Message message)
        {
            log.Enqueue((InstanceId, "IN " + string.Join(",", message.Ports.Keys), ""));
            Message reply;
            try { reply = await inner.ProcessAsync(message); }
            catch (Exception ex) { log.Enqueue((InstanceId, "EXCEPTION", ex.ToString())); throw; }
            if (reply?.MessageType == Message.ErrorType) log.Enqueue((InstanceId, "ERROR", reply.Payload ?? ""));
            if (reply?.Ports is not null) foreach (var (port, json) in reply.Ports) log.Enqueue((InstanceId, port, json));
            return reply!;
        }
    }

    private sealed class Provider : IAimProvider, IDisposable
    {
        private readonly AmdStore _store;
        private readonly SubjectGallery _gallery;
        private readonly ConcurrentQueue<(string Aim, string Port, string Json)> _log;
        private Mpai.Mmc.Sir.SpeakerEmbedder? _ecapa;
        private Mpai.Paf.Fir.ArcFaceRecogniser? _arcFace;
        private Mpai.Osd.VisualScene.ScrfdFaceDetector? _scrfd;

        public Provider(AmdStore store, SubjectGallery gallery, ConcurrentQueue<(string, string, string)> log) { _store = store; _gallery = gallery; _log = log; }

        public bool CanCreate(string aimName) => SubAims.Contains(aimName);

        public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings, AIF.SharedStorage.ISharedStorage? storage)
        {
            var ports = AimPortReader.Load(_store, aimName);
            IAimProcessor aim = aimName switch
            {
                "1CVE-VCD-V1.0-I01" => new VisualChangeDetection(aimName, settings),
                "1OSD-FBO-V1.5-I01" => new FullToBasicObject(aimName),
                "1OSD-BSS-V1.5-I01" => new BasicSpeechSceneDescription(aimName),
                "1OSD-MSM-V1.5-I01" => new MultimodalSceneMultiplexing(aimName),
                "1OSD-MED-V1.5-I01" => new MultimodalEventDescription(aimName),
                "1CAE-AUS-V1.0-I01" => new Mpai.Cae.Aus.AusAimProcessor(aimName, Ecapa(settings), ports),
                "1PAF-FIR-V1.6-I02" => new Mpai.Paf.Fir.FirEachFace(aimName, Scrfd(settings), ArcFace(settings), _gallery),     // every face of every sampled frame, where and when
                "1MMC-SIR-V2.5-I02" => new Mpai.Mmc.Sir.SirAimProcessor(aimName, Ecapa(settings), _gallery, ports),
                "1MMC-ASR-V2.5-I02" => new Mpai.Aims.Asr.AsrAimProcessor(aimName, Mpai.Aims.Asr.AsrFactory.Create(settings), ports),
                "1OSD-IDR-V1.5-I01" => new IdentityReconciliationEach(aimName),          // as TMA needs it: each identity answered as it arrives
                "1OSD-BVS-V1.5-I01" => new Mpai.Osd.Bvs.BvsAimProcessor(aimName, ports),
                _ => throw new NotSupportedException(aimName)
            };
            return aim is TmaAim or Mpai.Paf.Fir.FirEachFace ? aim : new Recorder(aim, _log);
        }

        private Mpai.Mmc.Sir.SpeakerEmbedder Ecapa(IReadOnlyDictionary<string, string> s) => _ecapa ??= new(MpaiPaths.Given(s, "EcapaModel"));
        private Mpai.Paf.Fir.ArcFaceRecogniser ArcFace(IReadOnlyDictionary<string, string> s) => _arcFace ??= new(MpaiPaths.Given(s, "ArcFaceModel"));
        private Mpai.Osd.VisualScene.ScrfdFaceDetector Scrfd(IReadOnlyDictionary<string, string> s) => _scrfd ??= new(MpaiPaths.Given(s, "ScrfdModel"));
        public void Dispose() { _ecapa?.Dispose(); _arcFace?.Dispose(); _scrfd?.Dispose(); }
    }

    // ---- the programme ------------------------------------------------------------------------------------------------------------------------

    private sealed record Turn(int Person, double Start, double End);

    private static float[] Clip(string name) => Mpai.Mmc.Sir.WavReader.ReadMono16k(Data("Speech", name + ".wav"));

    // Six turns of about 3 s in a random order of the three (the same seed gives the same programme); two of them begin 1 s before the one before ends.
    private static (List<Turn> Turns, float[] Audio) Audio(int seed)
    {
        var rnd = new Random(seed);
        var order = new List<int>();
        while (order.Count < 6) { var p = rnd.Next(3); if (order.Count == 0 || order[^1] != p) order.Add(p); }
        var clips = People.Select(p => Clip(p.Clip)).ToArray();
        var used = new double[3] { 4, 4, 4 };                                  // the first 4 s of each clip enrol the person
        var turns = new List<Turn>();
        double t = 0;
        for (var i = 0; i < order.Count; i++)
        {
            var begin = i > 0 && i % 2 == 0 ? t - 1.0 : t;                    // an overlap at the third and the fifth turn
            var length = 3.0;
            turns.Add(new Turn(order[i], begin, begin + length));
            t = begin + length;
        }
        var total = (int)((t + 0.5) * 16000);
        var mix = new float[total];
        foreach (var turn in turns)
        {
            var clip = clips[turn.Person]; var from = (int)(used[turn.Person] * 16000); used[turn.Person] += 3.0;
            var to = (int)(turn.Start * 16000);
            for (var k = 0; k < 3 * 16000 && from + k < clip.Length && to + k < total; k++) mix[to + k] += 0.7f * clip[from + k];
        }
        return (turns, mix);
    }

    private static byte[] Wav(float[] samples)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            var data = samples.Length * 2;
            w.Write("RIFF"u8.ToArray()); w.Write(36 + data); w.Write("WAVEfmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(16000); w.Write(32000); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8.ToArray()); w.Write(data);
            foreach (var s in samples) w.Write((short)Math.Clamp(s * 32767, -32768, 32767));
        }
        return ms.ToArray();
    }

    private static string Picture(int person, int index) =>
        Directory.GetFiles(Data("Images", "lfw", People[person].Name)).OrderBy(f => f, StringComparer.Ordinal).ElementAt(index);

    // Each person is shot in front of a backdrop of their own, so that a change of speaker is a change of shot, as a cut between cameras is.
    private static readonly Rgb24[] Backdrops = [new Rgb24(30, 60, 170), new Rgb24(170, 50, 50), new Rgb24(40, 150, 60)];

    // A head-and-shoulders frame of a television picture: the picture of the person (94 x 125 in the collection) enlarged and placed on a 640 x 480 frame, as a camera gives it.
    private static byte[] Frame(int person, string pictureFile, float brightness = 1f)
    {
        using var face = Image.Load<Rgb24>(pictureFile);
        using var frame = new Image<Rgb24>(640, 480, Backdrops[person]);
        face.Mutate(c => c.Resize(282, 375).Brightness(brightness));
        frame.Mutate(c => c.DrawImage(face, new Point((640 - 282) / 2, 60), 1f));
        using var ms = new MemoryStream();
        frame.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    private static JsonObject Video(List<Turn> turns, double seconds)
    {
        var frames = new JsonArray();
        var rnd = new Random(7);
        for (var f = 0; f < (int)(seconds * Fps); f++)
        {
            var time = f / Fps;
            var turn = turns.Last(t => t.Start <= time + 1e-9);               // the one who began to speak last
            frames.Add(new JsonObject { ["Data"] = Convert.ToBase64String(Frame(turn.Person, Picture(turn.Person, 1), 1f + (float)(rnd.NextDouble() - 0.5) * 0.02f)) });
        }
        return new JsonObject
        {
            ["Header"] = "OSD-BVO-V1.5", ["MInstanceID"] = "tma-test", ["BasicVisualObjectID"] = "programme-video",
            ["BasicVisualObjectData"] = frames, ["BasicVisualObjectProperties"] = new JsonObject { ["FrameRate"] = Fps }
        };
    }

    private static JsonObject SpeechObject(byte[] wav) => new JsonObject { ["Header"] = "OSD-BSO-V1.5" }.With(wav);

    // The sound of a programme as TV Splitting gives it: a Basic Speech Object whose time is that of the programme, from 0 (a MEV knows no other time).
    private static JsonObject ProgrammeSpeech(byte[] wav, double seconds)
    {
        var o = SpeechObject(wav);
        o["BasicSpeechObjectTime"] = new JsonObject { ["Header"] = "OSD-STM-V1.5", ["SimpleTimeID"] = "programme",
            ["SimpleTimeData"] = new JsonArray(new JsonObject { ["FlagsByte"] = 0, ["StartTime"] = 0.0, ["EndTime"] = seconds, ["AccuracyMode"] = "single", ["AccuracyPlusMinus"] = 0.0, ["TimeType"] = false, ["TimeUnit"] = "00" }) };
        return o;
    }

    private static string AuxiliaryText() =>
        "A talk with " + string.Join(", ", People.Select(p => p.Name.Replace('_', ' '))) + ".";

    // ---- the run -----------------------------------------------------------------------------------------------------------------------------

    private string Prepare()
    {
        Directory.CreateDirectory(_folder);
        foreach (var aim in SubAims.Append(Module))
        {
            // The models are given by the settings of the test, from Models\: the L3s as the repository has them name remote AccessSources that the test does not fetch.
            var l3 = JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Amds, aim + ".json")))!.AsObject();
            l3.Remove("AccessSources");
            File.WriteAllText(Path.Combine(_folder, aim + ".json"), l3.ToJsonString());
        }
        var settings = new JsonObject
        {
            ["1CAE-AUS-V1.0-I01"] = new JsonObject { ["EcapaModel"] = Models("ecapa-tdnn.onnx") },
            ["1MMC-SIR-V2.5-I02"] = new JsonObject { ["EcapaModel"] = Models("ecapa-tdnn.onnx") },
            ["1PAF-FIR-V1.6-I02"] = new JsonObject { ["ScrfdModel"] = Models("scrfd_10g_bnkps.onnx"), ["ArcFaceModel"] = PinnedFaceModel() ?? Models("glintr100.onnx") },
            ["1MMC-ASR-V2.5-I02"] = new JsonObject { ["ExecutablePath"] = Models("Whisper", "bin", "whisper-cli.exe"), ["ModelPath"] = Models("Whisper", "models", "ggml-small.bin"), ["LanguageCode"] = "en" },
            ["1CVE-VCD-V1.0-I01"] = new JsonObject { ["ImagesPerScene"] = "1", ["MinSceneLength"] = "15", ["SampleSeconds"] = "1" }
        };
        var path = Path.Combine(_folder, "settings.json");
        File.WriteAllText(path, settings.ToJsonString());
        return path;
    }

    // Each person enrolled from the first picture and from the first 4 s of the clip.
    private static SubjectGallery Gallery()
    {
        var gallery = new SubjectGallery();
        using var ecapa = new Mpai.Mmc.Sir.SpeakerEmbedder(Models("ecapa-tdnn.onnx"));
        using var arc = new Mpai.Paf.Fir.ArcFaceRecogniser(PinnedFaceModel() ?? Models("glintr100.onnx"));     // the model FIR is given (the settings above): enrolled and recognised by one model
        using var scrfd = new Mpai.Osd.VisualScene.ScrfdFaceDetector(Models("scrfd_10g_bnkps.onnx"));
        for (var p = 0; p < People.Length; p++)
        {
            var bytes = Frame(p, Picture(p, 0));
            var face = scrfd.Detect(bytes).OrderByDescending(f => f.Width * f.Height).FirstOrDefault();
            using var crop = face is null ? Image.Load<Rgb24>(bytes) : Mpai.Paf.Fir.FaceCrop.Crop(bytes, face.X1, face.Y1, face.X2, face.Y2);
            var voice = ecapa.Embed(Clip(People[p].Clip).Take(4 * 16000).ToArray());
            gallery.EnrolEmbeddings(People[p].Name, arc.Embed(crop), voice);
        }
        return gallery;
    }

    private static string Record(string line)
    {
        var folder = Path.Combine(Repository.Root, "Test", "Results", "2026-11-06");
        Directory.CreateDirectory(folder);
        File.AppendAllText(Path.Combine(folder, "tma-e2e.txt"), line + "\n");
        return line;
    }

    // THE RUN: the three speakers, six turns, two overlaps; the Module is given the speech, the video and the text, and gives the Multimodal Event Descriptors.
    // What it must do: finish; give Event Descriptors that are valid against their schema and hold the scenes of the speech and of the pictures; and the face recognition and
    // the speaker recognition must have named each of the three at least once. What else it gave is written down for the report (Test\Results\2026-11-06\tma-e2e.txt).
    [SkippableFact]
    public void TheThreeSpeakers()
    {
        Skip.IfNot(File.Exists(Models("ecapa-tdnn.onnx")) && File.Exists(Models("glintr100.onnx")) && File.Exists(Models("Whisper", "bin", "whisper-cli.exe")), "the models are not in Models\\");
        var settings = Prepare();
        var (turns, audio) = Audio(seed: 11);
        var seconds = audio.Length / 16000.0;
        Record($"--- {DateTime.Now:s}: {seconds:F1} s, turns: " + string.Join("; ", turns.Select(t => $"{People[t.Person].Name} {t.Start:F1}-{t.End:F1}")));

        var log = new ConcurrentQueue<(string Aim, string Port, string Json)>();
        using var gallery = new GalleryHolder(Gallery());
        using var api = new ControllerApi(_folder, settings, store => new Provider(store, gallery.Value, log));
        Assert.Equal(AifError.OK, api.StartFlow(Module, out var moduleId));
        var clock = System.Diagnostics.Stopwatch.StartNew();

        Assert.Equal(AifError.OK, api.InputWrite(moduleId, "OSD-TXO-V1.5", 1, new JsonObject { ["Header"] = "OSD-TXO-V1.5", ["TextObjectID"] = "aux", ["BasicTextObjects"] = new JsonArray(Mpai.Core.BasicTextObject.FromText(AuxiliaryText()).ToNode()) }.ToJsonString(), 5000));
        Assert.Equal(AifError.OK, api.InputWrite(moduleId, "OSD-BVO-V1.5", 1, Video(turns, seconds).ToJsonString(), 60000));
        Assert.Equal(AifError.OK, api.InputWrite(moduleId, "OSD-BSO-V1.5", 1, SpeechObject(Wav(audio)).ToJsonString(), 60000));

        // The Event Descriptors grow as the scenes arrive: the last is the whole.
        JsonObject? last = null;
        for (var i = 0; i < 200; i++)
        {
            var read = api.OutputRead(moduleId, "OSD-MEV-V1.5", 1, i == 0 ? 600_000 : 120_000);
            if (read.Json is null) break;
            last = JsonNode.Parse(read.Json)!.AsObject();
        }
        Record($"run: {clock.Elapsed.TotalSeconds:F1} s");
        Assert.NotNull(last);

        var scenes = last!["MultimodalSceneDescriptors"]!.AsArray();
        Record($"Event Descriptors: {scenes.Count} scenes: " +
               $"{scenes.Count(s => s!["BasicMultimodalSceneDescriptorsData"]?[0]?["BXSOrBXSID"]?["BasicSpeechSceneDescriptors"] is not null)} of speech, " +
               $"{scenes.Count(s => s!["BasicMultimodalSceneDescriptorsData"]?[0]?["BXSOrBXSID"]?["BasicVisualSceneDescriptors"] is not null)} of pictures");
        var dump = Path.Combine(Repository.Root, "Test", "Results", "2026-11-06");
        File.WriteAllText(Path.Combine(dump, "tma-last-mev.json"), last.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllLines(Path.Combine(dump, "tma-aim-outputs.txt"), log.Select(l => $"{l.Aim} | {l.Port} | {(l.Json.Length > 600 ? l.Json[..600] + "..." : l.Json)}"));
        var verdict = PgmRig.Check(last);
        Record("valid against the schema: " + verdict);

        var data = log.Where(l => l.Json.Length > 0 && !l.Port.StartsWith("IN ") && l.Port != "ERROR" && l.Port != "EXCEPTION").ToList();
        string Top(string aim, string type) => string.Join(" | ", data.Where(l => l.Aim.Contains(aim) && l.Port.Contains(type)).Select(l =>
            (string?)JsonNode.Parse(l.Json)?["InstanceIdentifierData"]?[0]?["InstanceLabel"] + $" ({(double?)JsonNode.Parse(l.Json)?["InstanceIdentifierData"]?[0]?["LabelConfidenceLevel"]:F2})"));
        // FIR (FirEachFace) runs continuously and is not recorded (the Recorder wraps one-shot AIMs only): the faces it named are read where they arrive, in the
        // Event Descriptors - each face a Basic Visual Object whose Visual Qualifier names its Instance Identifier.
        var faceLabels = new List<string>();
        void Faces(JsonNode? n)
        {
            if (n is JsonObject o)
            {
                if ((string?)o["VisualObjectType"] == "Face" && o["ObjectID"]?["InstanceIdentifierData"]?[0] is JsonObject id)
                    faceLabels.Add((string?)id["InstanceLabel"] + $" ({(double?)id["LabelConfidenceLevel"]:F2})");
                foreach (var (_, v) in o) Faces(v);
            }
            else if (n is JsonArray a) foreach (var v in a) Faces(v);
        }
        Faces(last);
        var faces = string.Join(" | ", faceLabels.Distinct()); var voices = Top("SIR", "OSD-IID");
        Record("faces: " + faces); Record("voices: " + voices);
        Record("texts: " + string.Join(" | ", data.Where(l => l.Aim.Contains("ASR")).Select(l => (string?)JsonNode.Parse(l.Json)?["BasicTextData"]?[0]?["Data"])));
        Record("users: " + string.Join(" | ", data.Where(l => l.Aim.Contains("IDR") && l.Port.Contains("OSD-IID")).Select(l => (string?)JsonNode.Parse(l.Json)?["InstanceIdentifierData"]?[0]?["InstanceLabel"])));

        Assert.Equal("valid", verdict);
        Assert.True(scenes.Count > 0, "no scenes");
        foreach (var (name, _) in People) { Assert.Contains(name, faces); Assert.Contains(name, voices); }
    }

    // ---- A REAL PROGRAMME: the AMI meeting (2026-11-14) ---------------------------------------------------------------------------------------------
    //
    // Test\Data\TMA\AMI-IS1009c-10m00-15m00-side-by-side.mp4: four people at a table, talking among themselves, two cameras side by side, the headset mix as sound
    // (README.md beside it). The video is split as TV Splitting would (it has no implementation yet): its frames at 5 a second, 960 x 540, as one Basic Visual Object;
    // its sound, 16 kHz mono, as one Basic Speech Object; no Text. Nobody is enrolled: TMA says "A speaks, B speaks", not who the people are.
    // What it must do: finish; give Event Descriptors that are valid against their schema and hold scenes. What each AIM gave is written down
    // (Test\Results\2026-11-14\tma-ami*.txt). How much of the programme: MPAI_TMA_AMI_SECONDS (30 by default; the whole is 300).
    [SkippableFact]
    public void TheAmiMeeting() => RunAmi(new SubjectGallery(), "tma-ami", "2026-11-14");

    // ---- THE SAME MEETING, ITS FOUR PARTICIPANTS REGISTERED (2026-11-15) ----------------------------------------------------------------------------
    //
    // The author: "create two (or four if four are relevant) entities and use Access Control Registration to create entries in the speech and face DBs".
    // Access Control Registration (1OSD-ACR-V1.5-I01, the Module of Apps/MMC/ACR, with its own AIMs EFD and ESD) registers A, B, C and D - the speaker
    // letters of the AMI annotation, so that the labels stay anonymous - each from a face (his or her half of a camera picture taken OUTSIDE the clip)
    // and from 10-30 s of his or her own headset OUTSIDE the clip (Data\AMI\IS1009c\enrol, machine-local, made from the AMI annotation). TMA then
    // runs on the meeting with that gallery, which its SIR and FIR search. The reference who-speaks-when of the clip is beside the material
    // (Data\AMI\IS1009c\IS1009c-10m00-15m00-who-speaks-when.csv); the outputs of AUS, SIR, FIR, IDR and ASR are written whole for the comparison.
    [SkippableFact]
    public void TheAmiMeetingWithItsParticipantsRegistered()
    {
        var enrol = Path.Combine(Repository.Root, "Data", "AMI", "IS1009c", "enrol");
        string[] people = ["A", "B", "C", "D"];
        Skip.IfNot(people.All(p => File.Exists(Path.Combine(enrol, p + ".wav")) && File.Exists(Path.Combine(enrol, p + ".jpg"))), $"the registration material is not in {enrol}");
        var store = Path.Combine(_folder, "gallery");
        using (var acrApi = new ControllerApi(Repository.Amds, Path.Combine(Repository.Root, "AIMs", "aim-settings.json"), st => new Mpai.Providers.AcrProvider(st)))
        {
            Assert.Equal(AifError.OK, acrApi.StartFlow("1OSD-ACR-V1.5-I01", out var acr));
            acrApi.SharedStorageInit(acr, store);
            foreach (var p in people)
            {
                var face = MpaiJson.ToJson(BasicVisualObject.FromFile(p + ".jpg", File.ReadAllBytes(Path.Combine(enrol, p + ".jpg")), "Face"));
                var speech = SpeechObject(File.ReadAllBytes(Path.Combine(enrol, p + ".wav"))).ToJsonString();
                var r = acrApi.Advance(acr, [new("OSD-BVO-V1.5", 1, face), new("OSD-BSO-V1.5", 1, speech),
                                             new("OSD-BTO-V1.5", 2, MpaiJson.ToJson(BasicTextObject.FromText(p))), TestAvatar.Datum]);
                Assert.True(r.Error == AifError.OK, $"ACR, {p}: {r.Error}");
            }
        }
        var gallery = SubjectGallery.Load(new AIF.SharedStorage.FileSharedStorage(store, "test", "test"));
        AmiRecord($"--- {DateTime.Now:s}: registered by ACR: {string.Join(", ", gallery.SubjectIds.Order())}; with a face: {gallery.ScoreFace(new float[512]).Count}, with a voice: {gallery.ScoreVoice(new float[192]).Count}", "2026-11-15", "tma-ami-registered");
        RunAmi(gallery, "tma-ami-registered", "2026-11-15");
    }

    private void RunAmi(SubjectGallery registered, string name, string day)
    {
        string AmiRecord(string line) => TmaEndToEndTests.AmiRecord(line, day, name);
        var video = Data("TMA", "AMI-IS1009c-10m00-15m00-side-by-side.mp4");
        var ffmpeg = Ffmpeg();
        Skip.IfNot(File.Exists(Models("ecapa-tdnn.onnx")) && File.Exists(Models("glintr100.onnx")) && File.Exists(Models("Whisper", "bin", "whisper-cli.exe")), "the models are not in Models\\");
        Skip.IfNot(File.Exists(video) && ffmpeg is not null, "the AMI video (Test\\Data\\TMA) or ffmpeg is not on this machine");
        var seconds = double.TryParse(Environment.GetEnvironmentVariable("MPAI_TMA_AMI_SECONDS"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 30;
        const double fps = 5;
        var settings = Prepare();

        var frames = Path.Combine(_folder, "frames"); Directory.CreateDirectory(frames);
        var span = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        RunTool(ffmpeg!, $"-v error -y -t {span} -i \"{video}\" -an -vf fps={fps},scale=960:540 -q:v 4 \"{Path.Combine(frames, "f%05d.jpg")}\"");
        var wav = Path.Combine(_folder, "audio.wav");
        RunTool(ffmpeg!, $"-v error -y -t {span} -i \"{video}\" -vn -ac 1 -ar 16000 -c:a pcm_s16le \"{wav}\"");
        var jpgs = Directory.GetFiles(frames, "*.jpg").Order(StringComparer.Ordinal).ToList();
        var bvo = new JsonObject
        {
            ["Header"] = "OSD-BVO-V1.5", ["MInstanceID"] = "tma-test", ["BasicVisualObjectID"] = "ami-is1009c-video",
            ["BasicVisualObjectData"] = new JsonArray(jpgs.Select(f => (JsonNode)new JsonObject { ["Data"] = Convert.ToBase64String(File.ReadAllBytes(f)) }).ToArray()),
            ["BasicVisualObjectProperties"] = new JsonObject { ["FrameRate"] = fps }
        };
        AmiRecord($"--- {DateTime.Now:s}: AMI IS1009c 10:00 +{seconds:F0} s, {jpgs.Count} frames at {fps}/s, {new FileInfo(wav).Length / 32000.0:F1} s of sound");

        var log = new ConcurrentQueue<(string Aim, string Port, string Json)>();
        using var gallery = new GalleryHolder(registered);
        using var api = new ControllerApi(_folder, settings, store => new Provider(store, gallery.Value, log));
        Assert.Equal(AifError.OK, api.StartFlow(Module, out var moduleId));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(AifError.OK, api.InputWrite(moduleId, "OSD-BVO-V1.5", 1, bvo.ToJsonString(), 120_000));
        Assert.Equal(AifError.OK, api.InputWrite(moduleId, "OSD-BSO-V1.5", 1, ProgrammeSpeech(File.ReadAllBytes(wav), seconds).ToJsonString(), 120_000));

        JsonObject? last = null;
        for (var i = 0; i < 2000; i++)
        {
            var read = api.OutputRead(moduleId, "OSD-MEV-V1.5", 1, i == 0 ? 1_800_000 : 180_000);
            if (read.Json is null) break;
            last = JsonNode.Parse(read.Json)!.AsObject();
        }
        AmiRecord($"run: {clock.Elapsed.TotalSeconds:F1} s");
        AmiRecord("AIMs: " + string.Join("; ", api.Status(moduleId).Aims.Select(a => $"{a.Aim} {a.Status}{(string.IsNullOrEmpty(a.Reason) ? "" : " " + a.Reason)}")));
        var failures = log.Where(l => l.Port is "EXCEPTION" or "ERROR").ToList();
        AmiRecord($"exceptions/errors of AIMs: {failures.Count}" + string.Concat(failures.Take(5).Select(f => $"\n  {f.Aim} {f.Port}: {(f.Json.Length > 300 ? f.Json[..300] : f.Json)}")));
        var dump = Path.Combine(Repository.Root, "Test", "Results", day);
        Directory.CreateDirectory(dump);
        static bool Whole(string aim) => aim.Contains("AUS") || aim.Contains("SIR") || aim.Contains("FIR") || aim.Contains("IDR") || aim.Contains("ASR");
        File.WriteAllLines(Path.Combine(dump, name + "-aim-outputs.txt"), log.Select(l => $"{l.Aim} | {l.Port} | {(l.Json.Length > 600 && !(Whole(l.Aim) && l.Json.Length < 200_000) ? l.Json[..600] + "..." : l.Json)}"));
        Assert.NotNull(last);
        File.WriteAllText(Path.Combine(dump, name + "-last-mev.json"), last!.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

        var scenes = last["MultimodalSceneDescriptors"]!.AsArray();
        AmiRecord($"Event Descriptors: {scenes.Count} scenes: " +
                  $"{scenes.Count(x => x!["BasicMultimodalSceneDescriptorsData"]?[0]?["BXSOrBXSID"]?["BasicSpeechSceneDescriptors"] is not null)} of speech, " +
                  $"{scenes.Count(x => x!["BasicMultimodalSceneDescriptorsData"]?[0]?["BXSOrBXSID"]?["BasicVisualSceneDescriptors"] is not null)} of pictures");
        var data = log.Where(l => l.Json.Length > 0 && !l.Port.StartsWith("IN ") && l.Port is not ("ERROR" or "EXCEPTION")).ToList();
        AmiRecord($"voices given by SIR: {data.Count(l => l.Aim.Contains("SIR") && l.Port.Contains("OSD-IID"))}");
        // WHO, WHAT, WHERE AND WHEN in the last MEV (2026-11-18): the faces recorded in the Visual Scenes, the utterances with their time, speaker, words and position.
        var faceObjects = scenes.SelectMany(x => x!["BasicObjectsData"] as JsonArray ?? []).Select(o => o?["BXOOrBXOID"])
                                .Where(o => (string?)o?["VisualQualifier"]?["Attributes"]?["VisualObjectType"] == "Face").ToList();
        AmiRecord($"faces in the MEV: {faceObjects.Count}; by person: " + string.Join(", ", faceObjects.GroupBy(o => (string?)o!["VisualQualifier"]!["Attributes"]!["ObjectID"]!["InstanceIdentifierData"]![0]!["InstanceLabel"])
                                                                                     .OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}")));
        var utterances = scenes.Select(x => x!["BasicMultimodalSceneDescriptorsData"]?[0]?["BXSOrBXSID"]?["BasicSpeechSceneDescriptors"]?[0]?["ObjectIDOrObject"]?[0]).Where(o => o is not null).ToList();
        AmiRecord($"utterances: {utterances.Count}; with their time: {utterances.Count(u => u!["BasicSpeechObjectTime"] is not null || u["BasicSpeechObjectSpaceTime"]?["Time"] is not null)}; " +
                  $"with a speaker: {utterances.Count(u => u!["BasicSpeechDataQualifier"]?["Attributes"]?["Metadata"]?["SpeakerIdentity"] is not null)}; " +
                  $"with their words: {utterances.Count(u => u!["BasicSpeechDataQualifier"]?["Attributes"]?["Metadata"]?["ContentDescription"]?["TextObject"] is not null)}; " +
                  $"with a position: {utterances.Count(u => u!["BasicSpeechObjectSpaceTime"]?["SpatialAttitude1"] is not null)}; " +
                  $"voice-face alignments: {scenes.Sum(x => (x!["AlignedMMObjects"] as JsonArray)?.Count ?? 0)}");
        AmiRecord("texts: " + string.Join(" | ", data.Where(l => l.Aim.Contains("ASR")).Select(l => (string?)JsonNode.Parse(l.Json)?["BasicTextData"]?[0]?["Data"])));
        var verdict = PgmRig.Check(last);
        AmiRecord("valid against the schema: " + verdict);
        Assert.Equal("valid", verdict);
        Assert.True(scenes.Count > 0, "no scenes");
    }

    // THE FACE MODEL THE L3 PINS (2026-11-18): its AccessSources "face-embedder", SHA-256 4AB1D643..., the one Access Control Registration registers faces with, where the
    // Controller has obtained it on this machine (AIF\Access). Models\glintr100.onnx is another file of the same name: its features cannot be compared with ACR's.
    private static string? PinnedFaceModel()
    {
        var access = Path.Combine(Repository.Root, "AIF", "Access", "view");
        if (!Directory.Exists(access)) return null;
        foreach (var file in Directory.EnumerateFiles(access, "glintr100.onnx", SearchOption.AllDirectories))
        {
            using var stream = File.OpenRead(file);
            if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).StartsWith("4AB1D6435D639628", StringComparison.Ordinal)) return file;
        }
        return null;
    }

    // ffmpeg: on the PATH, else where winget puts it (C:\Users\...\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_...\...\bin).
    private static string? Ffmpeg()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (dir.Length > 0 && File.Exists(Path.Combine(dir, "ffmpeg.exe"))) return Path.Combine(dir, "ffmpeg.exe");
        var winget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages");
        return Directory.Exists(winget) ? Directory.EnumerateFiles(winget, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault() : null;
    }

    private static void RunTool(string exe, string arguments)
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, arguments) { RedirectStandardError = true, UseShellExecute = false })!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(exe)} failed: {err}");
    }

    private static string AmiRecord(string line, string day, string name)
    {
        var folder = Path.Combine(Repository.Root, "Test", "Results", day);
        Directory.CreateDirectory(folder);
        File.AppendAllText(Path.Combine(folder, name + ".txt"), line + "\n");
        return line;
    }

    private sealed class GalleryHolder(SubjectGallery value) : IDisposable { public SubjectGallery Value { get; } = value; public void Dispose() { } }
}

internal static class TmaTestExtensions
{
    public static JsonObject With(this JsonObject o, byte[] wav)
    {
        o["MInstanceID"] = "tma-test";
        o["BasicSpeechObjectID"] = "programme-speech";
        o["BasicSpeechObjectData"] = new JsonArray(new JsonObject { ["Data"] = Convert.ToBase64String(wav) });
        return o;
    }

    public static JsonNode ToNode(this Mpai.Core.BasicTextObject text) => JsonNode.Parse(MpaiJson.ToJson(text))!;
}
