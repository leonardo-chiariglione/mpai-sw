using System.Text.Json.Nodes;

using AIF.Controller;
using AIF.Store;
using Mpai.Osd.Tma;

namespace Mpai.Aif.Tests;

// TELEVISION MEDIA ANALYSIS: WHERE AND WHEN (N-34 step 3, night 2026-11-18). Visual Change Detection gives the programme time of each scene and samples its frames one a
// second; the faces are recorded in the Visual Scene of their time, with where they are in the picture; an utterance is placed where its speaker's face is during it, and the
// voice and the face are recorded as Aligned Objects.
[Trait("Group", "Fast")]
public class TmaWhereTests
{
    private static JsonObject Time(double from, double to) => new() { ["Header"] = "OSD-STM-V1.5", ["SimpleTimeID"] = "t" + from,
        ["SimpleTimeData"] = new JsonArray(new JsonObject { ["FlagsByte"] = 0, ["StartTime"] = from, ["EndTime"] = to, ["AccuracyMode"] = "single", ["AccuracyPlusMinus"] = 0.0, ["TimeType"] = false, ["TimeUnit"] = "00" }) };

    private static string Frame(byte r, byte g, byte b, int seed)
    {
        var rnd = new Random(seed);
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(64, 48);
        for (var y = 0; y < 48; y++)
            for (var x = 0; x < 64; x++)
                img[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgb24((byte)(r + rnd.Next(-3, 4)), (byte)(g + rnd.Next(-3, 4)), (byte)(b + rnd.Next(-3, 4)));
        using var ms = new MemoryStream();
        SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img, ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    // Two shots of 4 s at 5 frames a second, sampled one frame a second: each scene has its programme time and the time of each frame it gives.
    [Fact]
    public void ScenesHaveTheirProgrammeTimeAndAFrameASecond()
    {
        var frames = new JsonArray();
        var n = 0;
        foreach (var (r, g, b) in new (byte, byte, byte)[] { (30, 60, 210), (210, 210, 40) })
            for (var i = 0; i < 20; i++) frames.Add(new JsonObject { ["Data"] = Frame(r, g, b, n++) });
        var video = new JsonObject { ["Header"] = "OSD-BVO-V1.5", ["BasicVisualObjectID"] = "v", ["BasicVisualObjectData"] = frames, ["BasicVisualObjectProperties"] = new JsonObject { ["FrameRate"] = 5.0 } };
        var scenes = new VisualChangeDetection("vcd", new Dictionary<string, string> { ["SampleSeconds"] = "1" }).Handle(TmaTypes.Bvo, 1, video).Select(s => s.Json).ToList();
        Assert.Equal(2, scenes.Count);
        var second = scenes[1];
        var time = second["BasicVisualObjectTime"]!["Time"]!["SimpleTimeData"]![0]!;
        Assert.Equal(4.0, (double)time["StartTime"]!); Assert.Equal(8.0, (double)time["EndTime"]!);
        Assert.False((bool)time["TimeType"]!);
        Assert.Equal([4.0, 5.0, 6.0, 7.0], second["BasicVisualObjectProperties"]!["FrameTimes"]!.AsArray().Select(t => (double)t!));
        Assert.Equal(4, second["BasicVisualObjectData"]!.AsArray().Count);
    }

    // A visual scene of 0-10 s; a face of A at 2.0 s on the left, one of B at 2.0 s on the right; an utterance 1.5-3.0 s whose speaker is A: the faces are in the visual scene, the
    // utterance is where A's face is, the voice and the face are aligned, and the MEV is valid.
    [Fact]
    public void AnUtteranceIsWhereTheFaceOfItsSpeakerIs()
    {
        var store = new AmdStore(Repository.Amds); store.Scan();
        var bvs = new Mpai.Osd.Bvs.BvsAimProcessor("bvs", AimPortReader.Load(store, "1OSD-BVS-V1.5-I01"));
        var picture = new JsonObject
        {
            ["Header"] = "OSD-BVO-V1.5", ["MInstanceID"] = "tma", ["BasicVisualObjectID"] = "scene-1",
            ["BasicVisualObjectTime"] = new JsonObject { ["Header"] = "OSD-SPT-V1.5", ["SpaceTimeID"] = "s", ["Time"] = Time(0, 10) },
            ["BasicVisualObjectData"] = new JsonArray(new JsonObject { ["Data"] = Frame(30, 60, 210, 1) })
        };
        var ports = AimPortReader.Load(store, "1OSD-BVS-V1.5-I01");
        var reply = bvs.ProcessAsync(new Message { MessageId = "m", Ports = new Dictionary<string, string> { [ports.Input("OSD-BVO-V1.5")] = picture.ToJsonString() } }).Result;
        var visualScene = JsonNode.Parse(reply.Ports[ports.Output("OSD-BVS-V1.5")])!.AsObject();
        Assert.True(visualScene["BVSDescriptorsSpaceTime"]?["Time"]?["SimpleTimeData"]?.AsArray().Count == 1, "the visual scene keeps its time: " + visualScene["BVSDescriptorsSpaceTime"]?.ToJsonString());

        JsonObject FaceOf(string who, double at, double x1, double x2) => new()
        {
            ["Header"] = "OSD-IID-V1.5", ["MInstanceID"] = "tma", ["InstanceIdentifier"] = "face-" + who, ["InstanceTime"] = Time(at, at),
            ["InstanceSpaceTime"] = new JsonObject { ["Header"] = "OSD-SPT-V1.5", ["SpaceTimeID"] = "f" + who, ["SpatialAttitude1"] = Mpai.Paf.Fir.FirEachFace.PictureAttitude(who, x1, 100, x2, 220), ["Time"] = Time(at, at) },
            ["InstanceIdentifierData"] = new JsonArray(new JsonObject { ["InstanceLabel"] = who, ["LabelConfidenceLevel"] = 0.8, ["Taxonomy"] = new JsonObject { ["TaxonomyLevelIDs"] = new JsonArray("visual", "face", "person"), ["TaxonomyDataURI"] = "" } })
        };
        var voice = new JsonObject
        {
            ["Header"] = "OSD-IID-V1.5", ["MInstanceID"] = "tma", ["InstanceIdentifier"] = "voice-A", ["ObjectID"] = "seg-1.5", ["InstanceTime"] = Time(1.5, 3.0),
            ["InstanceIdentifierData"] = new JsonArray(new JsonObject { ["InstanceLabel"] = "A", ["LabelConfidenceLevel"] = 0.6, ["Taxonomy"] = new JsonObject { ["TaxonomyLevelIDs"] = new JsonArray("person"), ["TaxonomyDataURI"] = "" } })
        };
        var segment = new JsonObject { ["Header"] = "OSD-BSO-V1.5", ["MInstanceID"] = "tma", ["BasicSpeechObjectID"] = "seg-1.5", ["BasicSpeechObjectTime"] = Time(1.5, 3.0) };

        var bss = new BasicSpeechSceneDescription("bss");
        var msm = new MultimodalSceneMultiplexing("msm");
        var med = new MultimodalEventDescription("med");
        var idr = new IdentityReconciliationEach("idr");
        JsonObject? mev = null;
        void Into(IEnumerable<(string DataType, int Port, JsonObject Json)> given) { foreach (var g in given) foreach (var e in med.Handle(g.DataType, g.Port, g.Json)) mev = e.Json; }

        Into(msm.Handle(TmaTypes.Bvs, 1, visualScene));
        foreach (var f in idr.Handle(TmaTypes.Iid, 1, FaceOf("A", 2.0, 100, 200))) Into(msm.Handle(f.DataType, f.Port, f.Json));
        foreach (var f in idr.Handle(TmaTypes.Iid, 1, FaceOf("B", 2.0, 500, 600))) Into(msm.Handle(f.DataType, f.Port, f.Json));
        Into(msm.Handle(TmaTypes.Bss, 1, bss.Handle(TmaTypes.Bso, 1, segment).Single().Json));
        var reconciled = idr.Handle(TmaTypes.Iid, 2, voice).Single();
        Assert.Equal(1 - 0.4 * 0.2, (double)reconciled.Json["InstanceIdentifierData"]![0]!["LabelConfidenceLevel"]!, 6);     // the face of A seen during the utterance confirms the voice
        Into(msm.Handle(reconciled.DataType, reconciled.Port, reconciled.Json));

        var scenes = mev!["MultimodalSceneDescriptors"]!.AsArray();
        Assert.Equal(2, scenes.Count);                                                  // the visual scene and the utterance, each once
        var faces = scenes[0]!["BasicObjectsData"]!.AsArray().Select(o => o!["BXOOrBXOID"]!).ToList();
        Assert.Equal(["A", "B"], faces.Select(f => (string)f["VisualQualifier"]!["Attributes"]!["ObjectID"]!["InstanceIdentifierData"]![0]!["InstanceLabel"]!));
        Assert.All(faces, f => Assert.Equal("Face", (string)f["VisualQualifier"]!["Attributes"]!["VisualObjectType"]!));
        var speech = scenes[1]!["BasicMultimodalSceneDescriptorsData"]![0]!["BXSOrBXSID"]!["BasicSpeechSceneDescriptors"]![0]!["ObjectIDOrObject"]![0]!;
        Assert.Equal([150.0, 160.0, 0.0], speech["BasicSpeechObjectSpaceTime"]!["SpatialAttitude1"]!["Position"]!["CartPosition"]!.AsArray().Select(v => (double)v!));
        Assert.Equal(1.5, (double)speech["BasicSpeechObjectSpaceTime"]!["Time"]!["SimpleTimeData"]![0]!["StartTime"]!);
        var aligned = scenes[1]!["AlignedMMObjects"]!.AsArray().Single()!;
        Assert.Equal("SpeakerFace", (string)aligned["AlignmentCode"]!);
        Assert.Equal(["seg-1.5", "face-face-A"], aligned["AlignedObjects"]!.AsArray().Select(o => (string)o!));
        var v = PgmRig.Check(mev); Assert.True(v == "valid", v);
    }

    // The words of an utterance as Speech Recognition gives them (the Text Qualifier of the typed model of Mpai.Core: SubType, Format) are recorded under the schema's names,
    // and the MEV is valid.
    [Fact]
    public void RecognisedWordsAreRecordedAsTheirSchemaHasThem()
    {
        var text = Mpai.Core.BasicTextObject.FromText("a remote control", new Mpai.Core.TextQualifier
        {
            TextQualifierID = "q", Format = new Mpai.Core.TextFormat { ContentFormat = new Mpai.Core.TextContentFormat { Static = Mpai.Core.TextStaticFormat.Utf8 } },
            Attributes = new Mpai.Core.TextAttributes { Language = new Mpai.Core.Language { LanguageCode = "en", LanguageFormat = Mpai.Core.LanguageFormat.Iso639_1 } }
        });
        var words = JsonNode.Parse(Mpai.Core.MpaiJson.ToJson(text))!.AsObject();
        words["BasicTextObjectSpaceTime"] = Time(4, 6);
        var msm = new MultimodalSceneMultiplexing("msm");
        var med = new MultimodalEventDescription("med");
        var bss = new BasicSpeechSceneDescription("bss");
        JsonObject? mev = null;
        void Into(IEnumerable<(string DataType, int Port, JsonObject Json)> given) { foreach (var g in given) foreach (var e in med.Handle(g.DataType, g.Port, g.Json)) mev = e.Json; }
        var segment = new JsonObject { ["Header"] = "OSD-BSO-V1.5", ["MInstanceID"] = "tma", ["BasicSpeechObjectID"] = "seg-4", ["BasicSpeechObjectTime"] = Time(4, 6) };
        Into(msm.Handle(TmaTypes.Bss, 1, bss.Handle(TmaTypes.Bso, 1, segment).Single().Json));
        Into(msm.Handle(TmaTypes.Bto, 1, words));
        var v = PgmRig.Check(mev!); Assert.True(v == "valid", v);
    }

    // An utterance of someone whose face is not seen during it keeps its time and has no position.
    [Fact]
    public void AnUtteranceWhoseSpeakerIsNotSeenHasNoPosition()
    {
        var msm = new MultimodalSceneMultiplexing("msm");
        var bss = new BasicSpeechSceneDescription("bss");
        var segment = new JsonObject { ["Header"] = "OSD-BSO-V1.5", ["MInstanceID"] = "tma", ["BasicSpeechObjectID"] = "seg-9", ["BasicSpeechObjectTime"] = Time(9, 11) };
        var voice = new JsonObject
        {
            ["Header"] = "OSD-IID-V1.5", ["MInstanceID"] = "tma", ["InstanceIdentifier"] = "v", ["InstanceTime"] = Time(9, 11),
            ["InstanceIdentifierData"] = new JsonArray(new JsonObject { ["InstanceLabel"] = "C", ["LabelConfidenceLevel"] = 0.7, ["Taxonomy"] = new JsonObject { ["TaxonomyLevelIDs"] = new JsonArray("person"), ["TaxonomyDataURI"] = "" } })
        };
        msm.Handle(TmaTypes.Bss, 1, bss.Handle(TmaTypes.Bso, 1, segment).Single().Json).ToList();
        var scene = msm.Handle(TmaTypes.Iid, 1, voice).Single().Json;
        var speech = scene["BasicMultimodalSceneDescriptorsData"]![0]!["BXSOrBXSID"]!["BasicSpeechSceneDescriptors"]![0]!["ObjectIDOrObject"]![0]!;
        Assert.Null(speech["BasicSpeechObjectSpaceTime"]?["SpatialAttitude1"]);
        Assert.Equal(9.0, (double)speech["BasicSpeechObjectTime"]!["SimpleTimeData"]![0]!["StartTime"]!);
        Assert.Null(scene["AlignedMMObjects"]);
    }
}

// Face Identity Recognition of Television Media Analysis on a picture of two people (Models): two Instance Identifiers, left to right, each with its box in the picture and the
// time of the frame, valid against the schema; the same unregistered face keeps its label.
[Trait("Group", "Models")]
public class TmaFaceTests
{
    [SkippableFact]
    public void EveryFaceOfAFrameWithWhereItIs()
    {
        var models = Path.Combine(Repository.Root, "Models");
        var people = Path.Combine(Repository.Root, "Test", "Data", "Images", "lfw");
        var lfw = Directory.Exists(people) ? Directory.GetDirectories(people).Order(StringComparer.Ordinal).Take(2).Select(d => Directory.GetFiles(d, "*.jpg").Order(StringComparer.Ordinal).First()).ToArray() : [];   // two different people of Labeled Faces in the Wild
        Skip.IfNot(File.Exists(Path.Combine(models, "scrfd_10g_bnkps.onnx")) && File.Exists(Path.Combine(models, "glintr100.onnx")) && lfw.Length == 2, "the face models or two face pictures are not installed");
        using var left = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgb24>(lfw[0]);
        using var right = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgb24>(lfw[1]);
        using var both = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(left.Width + right.Width, Math.Max(left.Height, right.Height));
        SixLabors.ImageSharp.Processing.ProcessingExtensions.Mutate(both, c => SixLabors.ImageSharp.Processing.DrawImageExtensions.DrawImage(SixLabors.ImageSharp.Processing.DrawImageExtensions.DrawImage(c, left, new SixLabors.ImageSharp.Point(0, 0), 1f), right, new SixLabors.ImageSharp.Point(left.Width, 0), 1f));
        using var ms = new MemoryStream(); SixLabors.ImageSharp.ImageExtensions.SaveAsJpeg(both, ms);
        var picture = new JsonObject
        {
            ["Header"] = "OSD-BVO-V1.5", ["MInstanceID"] = "tma", ["BasicVisualObjectID"] = "p",
            ["BasicVisualObjectData"] = new JsonArray(new JsonObject { ["Data"] = Convert.ToBase64String(ms.ToArray()) }, new JsonObject { ["Data"] = Convert.ToBase64String(ms.ToArray()) }),
            ["BasicVisualObjectProperties"] = new JsonObject { ["FrameTimes"] = new JsonArray(12.0, 13.0) }
        };
        using var detector = new Mpai.Osd.VisualScene.ScrfdFaceDetector(Path.Combine(models, "scrfd_10g_bnkps.onnx"));
        using var recogniser = new Mpai.Paf.Fir.ArcFaceRecogniser(Path.Combine(models, "glintr100.onnx"));
        var fir = new Mpai.Paf.Fir.FirEachFace("fir", detector, recogniser, new Mpai.Core.SubjectGallery());
        var ids = fir.Faces(picture).ToList();
        Assert.Equal(4, ids.Count);                                                     // two faces in each of two frames
        Assert.All(ids, id => { var v = PgmRig.Check(id); Assert.True(v == "valid", v); });
        double X(JsonObject id) => (double)id["InstanceSpaceTime"]!["SpatialAttitude1"]!["Position"]!["CartPosition"]![0]!;
        Assert.True(X(ids[0]) < left.Width && X(ids[1]) > left.Width);
        Assert.Equal(12.0, (double)ids[0]["InstanceTime"]!["SimpleTimeData"]![0]!["StartTime"]!);
        Assert.Equal(13.0, (double)ids[2]["InstanceTime"]!["SimpleTimeData"]![0]!["StartTime"]!);
        string Label(JsonObject id) => (string)id["InstanceIdentifierData"]![0]!["InstanceLabel"]!;
        Assert.Equal(Label(ids[0]), Label(ids[2])); Assert.Equal(Label(ids[1]), Label(ids[3]));          // the same unregistered face, the same label
        Assert.NotEqual(Label(ids[0]), Label(ids[1]));
    }
}
