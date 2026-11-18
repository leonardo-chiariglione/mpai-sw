"""N-34 step 3 (night 2026-11-18): Multimodal Scene Multiplexing of Television Media Analysis records the faces (where and when) and the position of each utterance
(the face of its speaker at that time), with the voice-face pairing as Aligned Objects."""
from pathlib import Path

P = Path(r"D:\AI\AIMs\OSD\V1.5\TMA\TmaAim.cs")
raw = P.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")


def rep(old, new):
    global s
    assert s.count(old) == 1, old[:70]
    s = s.replace(old, new)


# 1. the time of a visual scene is in BVSDescriptorsSpaceTime (its schema's name)
rep("""        var st = (json["BasicSpeechSceneDescriptorsSpaceTime"] ?? json["BASSpaceTime"] ?? json["BasicVisualSceneDescriptorsSpaceTime"]) as JsonObject ?? TmaTypes.SpaceTime(DateTimeOffset.UtcNow);""",
"""        var st = (json["BasicSpeechSceneDescriptorsSpaceTime"] ?? json["BASSpaceTime"] ?? json["BVSDescriptorsSpaceTime"] ?? json["BasicVisualSceneDescriptorsSpaceTime"]) as JsonObject ?? TmaTypes.SpaceTime(DateTimeOffset.UtcNow);""")

# 2. faces: before the handling of a timed identity
rep("""    public override IEnumerable<(string DataType, int Port, JsonObject Json)> Handle(string dataType, int port, JsonObject json)
    {
        if (!combine && dataType is TmaTypes.Bto or TmaTypes.Iid && Key(""",
"""    // WHERE AND WHEN THE FACES ARE (2026-11-18). An Instance Identifier of a face (Face Identity Recognition of Television Media Analysis: its taxonomy says face, its
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
        time?["SimpleTimeData"]?[0] is JsonObject d && d["StartTime"] is JsonValue a && a.TryGetValue<double>(out var f) && d["EndTime"] is JsonValue b && b.TryGetValue<double>(out var t) ? (f, t) : null;

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
        if (!combine && dataType is TmaTypes.Bto or TmaTypes.Iid && Key(""")

# 3. an identity recorded into an utterance: place it
rep("""            if (spokenScenes.TryGetValue(at, out var held) && SpokenObject(held) is { } speech)
            {
                Record(speech, dataType, json);
                yield return (TmaTypes.Bms, 1, (JsonObject)held.DeepClone());
            }""",
"""            if (spokenScenes.TryGetValue(at, out var held) && SpokenObject(held) is { } speech)
            {
                Record(speech, dataType, json);
                if (dataType == TmaTypes.Iid) Place(held);
                yield return (TmaTypes.Bms, 1, (JsonObject)held.DeepClone());
            }""")

# 4. a new utterance scene: place it if its identity came first; a new visual scene: remember it and take the faces that wait for it
rep("""            if (waiting.Remove(when, out var arrived)) foreach (var (kind, what) in arrived) Record(spoken, kind, what);
            spokenScenes[when] = scene;
        }""",
"""            if (waiting.Remove(when, out var arrived)) foreach (var (kind, what) in arrived) Record(spoken, kind, what);
            spokenScenes[when] = scene;
            Place(scene);
        }
        if (!combine && dataType == TmaTypes.Bvs && Span(st["Time"]) is { } shown)
        {
            visualScenes.Add((shown.From, shown.To, scene));
            foreach (var id in facesWaiting.ToList())
                if (Span(id["InstanceTime"]) is { } t && shown.From - 1e-6 <= t.From && t.From <= shown.To + 1e-6) { AddObject(scene, FaceObject(id)); facesWaiting.Remove(id); }
        }""")
P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
