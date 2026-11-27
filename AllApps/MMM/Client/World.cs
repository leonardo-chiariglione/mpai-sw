using System.Globalization;
using System.Text.Json.Nodes;

namespace Mpai.Mmm.Client;

// THE WORLD OF THE MMM, AS THE M-INSTANCE HAS IT (AllApps/MMM/World/world.json, written by make_world.py): the Metaverse Square, the buildings on it and the rooms in them.
// A building is a Location of three dimensions - a Location Object with its Sub-Locations, the rooms - rendered by a 3D Model Object (glTF); the Square is a Location of two.
// A door is a Right: a Process that holds the Right to move its Persona to the Location behind the door may enter, and the others are refused (403). The buildings and
// their rooms are the MPAI Building Service's: not public. The MPAI building's atrium is public: anybody may enter, look, talk and ask. Its wings and rooms are not: the meeting room of a group holds the table and the seats of its meeting.
public static class World
{
    // The Rights the MPAI Building Service gives a User it invites, and takes back when it takes the User out: to move any of its Personae through the doors of the Town Hall.
    public static string DoorID(string user, string location) => $"PA-Door-{user}-{location}";
    public static JsonObject DoorPA(string user, string location) =>
        Uc2.PA("MMM-2MP-V2.2", "MMMove", DoorID(user, location), ("RQ3", "To", "PointOfView2", Uc2.PointOfView(location)));

    // A WALK INTO A PLACE (2026-11-27): MM-Animate of a Persona At the M-Location, the Persona not named (any of the holder's) - the Right an A-User needs to walk there.
    public static string WalkID(string user, string location) => $"PA-Walk-{user}-{location}";
    public static JsonObject WalkPA(string user, string location) =>
        Uc2.PA("MMM-2AP-V2.2", "MMAnimate", WalkID(user, location), ("RQ3", "At", "MLocationID", JsonValue.Create(location)!));

    // THE AREAS OF THE MPAI BUILDING (2026-11-27): each place that is not public belongs to the area of its wing - the first place through which it is reached from a public
    // one: the Members' wing (the Member Companies area), the Meeting rooms, the Offices of MPAI.
    public const string MembersArea = "MembersWing", MeetingsArea = "MeetingsWing", OfficesArea = "OfficesWing";
    public static Dictionary<string, string[]> Areas(JsonObject world) =>
        world["buildings"]!.AsArray().OfType<JsonObject>().SelectMany(b => b["rooms"]!.AsArray().OfType<JsonObject>()).Where(r => (bool?)r["public"] != true)
             .Select(r => (Room: (string)r["id"]!, Area: DoorsTo(world, (string)r["id"]!).FirstOrDefault()))
             .Where(x => x.Area is not null).GroupBy(x => x.Area!).ToDictionary(g => g.Key, g => g.Select(x => x.Room).ToArray());

    public static JsonObject Load(string repo) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(repo, "AllApps", "MMM", "Viewer", "world", "world.json")))!.AsObject();

    // The Square, the buildings and the rooms as Locations, and the seats of the meeting room; the Services of the meeting. Returns the world as read.
    public static JsonObject Setup(MInstance m, string repo)
    {
        var world = Load(repo);
        var sq = world["square"]!;
        m.AddLocation((string)sq["id"]!, isPublic: true, x: (double)sq["x"]!, y: (double)sq["y"]!, size: (double)sq["size"]!);
        m.AddUser("Seller", "human0");
        foreach (var b in world["buildings"]!.AsArray().OfType<JsonObject>())
        {
            var fp = b["footprint"]!.AsArray().Select(v => (double)v!).ToArray();
            // A building the world says is public (the MPAI building: its atrium is a public space) is entered by anybody; its wings and rooms are the MPAI Building Service's, entered with a Right.
            m.AddLocation((string)b["id"]!, holder: Meeting.BuildingService, isPublic: (bool?)b["public"] == true, x: (double)b["x"]!, y: (double)b["y"]!, size: Math.Max(fp[2] - fp[0], fp[3] - fp[1]));
            m.Get((string)b["id"]!)!.Model = (string)b["model"]!;
            foreach (var r in b["rooms"]!.AsArray().OfType<JsonObject>())
            {
                var rf = r["footprint"]!.AsArray().Select(v => (double)v!).ToArray();
                m.AddLocation((string)r["id"]!, holder: Meeting.BuildingService, isPublic: (bool?)r["public"] == true, x: (double)r["x"]!, y: (double)r["y"]!, size: Math.Max(rf[2] - rf[0], rf[3] - rf[1]), at: (string)b["id"]!);
                m.Get((string)r["id"]!)!.Body = RoomLocationObject(r);
                if ((string)r["id"]! != Meeting.Room) continue;
                Meeting.ParcelX = (double)b["x"]! + (double)r["x"]!; Meeting.ParcelY = (double)b["y"]! + (double)r["y"]!;      // where the centre of the meeting room is in the world
                Meeting.Seats = r["seats"]!.AsArray().OfType<JsonObject>().Select(s => ((string)s["id"]!, (double)s["x"]!, (double)s["y"]!, (double)s["yaw"]!)).ToArray();
                if (Meeting.PlannedSeats is int planned) Meeting.Seats = Meeting.Plan(Meeting.Seats, planned);      // the room has the seats that were planned, and no more
                foreach (var s in Meeting.Seats) m.AddLocation(s.ID, holder: Meeting.BuildingService, x: s.X, y: s.Y, size: 0.8, perceptible: false, at: Meeting.Room);
            }
        }
        // THE DOORS ARE ITEMS (2026-11-27): each door of the world is an Item (a 3D Model Object, Kind Door) at the place it is entered from, that gives access to the place beyond
        // (Leads); the MPAI Building Service holds the Rights to it, as to the places. Its Item Properties: OpenState (it starts open) and LockState. Who may enter the place
        // beyond may open and close it; closed, it stops a Persona.
        var at = new Dictionary<string, (double X, double Y)> { [(string)sq["id"]!] = ((double)sq["x"]!, (double)sq["y"]!) };
        foreach (var b in world["buildings"]!.AsArray().OfType<JsonObject>())
        {
            at[(string)b["id"]!] = ((double)b["x"]!, (double)b["y"]!);
            foreach (var r in b["rooms"]!.AsArray().OfType<JsonObject>()) at[(string)r["id"]!] = ((double)b["x"]! + (double)r["x"]!, (double)b["y"]! + (double)r["y"]!);
        }
        foreach (var b in world["buildings"]!.AsArray().OfType<JsonObject>())
            foreach (var d in (b["doors"] as JsonArray ?? []).Concat(b["rooms"]!.AsArray().Select(r => r?["door"])).OfType<JsonObject>())
            {
                var (id, from, to) = ((string)d["id"]!, (string)d["from"]!, (string)d["to"]!);
                var o = at.GetValueOrDefault(from);
                m.AddItem(id, "OSD-3DO-V1.5", Meeting.BuildingService, from, (double)d["x"]! - o.X, (double)d["y"]! - o.Y, kind: "Door",
                          properties: [("OpenState", JsonValue.Create("Open")), ("LockState", JsonValue.Create("Unlocked"))]);
                m.SetLeads(id, to);
            }
        Meeting.DoorsNeedRights = true;
        Meeting.Doors = DoorsTo(world, Meeting.Room);
        Meeting.Areas = Areas(world);
        Meeting.AllPlaces = world["buildings"]!.AsArray().OfType<JsonObject>().SelectMany(b => b["rooms"]!.AsArray().OfType<JsonObject>()).Where(r => (bool?)r["public"] != true).Select(r => (string)r["id"]!).ToArray();
        m.AddService(Meeting.BuildingService);
        m.AddService(Meeting.Asr);
        m.AddService(Meeting.Secretary, Meeting.SecretaryPersona, "catalogue/Female_Adult_05.glb");
        foreach (var b in world["buildings"]!.AsArray().OfType<JsonObject>())
            if (b["reception"] is JsonObject rc)
            {
                Meeting.Reception = ((string)rc["persona"]!, (string)rc["model"]!, "Atrium", (double)rc["x"]!, (double)rc["y"]!, (double)rc["yaw"]!);
                m.AddService(Meeting.ReceptionService, Meeting.Reception.Value.Persona, Meeting.Reception.Value.Model);
            }
        return world;
    }

    // The Locations a Persona goes through to reach a room, that need a Right: from the public atrium, the wing, the room.
    public static string[] DoorsTo(JsonObject world, string room)
    {
        var chain = new List<string>();
        foreach (var b in world["buildings"]!.AsArray().OfType<JsonObject>())
        {
            var rooms = b["rooms"]!.AsArray().OfType<JsonObject>().ToDictionary(r => (string)r["id"]!);
            for (var at = room; rooms.TryGetValue(at, out var r) && (bool?)r["public"] != true; at = (string)r["door"]!["from"]!) chain.Insert(0, at);
        }
        return chain.ToArray();
    }

    // The meeting room as an OSD Location Object: a Location of the M-Instance, Virtual (Location Qualifier), whose Attributes carry its Acoustic Profile (CAE3: the Scene's
    // reflectivity, reverberation, diffusion and absorption), which a client renders the speech of the room with.
    public static JsonObject RoomLocationObject(JsonObject room)
    {
        var f = room["footprint"]!.AsArray().Select(v => (double)v!).ToArray();
        var id = (string)room["id"]!;
        return new JsonObject
        {
            ["Header"] = "OSD-BLC-V1.5", ["MInstanceID"] = "MI1", ["BasicLocationObjectID"] = id,
            ["BasicLocationObjectSpaceTime"] = new JsonObject { ["Extent"] = new JsonArray(f[2] - f[0], f[3] - f[1], (double)room["height"]!) },
            ["LocationQualifier"] = new JsonObject
            {
                ["Header"] = "TFA-LCQ-V1.5", ["LocationQualifierID"] = id + "-Q", ["SubTypes"] = new JsonObject { ["Categories"] = new JsonArray("Virtual") },
                ["Attributes"] = new JsonObject { ["AcousticProfile"] = room["acoustics"]!.DeepClone() }
            }
        };
    }

    public static string Invariant(double v) => v.ToString(CultureInfo.InvariantCulture);
}
