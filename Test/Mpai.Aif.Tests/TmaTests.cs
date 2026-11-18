using System.Linq;
using System.Text.Json.Nodes;
using Mpai.Osd.Tma;
using Xunit;

namespace Mpai.Aif.Tests;

// Television Media Analysis (OSD-TMA V1.5): the new AIMs against the schemas of their Data Types.
[Trait("Group", "Fast")]
public class TmaTests
{
    private static JsonObject Bso(string id, string words) => new()
    {
        ["Header"] = "OSD-BSO-V1.5", ["MInstanceID"] = "tma", ["BasicSpeechObjectID"] = id,
        ["BasicSpeechObjectProperties"] = new JsonObject { ["Words"] = words }
    };

    // An entry of a Speech Object, as the schema has it: the Basic Speech Object is held as BSObjectIDOrBSObject.
    private static JsonObject Held(JsonObject bso) => new() { ["BSObjectIDOrBSObject"] = new JsonArray(bso) };

    private static void Valid(JsonObject o) { var v = PgmRig.Check(o); Assert.True(v == "valid", v); }

    // A frame of one colour with a little noise, as a PNG in base64.
    private static string Frame(byte r, byte g, byte b, int seed)
    {
        var rnd = new System.Random(seed);
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(64, 48);
        for (var y = 0; y < 48; y++)
            for (var x = 0; x < 64; x++)
                img[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgb24((byte)(r + rnd.Next(-3, 4)), (byte)(g + rnd.Next(-3, 4)), (byte)(b + rnd.Next(-3, 4)));
        using var ms = new System.IO.MemoryStream();
        SixLabors.ImageSharp.ImageExtensions.SaveAsPng(img, ms);
        return System.Convert.ToBase64String(ms.ToArray());
    }

    [Fact]
    public void VideoOfThreeShotsGivesThreeScenes()
    {
        var frames = new JsonArray();
        var shots = new (byte, byte, byte)[] { (30, 60, 210), (210, 210, 40), (20, 110, 50) };
        var n = 0;
        foreach (var (r, g, b) in shots)
            for (var i = 0; i < 20; i++) frames.Add(new JsonObject { ["Data"] = Frame(r, g, b, n++) });
        var video = new JsonObject { ["Header"] = "OSD-BVO-V1.5", ["BasicVisualObjectID"] = "v1", ["BasicVisualObjectData"] = frames };

        var scenes = new VisualChangeDetection("vcd").Handle(TmaTypes.Bvo, 1, video).ToList();
        Assert.Equal(3, scenes.Count);
        Assert.Equal(new[] { 0, 20, 40 }, scenes.Select(s => (int)s.Json["BasicVisualObjectProperties"]!["FirstFrame"]!).ToArray());
        Assert.All(scenes, s => Valid(s.Json));

        var five = new VisualChangeDetection("vcd", new System.Collections.Generic.Dictionary<string, string> { ["ImagesPerScene"] = "5" }).Handle(TmaTypes.Bvo, 1, video).ToList();
        Assert.All(five, s => Assert.Equal(5, s.Json["BasicVisualObjectData"]!.AsArray().Count));
    }

    private static Mpai.Core.InstanceCandidate Candidate(string label, double confidence) => new() { InstanceLabel = label, LabelConfidenceLevel = confidence };

    [Fact]
    public void TheNamesInTheAuxiliaryTextRaiseTheirCandidates()
    {
        var identity = new Mpai.Core.InstanceIdentifier { InstanceIdentifierData = { Candidate("Anna", 0.52), Candidate("Bruno", 0.55), Candidate("Carla", 0.20) } };
        var text = Mpai.Core.OSD.AuxiliaryText.Words(new JsonObject { ["BasicTextData"] = new JsonArray(new JsonObject { ["Data"] = "Tonight Anna talks to Carla." }) }.ToJsonString());
        Assert.Equal("Tonight Anna talks to Carla.", text);
        var raised = Mpai.Core.OSD.AuxiliaryText.RaiseNamed(identity, text!);
        Assert.Equal(new[] { "Anna", "Bruno", "Carla" }, raised.InstanceIdentifierData.Select(c => c.InstanceLabel).ToArray());
        Assert.Equal(0.62, raised.InstanceIdentifierData[0].LabelConfidenceLevel, 3);
        Assert.Equal(0.55, raised.InstanceIdentifierData[1].LabelConfidenceLevel, 3);
        Assert.Equal(0.30, raised.InstanceIdentifierData[2].LabelConfidenceLevel, 3);
    }

    [Fact]
    public void FullToBasicGivesTheBasicObjectsInOrder()
    {
        var full = new JsonObject { ["Header"] = "OSD-SPO-V1.5", ["SpeechObjectID"] = "s1", ["BasicSpeechObjects"] = new JsonArray(Held(Bso("a", "one")), Held(Bso("b", "two")), Held(Bso("c", "three"))) };
        var given = new FullToBasicObject("fbo").Handle(TmaTypes.Spo, 1, full).ToList();
        Assert.Equal(new[] { "a", "b", "c" }, given.Select(g => (string?)g.Json["BasicSpeechObjectID"]).ToArray());
        Assert.All(given, g => { Assert.Equal(TmaTypes.Bso, g.DataType); Valid(g.Json); });
    }

    [Fact]
    public void ASceneIsMadeOfEachBasicSpeechObject()
    {
        var scene = new BasicSpeechSceneDescription("bss").Handle(TmaTypes.Bso, 1, Bso("a", "one")).Single();
        Assert.Equal(TmaTypes.Bss, scene.DataType);
        Valid(scene.Json);
    }

    [Fact]
    public void AnAudioSceneIsMultiplexedAsAScene()
    {
        var audio = new JsonObject { ["Header"] = "OSD-BAS-V1.5", ["MInstanceID"] = "tma", ["BasicAudioSceneDescriptorsID"] = "ears", ["BASSpaceTime"] = TmaTypes.SpaceTime(DateTimeOffset.UtcNow) };
        var bms = new MultimodalSceneMultiplexing("msm").Handle(TmaTypes.Bas, 1, audio).Single();
        Assert.Equal(TmaTypes.Bms, bms.DataType);
        Assert.Equal("ears", (string)bms.Json["BasicMultimodalSceneDescriptorsData"]![0]!["BXSOrBXSID"]!["BasicAudioSceneDescriptorsID"]!);
        Assert.Equal(audio["BASSpaceTime"]!["SpaceTimeID"]!.ToString(), bms.Json["BMSDescriptorsSpaceTime"]!["SpaceTimeID"]!.ToString());     // the Space-Time of the Audio Scene is the one of the Multimodal Scene
    }

    [Fact]
    public void CombiningTheSceneLastReceivedOfEachKindStandsInTheMultimodalScene()
    {
        JsonObject Visual(string id) => new() { ["Header"] = "OSD-BVS-V1.5", ["MInstanceID"] = "hmc", ["BasicVisualSceneDescriptorsID"] = id, ["BasicVisualSceneDescriptorsSpaceTime"] = TmaTypes.SpaceTime(DateTimeOffset.UtcNow) };
        JsonObject Audio(string id) => new() { ["Header"] = "OSD-BAS-V1.5", ["MInstanceID"] = "hmc", ["BasicAudioSceneDescriptorsID"] = id, ["BASSpaceTime"] = TmaTypes.SpaceTime(DateTimeOffset.UtcNow) };
        var msm = new MultimodalSceneMultiplexing("msm", combine: true);

        var first = msm.Handle(TmaTypes.Bvs, 1, Visual("eyes-1")).Single().Json;
        Assert.Single(first["BasicMultimodalSceneDescriptorsData"]!.AsArray());                  // only the visual scene has come

        var second = msm.Handle(TmaTypes.Bas, 1, Audio("ears-1")).Single().Json;
        var held = second["BasicMultimodalSceneDescriptorsData"]!.AsArray().Select(e => (string)e!["BXSOrBXSID"]!["Header"]!).ToArray();
        Assert.Equal(["OSD-BAS-V1.5", "OSD-BVS-V1.5"], held);                                    // both, in one Multimodal Scene
        Assert.Equal(2, (int)second["MultimodalObjectCount"]!);

        var third = msm.Handle(TmaTypes.Bvs, 1, Visual("eyes-2")).Single().Json;                  // a newer visual scene replaces the older one
        var ids = third["BasicMultimodalSceneDescriptorsData"]!.AsArray().Select(e => (string)(e!["BXSOrBXSID"]!["BasicVisualSceneDescriptorsID"] ?? e["BXSOrBXSID"]!["BasicAudioSceneDescriptorsID"])!).ToArray();
        Assert.Equal(["ears-1", "eyes-2"], ids);
    }

    [Fact]
    public void WithoutCombiningEachSceneIsAMultimodalSceneOfItsOwn()
    {
        var msm = new MultimodalSceneMultiplexing("msm");
        var visual = new JsonObject { ["Header"] = "OSD-BVS-V1.5", ["BasicVisualSceneDescriptorsID"] = "eyes" };
        var audio = new JsonObject { ["Header"] = "OSD-BAS-V1.5", ["BasicAudioSceneDescriptorsID"] = "ears" };
        msm.Handle(TmaTypes.Bvs, 1, visual).Single();
        Assert.Single(msm.Handle(TmaTypes.Bas, 1, audio).Single().Json["BasicMultimodalSceneDescriptorsData"]!.AsArray());
    }

    [Fact]
    public void SpeechTextAndIdAreMultiplexedAndCollected()
    {
        var smx = new MultimodalSceneMultiplexing("smx");
        var scene = new BasicSpeechSceneDescription("bss").Handle(TmaTypes.Bso, 1, Bso("a", "one")).Single().Json;
        Assert.Empty(smx.Handle(TmaTypes.Bto, 1, new JsonObject { ["Header"] = "OSD-BTO-V1.5", ["BasicTextObjectID"] = "t1" }));
        Valid(new JsonObject { ["Header"] = "OSD-IID-V1.5", ["MInstanceID"] = "tma", ["InstanceIdentifier"] = "user-1", ["InstanceIdentifierData"] = new JsonArray(new JsonObject { ["InstanceLabel"] = "user-1", ["LabelConfidenceLevel"] = 0.9, ["Taxonomy"] = new JsonObject { ["TaxonomyLevelIDs"] = new JsonArray("person"), ["TaxonomyDataURI"] = "https://example.org/taxonomy" } }) });
        Assert.Empty(smx.Handle(TmaTypes.Iid, 1, new JsonObject { ["Header"] = "OSD-IID-V1.5", ["MInstanceID"] = "tma", ["InstanceIdentifier"] = "user-1", ["InstanceIdentifierData"] = new JsonArray(new JsonObject { ["InstanceLabel"] = "user-1", ["LabelConfidenceLevel"] = 0.9, ["Taxonomy"] = new JsonObject { ["TaxonomyLevelIDs"] = new JsonArray("person"), ["TaxonomyDataURI"] = "https://example.org/taxonomy" } }) }));
        var bms = smx.Handle(TmaTypes.Bss, 1, scene).Single();
        Assert.Equal(TmaTypes.Bms, bms.DataType);
        Valid(bms.Json);
        Assert.Equal(2, bms.Json["BasicObjectsData"]!.AsArray().Count);          // the text and the User ID

        var med = new MultimodalEventDescription("med");
        med.Handle(TmaTypes.Bms, 1, bms.Json).ToList();
        var mev = med.Handle(TmaTypes.Bms, 1, bms.Json).Last();
        Assert.Single(mev.Json["MultimodalSceneDescriptors"]!.AsArray());     // the same Scene given again takes its own place (2026-11-15)
        Valid(mev.Json);
    }

    // WHAT THE MEV RECORDS OF AN UTTERANCE (2026-11-15): a segment of the programme with its time; the identity Speaker Identity Recognition gives
    // and the text Automatic Speech Recognition gives for that time go into its Speech Qualifier, whichever comes first; the Multimodal Scene is given
    // again with the same ID, and the Multimodal Event Descriptors keep one Scene, holding the speaker, the words and the programme time.
    [Fact]
    public void AnUtteranceRecordsItsSpeakerAndItsWordsAtItsProgrammeTime()
    {
        JsonObject Time(double from, double to) => new() { ["Header"] = "OSD-STM-V1.5", ["SimpleTimeID"] = "t" + from,
            ["SimpleTimeData"] = new JsonArray(new JsonObject { ["FlagsByte"] = 0, ["StartTime"] = from, ["EndTime"] = to, ["AccuracyMode"] = "single", ["AccuracyPlusMinus"] = 0.1, ["TimeType"] = false, ["TimeUnit"] = "00" }) };
        JsonObject Identity(string who, double from, double to) => new() { ["Header"] = "OSD-IID-V1.5", ["MInstanceID"] = "tma", ["InstanceIdentifier"] = who, ["ObjectID"] = "seg-" + from, ["InstanceTime"] = Time(from, to),
            ["InstanceIdentifierData"] = new JsonArray(new JsonObject { ["InstanceLabel"] = who, ["LabelConfidenceLevel"] = 0.6, ["Taxonomy"] = new JsonObject { ["TaxonomyLevelIDs"] = new JsonArray("person"), ["TaxonomyDataURI"] = "" } }) };
        JsonObject Words(string text, double from, double to) => new() { ["Header"] = "OSD-BTO-V1.5", ["BasicTextObjectID"] = "w" + from,
            ["BasicTextObjectSpaceTime"] = Time(from, to), ["BasicTextData"] = new JsonArray(new JsonObject { ["Data"] = text }) };
        JsonObject Segment(string id, double from, double to) { var b = Bso(id, ""); b["BasicSpeechObjectTime"] = Time(from, to); return b; }

        var bss = new BasicSpeechSceneDescription("bss");
        var msm = new MultimodalSceneMultiplexing("msm");
        var med = new MultimodalEventDescription("med");
        var idr = new IdentityReconciliationEach("idr");
        JsonObject? mev = null;
        void Into(IEnumerable<(string DataType, int Port, JsonObject Json)> given) { foreach (var g in given) foreach (var e in med.Handle(g.DataType, g.Port, g.Json)) mev = e.Json; }

        // the first segment: the scene first, then its identity (through IDR) and its words
        Into(msm.Handle(TmaTypes.Bss, 1, bss.Handle(TmaTypes.Bso, 1, Segment("seg-1.4", 1.4, 26.4)).Single().Json));
        foreach (var id in idr.Handle(TmaTypes.Iid, 2, Identity("B", 1.4, 26.4))) Into(msm.Handle(id.DataType, id.Port, id.Json));
        Into(msm.Handle(TmaTypes.Bto, 1, Words("Now this is an interface for a remote.", 1.4, 26.4)));
        // the second: its words before its scene
        Into(msm.Handle(TmaTypes.Bto, 1, Words("Okay, thank you very much.", 111.2, 120.1)));
        Into(msm.Handle(TmaTypes.Bss, 1, bss.Handle(TmaTypes.Bso, 1, Segment("seg-111.2", 111.2, 120.1)).Single().Json));

        var scenes = mev!["MultimodalSceneDescriptors"]!.AsArray();
        Assert.Equal(2, scenes.Count);
        JsonNode Speech(int i) => scenes[i]!["BasicMultimodalSceneDescriptorsData"]![0]!["BXSOrBXSID"]!["BasicSpeechSceneDescriptors"]![0]!["ObjectIDOrObject"]![0]!;
        var first = Speech(0)["BasicSpeechDataQualifier"]!["Attributes"]!["Metadata"]!;
        Assert.Equal("B", (string)first["SpeakerIdentity"]!["InstanceIdentifierData"]![0]!["InstanceLabel"]!);
        Assert.Equal("Now this is an interface for a remote.", (string)first["ContentDescription"]!["TextObject"]!["BasicTextObjects"]![0]!["BTObjectIDOrBTObject"]![0]!["BasicTextData"]![0]!["Data"]!);
        Assert.Equal(1.4, (double)scenes[0]!["BMSDescriptorsSpaceTime"]!["Time"]!["SimpleTimeData"]![0]!["StartTime"]!);          // programme time
        Assert.Equal("Okay, thank you very much.", (string)Speech(1)["BasicSpeechDataQualifier"]!["Attributes"]!["Metadata"]!["ContentDescription"]!["TextObject"]!["BasicTextObjects"]![0]!["BTObjectIDOrBTObject"]![0]!["BasicTextData"]![0]!["Data"]!);
        Valid(mev);
    }
}
