using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using AIF.Controller;

namespace Mpai.Osd.Tma;

// The Data Types of Television Media Analysis (OSD-TMA V1.5) by Header.
public static class TmaTypes
{
    public const string Spo = "OSD-SPO-V1.5", Bso = "OSD-BSO-V1.5", Bvo = "OSD-BVO-V1.5", Txo = "OSD-TXO-V1.5", Bto = "OSD-BTO-V1.5",
        Spt = "OSD-SPT-V1.5", Bss = "OSD-BSS-V1.5", Bas = "OSD-BAS-V1.5", Bvs = "OSD-BVS-V1.5", Bms = "OSD-BMS-V1.5", Mev = "OSD-MEV-V1.5", Iid = "OSD-IID-V1.5";

    public static string NewId(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..12];

    // One absolute instant (1970 epoch, seconds), or the stretch from start to end where both are given.
    public static JsonObject SimpleTime(DateTimeOffset at, double? seconds = null) => new()
    {
        ["Header"] = "OSD-STM-V1.5",
        ["SimpleTimeID"] = NewId("st"),
        ["SimpleTimeData"] = new JsonArray(new JsonObject
        {
            ["FlagsByte"] = 1,
            ["StartTime"] = at.ToUnixTimeMilliseconds() / 1000.0,
            ["EndTime"] = at.ToUnixTimeMilliseconds() / 1000.0 + (seconds ?? 0),
            ["AccuracyMode"] = "single",
            ["AccuracyPlusMinus"] = 0.0,
            ["TimeType"] = true,
            ["TimeUnit"] = "00"
        })
    };

    public static JsonObject SpaceTime(DateTimeOffset at) => new() { ["Header"] = "OSD-SPT-V1.5", ["SpaceTimeID"] = NewId("spt"), ["Time"] = SimpleTime(at) };
}

// An AIM of Television Media Analysis. The transformation each makes is Handle, from a Datum on an Input Port to the Data it gives on Output Ports,
// so that it can be tried without the Controller; RunAsync only reads the Input Ports as Messages arrive and writes what Handle gives.
public abstract class TmaAim(string instanceId) : IAimProcessor, IAimRunner
{
    public string InstanceId { get; } = instanceId;
    public Task<Message> ProcessAsync(Message message) => throw new NotSupportedException(InstanceId + " runs continuously.");

    protected abstract (string DataType, int Port)[] Inputs { get; }
    public abstract IEnumerable<(string DataType, int Port, JsonObject Json)> Handle(string dataType, int port, JsonObject json);

    public async Task RunAsync(IAimPorts ports, AimContext context)
    {
        while (await ports.SelectAsync(-1, Inputs) is { } p)
        {
            if (await ports.ReadAsync(p.DataType, p.PortNumber, 0) is not { } m) continue;
            if (JsonNode.Parse(m.Json) is not JsonObject json) continue;
            foreach (var (dt, n, o) in Handle(p.DataType, p.PortNumber, json))
                await ports.WriteAsync(dt, n, o.ToJsonString());
        }
    }
}

// OSD-FBO-V1.5 - Full to Basic Object. A Full Object of the medium it is instanced for (here the Speech Object) is given as its Basic Objects, one by one, in order.
public sealed class FullToBasicObject(string instanceId) : TmaAim(instanceId)
{
    protected override (string, int)[] Inputs => [(TmaTypes.Spo, 1)];

    public override IEnumerable<(string DataType, int Port, JsonObject Json)> Handle(string dataType, int port, JsonObject json)
    {
        if (json["BasicSpeechObjects"] is not JsonArray basics) yield break;
        // Each entry of a Speech Object holds its Basic Speech Object as BSObjectIDOrBSObject; the Basic Speech Object is an Object, and goes on as it is.
        foreach (var entry in basics.OfType<JsonObject>())
            if (entry["BSObjectIDOrBSObject"] is JsonArray held)
                foreach (var b in held.OfType<JsonObject>())
                {
                    var basic = (JsonObject)b.DeepClone();
                    basic["Header"] = "OSD-BSO-V1.5";
                    yield return (TmaTypes.Bso, 1, basic);
                }
    }
}

// OSD-BSS-V1.5 - Basic Speech Scene Description. Each Basic Speech Object is described as the one Object of a Scene, where and when it is, as the BVS does for the picture.
public sealed class BasicSpeechSceneDescription(string instanceId) : TmaAim(instanceId)
{
    private JsonObject? spaceTime;
    protected override (string, int)[] Inputs => [(TmaTypes.Spt, 1), (TmaTypes.Bso, 1)];

    public override IEnumerable<(string DataType, int Port, JsonObject Json)> Handle(string dataType, int port, JsonObject json)
    {
        if (dataType == TmaTypes.Spt) { spaceTime = json; yield break; }
        // Where and when the speech is: its Space-Time; else its time (a segment of the programme says from when to when it runs); else the
        // Space-Time given on the Port; only as a last resort the present moment.
        var st = (json["BasicSpeechObjectSpaceTime"] as JsonObject
                  ?? (json["BasicSpeechObjectTime"] is JsonObject time ? new JsonObject { ["Header"] = "OSD-SPT-V1.5", ["SpaceTimeID"] = TmaTypes.NewId("spt"), ["Time"] = time.DeepClone() } : null)
                  ?? spaceTime ?? TmaTypes.SpaceTime(DateTimeOffset.UtcNow)).DeepClone();
        yield return (TmaTypes.Bss, 1, new JsonObject
        {
            ["Header"] = "OSD-BSS-V1.5",
            ["MInstanceID"] = (string?)json["MInstanceID"] ?? "tma",
            ["BasicSpeechSceneDescriptorsID"] = TmaTypes.NewId("bss"),
            ["BasicSpeechSceneDescriptorsSpaceTime"] = st.DeepClone(),
            ["ObjectCount"] = 1,
            ["BasicSpeechSceneDescriptors"] = new JsonArray(new JsonObject
            {
                ["ObjectSpaceTime"] = st,
                ["ObjectIDOrObject"] = new JsonArray(json.DeepClone())
            })
        });
    }
}

// OSD-MSM-V1.5 - Multimodal Scene Multiplexing. The Scenes of the speech, of the audio and of the pictures, the Recognised Text and the User ID that arrive are put in one Basic Multimodal
// Scene: one for each Scene of speech, of audio or of pictures, with the Text and the ID last received.
// combine: the Multimodal Scene it gives holds the Scene last received of EACH kind - of speech, of audio and of pictures - and not only the one that has just arrived (the HMC Entity and Context Understanding
// gives one Multimodal Scene Descriptors, whatever the modality of the Scene that came last). By default (Television Media Analysis) it is one Scene for each Scene that arrives.
public sealed class MultimodalSceneMultiplexing(string instanceId, bool combine = false) : TmaAim(instanceId)
{
    private readonly Dictionary<string, JsonObject> latest = [];
    private JsonObject? userId;
    private readonly List<JsonObject> texts = [];
    // The Ports of the L3 (1OSD-MSM-V1.5-I01): a Text Object arriving at Handle is taken too, but its Port is not read here.
    protected override (string, int)[] Inputs => [(TmaTypes.Bss, 1), (TmaTypes.Bas, 1), (TmaTypes.Bvs, 1), (TmaTypes.Bto, 1), (TmaTypes.Iid, 1)];

    // WHAT THE MEV RECORDS OF EACH UTTERANCE (2026-11-15; the author: "MEV should simply record what it receives"; "position and time of speech
    // objects should be in TMA data"). A text or an identity that says the time of an utterance (Automatic Speech Recognition gives the time of the
    // speech it recognised, Speaker Identity Recognition the time and the ID of the speech it identified) goes INTO that utterance: the Speech
    // Qualifier of its Basic Speech Object - Attributes.Metadata.SpeakerIdentity and Attributes.Metadata.ContentDescription.TextObject - and the
    // Multimodal Scene is given again, with the same ID, now holding them. Whichever comes first waits for the other: the scene for the text and
    // the identity, or the text and the identity for the scene. A text or an identity without a time is given with the next scene, as before.
    private readonly Dictionary<long, JsonObject> spokenScenes = [];
    private readonly Dictionary<long, List<(string Kind, JsonObject Json)>> waiting = [];
    private static long? Key(JsonNode? time) => (time?["SimpleTimeData"] as JsonArray)?.FirstOrDefault()?["StartTime"] is JsonValue v && v.TryGetValue<double>(out var s) ? (long)Math.Round(s * 1000) : null;

    private static JsonObject? SpokenObject(JsonObject scene) =>
        scene["BasicMultimodalSceneDescriptorsData"]?[0]?["BXSOrBXSID"]?["BasicSpeechSceneDescriptors"]?[0]?["ObjectIDOrObject"]?[0] as JsonObject;

    private static void Record(JsonObject speech, string kind, JsonObject what)
    {
        var qualifier = speech["BasicSpeechDataQualifier"] as JsonObject ?? new JsonObject { ["Header"] = "TFA-SPQ-V1.5", ["SpeechQualifierID"] = TmaTypes.NewId("spq") };
        speech["BasicSpeechDataQualifier"] = qualifier;
        qualifier["SubTypes"] ??= new JsonObject();                                   // required by the schema of the Speech Qualifier
        qualifier["Formats"] ??= new JsonObject();
        var attributes = qualifier["Attributes"] as JsonObject ?? new JsonObject(); qualifier["Attributes"] = attributes;
        var metadata = attributes["Metadata"] as JsonObject ?? new JsonObject(); attributes["Metadata"] = metadata;
        if (kind == TmaTypes.Iid) { metadata["SpeakerIdentity"] = what.DeepClone(); return; }
        var content = metadata["ContentDescription"] as JsonObject ?? new JsonObject(); metadata["ContentDescription"] = content;
        // The Basic Text Object carries its time as a Simple Time; the Text Object and its entries carry a Space-Time, which holds it.
        var spaceTime = what["BasicTextObjectSpaceTime"] is JsonObject time ? new JsonObject { ["Header"] = "OSD-SPT-V1.5", ["SpaceTimeID"] = TmaTypes.NewId("spt"), ["Time"] = time.DeepClone() }
                                                                            : TmaTypes.SpaceTime(DateTimeOffset.UtcNow);
        content["TextObject"] = new JsonObject
        {
            ["Header"] = "OSD-TXO-V1.5", ["TextObjectID"] = TmaTypes.NewId("txo"), ["TextObjectSpaceTime"] = spaceTime.DeepClone(), ["BasicTextObjectCount"] = 1,
            ["BasicTextObjects"] = new JsonArray(new JsonObject { ["BasicTextObjectSpaceTime"] = spaceTime, ["BTObjectIDOrBTObject"] = new JsonArray(SchemaText(what)) })
        };
    }

    // THE TEXT AS ITS SCHEMA HAS IT (2026-11-18). The Text Qualifier written by the typed model of Mpai.Core (Speech Recognition gives it) names its parts SubType and Format,
    // where TFA-TXQ-V1.5 requires SubTypes and Formats: they are written under the schema's names (the content of each is the same; a SubType that holds nothing is an empty
    // SubTypes). Nothing else of the text changes. The model's names are a defect reported in MKG\pending-changes.md.
    private static JsonObject SchemaText(JsonObject text)
    {
        var copy = (JsonObject)text.DeepClone();
        if (copy["TextQualifier"] is JsonObject q)
        {
            if (q["SubTypes"] is null) { var sub = q["SubType"]; q.Remove("SubType"); q["SubTypes"] = sub is JsonObject o ? o.DeepClone() : new JsonObject(); }
            if (q["Formats"] is null) { var format = q["Format"]; q.Remove("Format"); q["Formats"] = format is JsonObject f ? f.DeepClone() : new JsonObject(); }
        }
        return copy;
    }

    // WHERE AND WHEN THE FACES ARE (2026-11-18). An Instance Identifier of a face (Face Identity Recognition of Television Media Analysis: its taxonomy says face, its
    // InstanceSpaceTime says where it is in the picture and when) is recorded as a Basic Visual Object of the Visual Scene of its time - VisualObjectType Face, ObjectID the
    // Instance Identifier, its Space-Time the place and the time - and the Scene is given again with the same ID. An utterance whose speaker is named (Speaker Identity
    // Recognition) is placed where the face of that person is during it (the face seen nearest the middle of the utterance, from half a second before to half a second
    // after it): its BasicSpeechObjectSpaceTime gets that Spatial Attitude, and the Multimodal Scene records the voice and the face as Aligned Objects, with the confidence
    // of the voice's identity times that of the face's. An utterance whose speaker's face is not seen keeps its time and has no position.
    private sealed record FaceSeen(string Label, double Time, double Confidence, JsonObject Visual);
    private readonly List<FaceSeen> faces = [];
    private readonly List<(double From, double To, JsonObject Scene)> visualScenes = [];
    private readonly List<JsonObject> facesWaiting = [];

    private static (string? Label, double Confidence) Named(JsonNode? id) =>
        id?["InstanceIdentifierData"]?[0] is JsonObject c && (string?)c["InstanceLabel"] is { } label && label is not ("person" or "face" or "speech")
            ? (label, c["LabelConfidenceLevel"] is JsonValue v && v.TryGetValue<double>(out var x) ? x : 0) : (null, 0);

    private static bool IsFace(JsonObject id) =>
        id["InstanceSpaceTime"]?["SpatialAttitude1"] is JsonObject && (id["InstanceIdentifierData"]?[0]?["Taxonomy"]?["TaxonomyLevelIDs"] as JsonArray)?.Any(l => (string?)l == "face") == true;

    private static (double From, double To)? Span(JsonNode? time) =>
        (time?["SimpleTimeData"] as JsonArray)?.FirstOrDefault() is JsonObject d && d["StartTime"] is JsonValue a && a.TryGetValue<double>(out var f) && d["EndTime"] is JsonValue b && b.TryGetValue<double>(out var t) ? (f, t) : null;

    private static JsonObject FaceObject(JsonObject id) => new()
    {
        ["Header"] = "OSD-BVO-V1.5", ["MInstanceID"] = (string?)id["MInstanceID"] ?? "tma", ["BasicVisualObjectID"] = "face-" + ((string?)id["InstanceIdentifier"] ?? TmaTypes.NewId("iid")),
        ["BasicVisualObjectTime"] = id["InstanceSpaceTime"]!.DeepClone(),
        ["VisualQualifier"] = new JsonObject
        {
            ["Header"] = "TFA-VIQ-V1.5", ["VisualQualifierID"] = TmaTypes.NewId("viq"), ["SubTypes"] = new JsonObject(), ["Formats"] = new JsonObject(),
            ["Attributes"] = new JsonObject { ["VisualObjectType"] = "Face", ["ObjectID"] = id.DeepClone() }
        },
        ["DescrMetadata"] = (string?)id["DescrMetadata"] ?? "a face"
    };

    private IEnumerable<(string DataType, int Port, JsonObject Json)> Face(JsonObject id)
    {
        var (label, confidence) = Named(id);
        var when = Span(id["InstanceTime"] ?? id["InstanceSpaceTime"]?["Time"]);
        if (label is null || when is null) yield break;
        var visual = FaceObject(id);
        faces.Add(new FaceSeen(label, when.Value.From, confidence, visual));
        // into the Visual Scene of its time, given again
        var scene = visualScenes.LastOrDefault(v => v.From - 1e-6 <= when.Value.From && when.Value.From <= v.To + 1e-6).Scene;
        if (scene is null) facesWaiting.Add(id);
        else { AddObject(scene, visual); yield return (TmaTypes.Bms, 1, (JsonObject)scene.DeepClone()); }
        // the utterances of this person during which it is seen, not placed yet
        foreach (var spoken in spokenScenes.Values)
            if (Place(spoken)) yield return (TmaTypes.Bms, 1, (JsonObject)spoken.DeepClone());
    }

    private static void AddObject(JsonObject scene, JsonObject visual)
    {
        var objects = scene["BasicObjectsData"] as JsonArray ?? new JsonArray(); scene["BasicObjectsData"] = objects;
        objects.Add(new JsonObject { ["BXOSpaceTime"] = visual["BasicVisualObjectTime"]!.DeepClone(), ["BXOOrBXOID"] = visual.DeepClone() });
    }

    // An utterance whose speaker is named and not yet placed: the face of that person seen nearest the middle of it. True if it was placed now.
    private bool Place(JsonObject scene)
    {
        if (SpokenObject(scene) is not { } speech || speech["BasicSpeechObjectSpaceTime"]?["SpatialAttitude1"] is not null) return false;
        var identity = speech["BasicSpeechDataQualifier"]?["Attributes"]?["Metadata"]?["SpeakerIdentity"];
        var (label, voice) = Named(identity);
        if (label is null || Span(scene["BMSDescriptorsSpaceTime"]?["Time"]) is not { } spoken) return false;
        var middle = (spoken.From + spoken.To) / 2;
        var seen = faces.Where(f => f.Label == label && f.Time >= spoken.From - 0.5 && f.Time <= spoken.To + 0.5).OrderBy(f => Math.Abs(f.Time - middle)).FirstOrDefault();
        if (seen is null) return false;
        var time = speech["BasicSpeechObjectTime"]?.DeepClone() ?? scene["BMSDescriptorsSpaceTime"]?["Time"]?.DeepClone();
        speech["BasicSpeechObjectSpaceTime"] = new JsonObject
        {
            ["Header"] = "OSD-SPT-V1.5", ["SpaceTimeID"] = TmaTypes.NewId("spt"),
            ["SpatialAttitude1"] = seen.Visual["BasicVisualObjectTime"]!["SpatialAttitude1"]!.DeepClone(), ["Time"] = time
        };
        var aligned = scene["AlignedMMObjects"] as JsonArray ?? new JsonArray(); scene["AlignedMMObjects"] = aligned;
        aligned.Add(new JsonObject
        {
            ["AlignmentCode"] = "SpeakerFace", ["Confidence"] = Math.Round(Math.Clamp(voice * seen.Confidence, 0, 1), 4),
            ["AlignedObjects"] = new JsonArray((string?)speech["BasicSpeechObjectID"] ?? "speech", (string?)seen.Visual["BasicVisualObjectID"] ?? "face")
        });
        return true;
    }

    public override IEnumerable<(string DataType, int Port, JsonObject Json)> Handle(string dataType, int port, JsonObject json)
    {
        if (!combine && dataType == TmaTypes.Iid && IsFace(json))
        {
            foreach (var given in Face(json)) yield return given;
            yield break;
        }
        if (!combine && dataType is TmaTypes.Bto or TmaTypes.Iid && Key(dataType == TmaTypes.Bto ? (json["BasicTextObjectSpaceTime"]?["Time"] ?? json["BasicTextObjectSpaceTime"]) : json["InstanceTime"]) is { } at)
        {
            if (spokenScenes.TryGetValue(at, out var held) && SpokenObject(held) is { } speech)
            {
                Record(speech, dataType, json);
                if (dataType == TmaTypes.Iid) Place(held);
                yield return (TmaTypes.Bms, 1, (JsonObject)held.DeepClone());
            }
            else
            {
                if (!waiting.TryGetValue(at, out var list)) waiting[at] = list = [];
                list.Add((dataType, (JsonObject)json.DeepClone()));
            }
            yield break;
        }
        switch (dataType)
        {
            case TmaTypes.Bto: texts.Add(json); yield break;
            case TmaTypes.Txo:
                // A Text Object is given as its Basic Text Objects.
                if (json["BasicTextObjects"] is JsonArray basics) texts.AddRange(basics.OfType<JsonObject>());
                else texts.Add(json);
                yield break;
            case TmaTypes.Iid: userId = json; yield break;
        }
        var st = (json["BasicSpeechSceneDescriptorsSpaceTime"] ?? json["BASSpaceTime"] ?? json["BVSDescriptorsSpaceTime"] ?? json["BasicVisualSceneDescriptorsSpaceTime"]) as JsonObject ?? TmaTypes.SpaceTime(DateTimeOffset.UtcNow);
        // The Scenes the Multimodal Scene holds: this one, or - combining - the one last received of each kind, in the order speech, audio, pictures.
        var scenes = new JsonArray();
        if (combine)
        {
            latest[dataType] = (JsonObject)json.DeepClone();
            foreach (var kind in new[] { TmaTypes.Bss, TmaTypes.Bas, TmaTypes.Bvs })
                if (latest.TryGetValue(kind, out var held)) scenes.Add(new JsonObject { ["BXSSpaceTime"] = st.DeepClone(), ["BXSOrBXSID"] = held.DeepClone() });
        }
        else scenes.Add(new JsonObject { ["BXSSpaceTime"] = st.DeepClone(), ["BXSOrBXSID"] = json.DeepClone() });
        var scene = new JsonObject
        {
            ["Header"] = "OSD-BMS-V1.5",
            ["MInstanceID"] = (string?)json["MInstanceID"] ?? "tma",
            ["BasicMultimodalSceneDescriptorsID"] = TmaTypes.NewId("bms"),
            ["BMSDescriptorsSpaceTime"] = st.DeepClone(),
            ["MultimodalObjectCount"] = scenes.Count,
            ["BasicMultimodalSceneDescriptorsData"] = scenes
        };
        // The Basic Texts that arrived and the User ID last received go in with the Scene, as Basic Objects of it.
        var objects = new JsonArray();
        foreach (var extra in texts.Concat(userId is null ? [] : [userId]))
            objects.Add(new JsonObject { ["BXOSpaceTime"] = st.DeepClone(), ["BXOOrBXOID"] = extra.DeepClone() });
        texts.Clear();
        if (objects.Count > 0) scene["BasicObjectsData"] = objects;
        // An utterance with its time: what waits for it goes into it, and it waits for what has not come yet.
        if (!combine && dataType == TmaTypes.Bss && SpokenObject(scene) is { } spoken && Key(st["Time"]) is { } when)
        {
            if (waiting.Remove(when, out var arrived)) foreach (var (kind, what) in arrived) Record(spoken, kind, what);
            spokenScenes[when] = scene;
            Place(scene);
        }
        if (!combine && dataType == TmaTypes.Bvs && Span(st["Time"]) is { } shown)
        {
            visualScenes.Add((shown.From, shown.To, scene));
            foreach (var id in facesWaiting.ToList())
                if (Span(id["InstanceTime"]) is { } t && shown.From - 1e-6 <= t.From && t.From <= shown.To + 1e-6) { AddObject(scene, FaceObject(id)); facesWaiting.Remove(id); }
        }
        yield return (TmaTypes.Bms, 1, (JsonObject)scene.DeepClone());
    }
}

// OSD-IDR-V1.5 - Identity Reconciliation, as Television Media Analysis needs it (2026-11-15). In a programme, faces and voices come at their own
// pace, not in pairs: each Instance Identifier is answered as it arrives - Port 1 the face, Port 2 the speaker. A speaker's identity goes on with
// its ObjectID and its time (the utterance it identifies); when the face last seen names the same subject, the two agree and the confidence rises
// (1 - (1-a)(1-b)); when they differ, the voice's identity goes on as it is (who speaks is a matter of the voice). A face's identity goes on as it is.
// The access-control IDR (OSD-MAC, CAV-HCI), which waits for a face and a voice to grant or deny, is another implementation of the same AIM.
public sealed class IdentityReconciliationEach(string instanceId) : TmaAim(instanceId)
{
    private JsonObject? lastFace;
    protected override (string, int)[] Inputs => [(TmaTypes.Iid, 1), (TmaTypes.Iid, 2)];

    private static (string? Label, double Confidence) Top(JsonObject? id) =>
        id?["InstanceIdentifierData"]?[0] is JsonObject c && (string?)c["InstanceLabel"] is { } label && label is not ("person" or "face" or "speech")
            ? (label, c["LabelConfidenceLevel"] is JsonValue v && v.TryGetValue<double>(out var x) ? x : 0) : (null, 0);

    // The faces seen, with their programme time (2026-11-18: a picture now has every face, frame by frame): a voice is confirmed by a face of the same person seen during
    // its utterance (from half a second before to half a second after); an identity without a time is compared with the face last seen, as before.
    private readonly List<(string Label, double Time, double Confidence)> seen = [];

    private static (double From, double To)? Span(JsonNode? time) =>
        (time?["SimpleTimeData"] as JsonArray)?.FirstOrDefault() is JsonObject d && d["StartTime"] is JsonValue a && a.TryGetValue<double>(out var f) && d["EndTime"] is JsonValue b && b.TryGetValue<double>(out var t) ? (f, t) : null;

    public override IEnumerable<(string DataType, int Port, JsonObject Json)> Handle(string dataType, int port, JsonObject json)
    {
        if (port == 1)
        {
            lastFace = json;
            var (who, c) = Top(json);
            if (who is not null && Span(json["InstanceTime"]) is { } at) seen.Add((who, at.From, c));
            yield return (TmaTypes.Iid, 1, (JsonObject)json.DeepClone()); yield break;
        }
        var reconciled = (JsonObject)json.DeepClone();
        var (voice, a) = Top(json);
        var (face, b) = Top(lastFace);
        if (voice is not null && Span(json["InstanceTime"]) is { } spoken)
        {
            var during = seen.Where(f => f.Label == voice && f.Time >= spoken.From - 0.5 && f.Time <= spoken.To + 0.5).ToList();
            (face, b) = during.Count > 0 ? (voice, during.Max(f => f.Confidence)) : (null, 0);
        }
        if (voice is not null && voice == face && reconciled["InstanceIdentifierData"]?[0] is JsonObject top) top["LabelConfidenceLevel"] = 1 - (1 - a) * (1 - b);
        yield return (TmaTypes.Iid, 1, reconciled);
    }
}

// OSD-MED-V1.5 - Multimodal Event Description. It collects the Descriptors of the Scenes that arrive into the Multimodal Event Descriptors, nothing more.
// A Scene given again with the same ID (the Multimodal Scene now holding the text and the identity of its utterance) takes the place of the one
// it completes: the MEV records what it receives, once for each Scene.
public sealed class MultimodalEventDescription(string instanceId) : TmaAim(instanceId)
{
    private readonly JsonArray collected = [];
    private readonly string id = TmaTypes.NewId("mev");
    protected override (string, int)[] Inputs => [(TmaTypes.Bms, 1)];

    public override IEnumerable<(string DataType, int Port, JsonObject Json)> Handle(string dataType, int port, JsonObject json)
    {
        var sceneId = (string?)json["BasicMultimodalSceneDescriptorsID"];
        var same = sceneId is null ? -1 : collected.ToList().FindIndex(s => (string?)s?["BasicMultimodalSceneDescriptorsID"] == sceneId);
        if (same >= 0) collected[same] = json.DeepClone();
        else collected.Add(json.DeepClone());
        yield return (TmaTypes.Mev, 1, new JsonObject
        {
            ["Header"] = "OSD-MEV-V1.5",
            ["MInstanceID"] = (string?)json["MInstanceID"] ?? "tma",
            ["MultimodalEventDescriptorsID"] = id,
            ["MultimodalEventDescriptorsTime"] = TmaTypes.SimpleTime(DateTimeOffset.UtcNow),
            ["MultimodalSceneDescriptors"] = collected.DeepClone()
        });
    }
}
