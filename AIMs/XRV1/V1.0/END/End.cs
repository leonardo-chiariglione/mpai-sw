using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using AIF.Controller;

namespace Mpai.Xrv.End;

// XRV-END-V1.0, Environment Description. In: RE Data In, VE Data In, the two Venue Specifications, the Participant Data, the Script, the Action Table and the Script-Venue Associations.
// Out: the RE and VE Performance, Participant and Operator Descriptors, and the Show Descriptors (each Port Number 1).
//
// THE SIMPLEST READING of "using the Script-Venue Associations to identify the data": each element of a Data In names its source - an Audio, 3D Model, Visual, MoCap or LiDAR Object
// by its own ID, a Control Object by its ControllerID. That name is looked up among the VenueElementIDs of the Associations of the same side, and the Designation it is found in says
// what the element describes:
//   Characters, PerformerAvatars        -> a performer of the Performance Descriptors (its ID is the VenueElementID)
//   TheatricalSetList (Sets, Objects)   -> an object of the Performance Descriptors (with the Control Object, if the element is one)
//   Participants, ParticipantAvatars    -> a participant of the Participant Descriptors (with the Control Object; a Control Object of a source whose DataSourceQualifier is "seat"
//                                          gives the SeatID in its Control Data)
//   Consoles (Operator Designations)    -> the Operator Descriptors: the Control Object goes in the Show Console (Rigging), the place the Status Interpretation reads a show control from
// An element whose source is in no Association is left out (reported once per source). Nothing is inferred from the media (no recognition of who is on stage): that is a later stage.
//
// Each Data In gives one set of Descriptors per Family it touched, and the Show Descriptors: all the latest Descriptors of both sides together.
public sealed class EndAimProcessor : IAimProcessor, IAimRunner
{
    public const string RDI = "XRV-RDI-V1.0", VDI = "XRV-VDI-V1.0", SHD = "XRV-SHD-V1.0";
    private readonly Task? startGate;

    public EndAimProcessor(string instanceId, Task? startGate = null) { InstanceId = instanceId; this.startGate = startGate; }

    public string InstanceId { get; }
    public Task<Message> ProcessAsync(Message message) => throw new NotSupportedException("XRV-END runs continuously.");

    private static readonly (string Array, string Item, string Id)[] MediaElements =
    [
        ("AudioObjects", "AudioObject", "AudioObjectID"), ("3DModelObjects", "3DModelObject", "3DModelObjectID"), ("VisualObjects", "VisualObject", "VisualObjectID"),
        ("MoCapObjects", "MoCapObject", "MoCapObjectID"), ("LiDARObjects", "LiDARObject", "LiDARObjectID"), ("MetaverseObjects", "MetaverseObject", "MetaverseObjectID")
    ];

    // Where a VenueElementID is designated: the Family, and for the Performance Family whether it is a performer or an object.
    public enum Role { Performer, Object, Participant, Console }
    public sealed record Source(Role Role, string VenueElementId, string? SourceQualifier);

    public static Dictionary<string, Source> Sources(ScriptVenueAssociations? sva, bool re)
    {
        var found = new Dictionary<string, Source>(StringComparer.Ordinal);
        if (sva?.Json[re ? "REAssociations" : "VEAssociations"] is not JsonObject a) return found;
        void Add(JsonNode? list, Role role)
        {
            foreach (var item in (list as JsonArray) ?? [])
                if (item?["VenueElementID"]?.GetValue<string>() is { } id)
                    found.TryAdd(id, new Source(role, id, item["DataSourceQualifier"]?.GetValue<string>()));
        }
        Add(a["PerformanceDesignations"]?["Characters"], Role.Performer);
        Add(a["PerformanceDesignations"]?["PerformerAvatars"], Role.Performer);
        foreach (var set in (a["PerformanceDesignations"]?["TheatricalSetList"] as JsonArray) ?? [])
        {
            Add(set?["TheatricalSet"], Role.Object);
            Add(set?["Objects"], Role.Object);
        }
        Add(a["ParticipantDesignations"]?["Participants"], Role.Participant);
        Add(a["ParticipantDesignations"]?["ParticipantAvatars"], Role.Participant);
        Add(a["OperatorDesignations"]?["Consoles"], Role.Console);
        return found;
    }

    // The Descriptors of one side made from one Data In: (Family, Descriptors JSON) for each Family the Data In touched.
    public static List<(Family Family, JsonObject Descriptors)> Describe(JsonObject dataIn, bool re, Dictionary<string, Source> sources, string id, double nowMs, Action<string>? unknown = null)
    {
        var s = re ? "RE" : "VE";
        var content = dataIn[re ? "REDataIn" : "VEDataIn"] as JsonObject ?? new JsonObject();
        var performers = new List<JsonObject>(); var objects = new List<JsonObject>(); var participants = new List<JsonObject>(); var consoles = new List<JsonObject>();
        var sounds = new Dictionary<Role, List<JsonObject>>();          // the Audio Objects of performers and participants: the Multimodal Scene Descriptors of their Family

        // N-26: what an element says of the person it describes - the body (a Body Descriptors Object), where it is and faces (a Spatial Attitude), its sound (an Audio Object).
        void Add(JsonObject person, JsonObject? body, JsonObject? attitude)
        {
            if (attitude is not null) person["SpatialAttitude"] = attitude.DeepClone();
            if (body is null) return;
            if (re) person["BodyDescriptorsObject"] = body.DeepClone();
            else
            {
                var id = person.First().Value!.GetValue<string>();
                person["AvatarOrAvatarID"] = new JsonArray(new JsonObject { ["Header"] = "PAF-PAV-V1.6", ["PortableAvatarID"] = id, ["BodyDescriptorsObject"] = body.DeepClone() });
            }
        }

        void Place(string? name, JsonObject? control, JsonObject? body = null, JsonObject? attitude = null, JsonObject? sound = null)
        {
            if (name is null) return;
            if (!sources.TryGetValue(name, out var source)) { unknown?.Invoke(name); return; }
            if (sound is not null && source.Role is Role.Performer or Role.Participant)
                (sounds.TryGetValue(source.Role, out var l) ? l : sounds[source.Role] = []).Add(sound);
            switch (source.Role)
            {
                case Role.Performer:
                    var performer = performers.FirstOrDefault(p => p[$"{s}PerformerID"]!.GetValue<string>() == name);
                    if (performer is null) performers.Add(performer = new JsonObject { [$"{s}PerformerID"] = name });
                    Add(performer, body, attitude);
                    break;
                case Role.Object:
                    var o = new JsonObject { [$"{s}ObjectID"] = name };
                    if (control is not null) o["ControlObject"] = control.DeepClone();
                    objects.Add(o);
                    break;
                case Role.Participant:
                    var p = participants.FirstOrDefault(x => x[$"{s}ParticipantID"]!.GetValue<string>() == name);
                    if (p is null) participants.Add(p = new JsonObject { [$"{s}ParticipantID"] = name });
                    Add(p, body, attitude);
                    if (control is not null)
                    {
                        if (re && source.SourceQualifier == "seat" && control["ControlData"]?.GetValue<string>() is { Length: > 0 } seat) p["SeatID"] = seat;
                        else p["ControlObject"] = control.DeepClone();
                    }
                    break;
                case Role.Console:
                    if (control is not null) consoles.Add(control);
                    break;
            }
        }

        foreach (var (array, item, idKey) in MediaElements)
            foreach (var element in (content[array] as JsonArray) ?? [])
            {
                var o = element?[item] as JsonObject;
                var (body, attitude) = Carried(o);
                Place(o?[idKey]?.GetValue<string>(), null, body, attitude, array == "AudioObjects" ? o : null);
            }
        foreach (var element in (content["ControlObjects"] as JsonArray) ?? [])
            if (element?["ControlObject"] is JsonObject control) Place(control["ControllerID"]?.GetValue<string>(), control);

        var environment = dataIn["UEnvironmentID"]?.DeepClone();
        JsonObject Head(Family family)
        {
            var info = KindInfo.Of(Enum.Parse<StatusKind>(s + family));
            var d = new JsonObject { ["Header"] = info.DescriptorsHeader, [info.DescriptorsIdKey] = $"{id}-{family}", ["UEnvironmentID"] = environment?.DeepClone(), [info.DescriptorsTimeKey] = XrvTime.Simple($"{id}-{family}-t", nowMs) };
            if (dataIn["MInstanceID"] is { } instance) d["MInstanceID"] = instance.DeepClone();
            return d;
        }

        // The Audio Objects of a Family, as the Multimodal Scene Descriptors (OSD-MSD-V1.5) of its Descriptors: what the Status Interpretation hears.
        void Scene(JsonObject d, Role role, Family family)
        {
            if (!sounds.TryGetValue(role, out var list) || list.Count == 0) return;
            d["MultimodalSceneDescriptors"] = new JsonObject
            {
                ["Header"] = "OSD-MSD-V1.5", ["MultimodalSceneDescriptorsID"] = $"{id}-{family}-msd", ["MultimodalSceneDescriptorsSpaceTime"] = new JsonObject { ["Header"] = "OSD-SPT-V1.5", ["SpaceTimeID"] = $"{id}-{family}-msd-st" },
                ["AudioObjectCount"] = list.Count,
                ["AudioObjects"] = new JsonArray(list.Select(a => (JsonNode)new JsonObject { ["AudioObject"] = a.DeepClone() }).ToArray())
            };
        }

        var made = new List<(Family, JsonObject)>();
        if (performers.Count > 0 || objects.Count > 0)
        {
            var d = Head(Family.Performance);
            Scene(d, Role.Performer, Family.Performance);
            d[$"{s}PerformanceDescriptorsData"] = new JsonArray(performers.Select(x => (JsonNode)x).ToArray());
            if (objects.Count > 0) d[$"{s}ObjectDescriptors"] = new JsonArray(objects.Select(x => (JsonNode)x).ToArray());
            made.Add((Family.Performance, d));
        }
        if (participants.Count > 0)
        {
            var d = Head(Family.Participant);
            Scene(d, Role.Participant, Family.Participant);
            d[$"{s}ParticipantDescriptorsData"] = new JsonArray(participants.Select(x => (JsonNode)x).ToArray());
            made.Add((Family.Participant, d));
        }
        if (consoles.Count > 0)
        {
            var d = Head(Family.Operator);
            d[$"{s}OperatorDescriptors"] = new JsonObject
            {
                ["ShowConsole"] = new JsonObject
                {
                    ["Header"] = "XRV-SHC-V1.0", ["ShowConsoleID"] = $"{id}-sc", ["UEnvironmentID"] = environment?.DeepClone(), ["ShowConsoleTime"] = XrvTime.Simple($"{id}-sc-t", nowMs),
                    ["ShowConsoleData"] = new JsonArray(consoles.Select(c => (JsonNode)new JsonObject { ["Rigging"] = new JsonObject { ["OperationID"] = c["ControllerID"]!.GetValue<string>(), ["ControlObject"] = c.DeepClone() } }).ToArray())
                }
            };
            made.Add((Family.Operator, d));
        }
        return made;
    }

    // WHAT AN ELEMENT OF A DATA IN CARRIES ABOUT A PERSON (N-26): a Body Descriptors Object (PAF-BDO-V1.6, the body) and a Spatial Attitude (OSD-OSA-V1.5, where it is and faces), each
    // as the text of one Data item of a MoCap Object (its MoCap Data) or of a Metaverse Object (its Metaverse Object Data). The simplest places the schemas give (the Space-Time of a
    // 3D Model Object is a Simple Time, with no Spatial Attitude); nothing is computed here.
    public static (JsonObject? Body, JsonObject? Attitude) Carried(JsonObject? element)
    {
        JsonObject? body = null, attitude = null;
        if (element is null) return (null, null);
        foreach (var d in ((element["MoCapData"] ?? element["MetaverseObjectData"]) as JsonArray) ?? [])
        {
            if (d?["Data"] is not JsonValue v || !v.TryGetValue<string>(out var text) || !text.StartsWith('{')) continue;
            JsonObject? o;
            try { o = JsonNode.Parse(text) as JsonObject; } catch (System.Text.Json.JsonException) { continue; }
            switch (o?["Header"]?.GetValue<string>())
            {
                case "PAF-BDO-V1.6": body = o; break;
                case "OSD-OSA-V1.5": attitude = o; break;
            }
        }
        return (body, attitude);
    }

    // The Show Descriptors: all the latest Descriptors of both sides, each under its own name.
    public static JsonObject Show(IReadOnlyDictionary<StatusKind, JsonObject> latest, string id, double nowMs, JsonNode? environment)
    {
        var all = new JsonObject();
        foreach (var info in KindInfo.All)
            if (latest.TryGetValue(info.Kind, out var d)) all[info.DescriptorsKey] = d.DeepClone();
        return new JsonObject { ["Header"] = SHD, ["ShowDescriptorsID"] = id, ["UEnvironmentID"] = environment?.DeepClone() ?? "", ["ShowDescriptorsTime"] = XrvTime.Simple(id + "-t", nowMs), ["ShowDescriptors"] = all };
    }

    public async Task RunAsync(IAimPorts ports, AimContext context)
    {
        if (startGate is not null) await startGate;
        ScriptVenueAssociations? sva = null;
        var latest = new Dictionary<StatusKind, JsonObject>();
        var reported = new HashSet<string>();
        long count = 0;

        // SelectAsync gives the first of these Ports with a Message waiting: what sets up the show comes first, so that where both wait - an AIM slow to start -
        // the Script-Venue Associations are taken in before the Data In they describe (2026-11-14).
        var inputs = new[] { (Types.ScriptVenueAssociations, 1), (Types.REVenueSpecification, 1), (Types.VEVenueSpecification, 1), (Types.ParticipantData, 1), (Types.Script, 1), (Types.ActionTable, 1), (RDI, 1), (VDI, 1) };
        while (await ports.SelectAsync(-1, inputs) is { } port)
        {
            if (await ports.ReadAsync(port.DataType, port.PortNumber, 0) is not { } message) continue;
            try
            {
                if (port.DataType == Types.ScriptVenueAssociations) { sva = ScriptVenueAssociations.Read(message.Json); continue; }
                if (port.DataType is not (RDI or VDI)) continue;                                  // the Venue Specifications, Participant Data, Script and Action Table: received, not used
                var re = port.DataType == RDI;
                var dataIn = JsonNode.Parse(message.Json) as JsonObject ?? throw new FormatException("the Data In is not a JSON object");
                var violations = XrvSchemas.Violations(re ? "XRV1/V1.0/data/REDataIn.json" : "XRV1/V1.0/data/VEDataIn.json", dataIn);
                if (violations.Count > 0) throw new FormatException($"not a valid {port.DataType}: {string.Join("; ", violations)}");
                if (sva is null) { context.Report("a Data In came before the Script-Venue Associations: not described"); continue; }

                var id = $"end-{++count}";
                var now = ports.Now.ToUnixTimeMilliseconds();
                var made = Describe(dataIn, re, Sources(sva, re), id, now, name => { if (reported.Add(name)) context.Report($"the source '{name}' is in no Script-Venue Association: left out"); });
                foreach (var (family, descriptors) in made)
                {
                    var info = KindInfo.Of(Enum.Parse<StatusKind>((re ? "RE" : "VE") + family));
                    latest[info.Kind] = descriptors;
                    await ports.WriteAsync(info.DescriptorsHeader, 1, descriptors.ToJsonString(), -1);
                }
                if (made.Count > 0) await ports.WriteAsync(SHD, 1, Show(latest, id + "-show", now, dataIn["UEnvironmentID"]).ToJsonString(), -1);
            }
            catch (FormatException bad) { context.Report(bad.Message); }
        }
    }
}
