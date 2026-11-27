using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace Mpai.Mmm;

// AN M-INSTANCE OF MMM-TEC V2.2. It holds Processes (Humans' Users, Services) and
// Items, and its Services perform the Process Actions the Processes request: each
// request goes to the Service of its Action (Register to RGSrvc, Transact to TRSrvc,
// MM-Add, MM-Move, MM-Animate and Property Change to LCSrvc, ...), which checks the
// Rights of the requesting Process, performs the Action and answers.
//
// RIGHTS (Rights chapter; the author, 2026/10/01). A Rights Item lists Rights, each a
// Deontic Verb, the Process Actions it concerns and a Level - Internal, from
// registration; Acquired, from Process Actions with other Processes; Granted, by a
// User to another, for a time. A Right names its Process Actions by ID (PAIDOrPA):
// the M-Instance keeps those Process Actions, concrete ones (an MM-Move PA,
// MMM-2MP-V2.2, with its Nil and To), and retrieves them to see what the Right
// allows. A Process holds a Rights Item once a Service has made it Final: a Request
// carries it at Status=Model, the Response returns it at Status=Final. May Not
// prevails over May. What a Process owns - its Personae, what it bought, what it
// placed - it may act on without a Right.
public sealed class MInstance
{
    public sealed class Item
    {
        public required string ID { get; init; }
        public required string DataType { get; set; }
        public JsonObject? Body { get; set; }
        public string? Holder { get; set; }   // the Process holding the Rights to the Item (the MMM has no owners, only Rights holders)
        public string? Location { get; set; }        // the M- or U-Location the Item is at
        public bool Public { get; set; }             // a Location any Process may enter
        public bool Perceptible { get; set; } = true;
        public string Status { get; set; } = "Final";
        public JsonNode? SpatialAttitude { get; set; }
        public string? Animation { get; set; }       // the Animation Stream animating it
        public JsonObject? Qualifier { get; set; }   // Data UM-Captured and not yet Identified: the Qualifier it was captured with
        public long Started { get; set; }            // Data UM-Captured: when (Unix milliseconds); a streamed Speech Object has no duration until its stream is over, and then this is its start
        public double[]? Place { get; set; }         // a Location's place, x (east) and y (north) in metres: in the
                                                     // M-Environment, or in the Location it is at
        public double Size { get; set; }             // a Location's side, metres
        public string? Model { get; set; }           // the 3D Model that renders it (a GLB file)
        public bool Streaming { get; set; }          // a Speech Object whose data is still arriving, as a stream
        public JsonObject? LightSource { get; set; } // the Light Source (CVE-LTS-V1.0) a Property Change gave it: its Type, Colour and Intensity
        public Dictionary<string, JsonNode?> Properties { get; } = new(StringComparer.Ordinal);   // its Item Properties (MMM-IPR-V3.0, RQ12 of Property Change V3.0): OpenState of a door...
        public string? Kind { get; set; }            // what the viewer draws it as, when its Data Type does not say (a Door is a 3D Model Object, as a Persona is)
        public double[]? WalkFrom, WalkTo;           // a walk its Animation Stream makes (MM-Animate At an M-Location): from and to, metres in the M-Environment
        public long WalkStart, WalkEnd, AnimatedUntil;   // when the walk starts and ends, and when the animation is over (Unix milliseconds)
        public double[][]? WalkPath;                 // the walk's way when it goes round an obstacle: from, the corners it turns at, to (metres in the M-Environment)
        public double[]? Footprint;                  // an Item a walk goes round (a table): its width east-west and its depth north-south, metres
    }

    public sealed class Process
    {
        public required string ID { get; init; }
        public required string Kind { get; init; }   // Human, User, Service
        public string? HumanID { get; init; }
        public string? Token { get; set; }
        public bool Autonomous { get; set; }         // an A-User: commanded by its human with words, it acts with autonomy (2026-11-27); else the H-User, driven by its human directly
    }

    // H-USER AND A-USER (the author, 2026-11-27). A registered human may deploy two Users, each with one Persona: the H-User, which the human drives directly, and the A-User,
    // which the human commands by text or speech and which acts with autonomy. The A-User's Persona is one of the catalogue's that make it recognisable as an A-User (the military
    // ones); the H-User's is any other. Which User is which follows from the Persona chosen for it: the Personal Profile (MMM-PPR) has no field for it.
    public Func<string, bool> ForAUsers { get; set; } = name => name.StartsWith("Military_", StringComparison.Ordinal);
    // A VISITOR (the author, 2026-11-27) has one H-User only, no A-User, and its Persona is of another class, the sports Personae: everybody sees it is a Visitor. The other
    // humans' H-Users take neither the Visitors' Personae nor the A-Users'.
    public Func<string, bool> ForVisitors { get; set; } = name => name.StartsWith("Sports_", StringComparison.Ordinal);
    public const int HUsersPerHuman = 1, AUsersPerHuman = 1;

    // The A-User of a human, if it has deployed one.
    public string? AUserOf(string human) { lock (gate) return processes.Values.FirstOrDefault(p => p.Kind == "User" && p.HumanID == human && p.Autonomous)?.ID; }
    public bool IsAUser(string user) { lock (gate) return processes.GetValueOrDefault(user) is { Kind: "User", Autonomous: true }; }
    public string? HumanOf(string user) { lock (gate) return processes.GetValueOrDefault(user)?.HumanID; }
    // The Users whose Account has a Level (the Rights of the Level are theirs: MeetingServices.LevelRightsAsync).
    public IReadOnlyList<string> UsersWithLevel() { lock (gate) return processes.Values.Where(p => p.Kind == "User" && accountLevels.ContainsKey(p.ID)).Select(p => p.ID).ToList(); }
    // The A-Users of the M-Instance that have a Persona, each with its Persona (their agents run them: AUserAgents).
    public IReadOnlyList<(string User, string Persona)> AUsers()
    {
        lock (gate) return processes.Values.Where(p => p is { Kind: "User", Autonomous: true })
            .Select(p => (User: p.ID, Persona: items.Values.FirstOrDefault(i => i.Holder == p.ID && i.DataType == "OSD-3DO-V1.5")?.ID))
            .Where(x => x.Persona is not null).Select(x => (x.User, x.Persona!)).ToList();
    }

    // The Users a Personal Profile asks for, each with the Persona chosen for it (null: none chosen): whether the human may have them - one H-User and one A-User at most, each
    // with a Persona of its kind. null when it may, else why not.
    private string? UsersAllowed(string human, IEnumerable<(string User, string? Choice)> wanted, string? level)
    {
        int h = processes.Values.Count(p => p.Kind == "User" && p.HumanID == human && !p.Autonomous), a = processes.Values.Count(p => p.Kind == "User" && p.HumanID == human && p.Autonomous);
        var visitor = level == "Visitor";
        foreach (var (u, choice) in wanted)
        {
            var existing = processes.GetValueOrDefault(u);
            var autonomous = existing?.Autonomous ?? (choice is not null && ForAUsers(choice));
            if (visitor && autonomous) return "a Visitor has an H-User only: an A-User needs a Participant or Officer Account";
            if (choice is not null && visitor != ForVisitors(choice) && !autonomous)
                return visitor ? $"a Visitor's Persona is one of the Visitors' (sports) Personae, not {choice}" : $"{choice} is a Persona for a Visitor: the H-User {u} chooses another";
            if (choice is not null && ForAUsers(choice) != autonomous)
                return autonomous ? $"the Persona of {u}, an A-User, is one of the A-Users' Personae" : $"{choice} is a Persona for an A-User: the H-User {u} chooses another";
            if (existing is not null) continue;
            if (autonomous ? ++a > AUsersPerHuman : ++h > HUsersPerHuman)
                return autonomous ? $"{human} has an A-User already" : $"{human} has an H-User already";
        }
        return null;
    }

    public sealed record Activity(long Time, string Source, string Action, string Destination, int Http, string Status);

    private readonly object gate = new();
    private readonly Schemas schemas;
    private readonly Dictionary<string, Item> items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Process> processes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> holdings = new(StringComparer.Ordinal);   // Process -> Rights IDs
    private readonly Dictionary<string, List<string>> inboxes = new(StringComparer.Ordinal);    // Process -> Message IDs
    private readonly List<Activity> activity = [];

    public string ID { get; }
    public string EnvironmentID { get; }

    public static readonly IReadOnlyDictionary<string, string> ServiceOf = new Dictionary<string, string>
    {
        ["Register"] = "RGSrvc", ["Transact"] = "TRSrvc", ["MM-Add"] = "LCSrvc", ["MM-Move"] = "LCSrvc",
        ["MM-Animate"] = "LCSrvc", ["Property Change"] = "LCSrvc", ["MM-Send"] = "COSrvc",
        ["Rights Change"] = "RTSrvc", ["Identify"] = "IDSrvc", ["UM-Capture"] = "EISrvc", ["MU-Actuate"] = "EISrvc",
        ["MM-Capture"] = "LCSrvc"
    };

    public MInstance(string schemasRoot, string id = "MI1", string environmentId = "ME1")
    {
        schemas = new Schemas(schemasRoot);
        ID = id;
        EnvironmentID = environmentId;
        foreach (var s in ServiceOf.Values.Distinct().Append("PRSrvc"))
            processes[s] = new Process { ID = s, Kind = "Service" };
    }

    public IEnumerable<string> Performed => ServiceOf.Keys;

    // What the viewer says about a demonstration in progress: the step just performed, and
    // whether the next one waits for the presenter (Manual). Not part of the M-Instance.
    public volatile string? Caption;
    public volatile string? Note;     // one line under the caption: what the step means
    public volatile string? Say;      // what the viewer's voice reads for the step (the caption and note when absent)
    public volatile string[]? Hidden; // Items the viewer does not draw yet (a demonstration brings them in)
    public volatile bool Manual;
    public Schemas Schemas => schemas;

    // ---------------------------------------------------------------- the state an M-Instance starts with
    // An M-Location (in the M-Environment, drawn by the viewer) or, universe: true, a
    // U-Location (in the Universe, where Data are captured and R-Items rendered).
    public void AddLocation(string id, string? holder = null, bool isPublic = false, double x = 0, double y = 0, double size = 4,
                            bool perceptible = true, bool universe = false, string? at = null)   // at: the M-Location it is placed in (a Room on its Parcel, a Seat in its Room); x, y then are metres within it
    {
        lock (gate) items[id] = new Item
        {
            ID = id, DataType = universe ? "MMM-ULC" : "MMM-LOC", Holder = holder, Public = isPublic, Place = [x, y], Size = size,
            Perceptible = perceptible, Location = at
        };
    }

    // An Item placed at an M-Location when the M-Instance starts (a door of a room), its Process's own, with its Item Properties (MMM-IPR-V3.0).
    public void AddItem(string id, string dataType, string holder, string at, double x, double y, double yaw = 0, string? kind = null, params (string Name, JsonNode Value)[] properties)
    {
        lock (gate)
        {
            var it = new Item
            {
                ID = id, DataType = dataType, Holder = holder, Location = at, Kind = kind,
                SpatialAttitude = new JsonObject { ["Position"] = new JsonObject { ["CartPosition"] = new JsonArray(x, y, 0.0) }, ["Orientation"] = new JsonObject { ["Orientation"] = new JsonArray(yaw, 0.0, 0.0) } }
            };
            foreach (var (n, v) in properties) it.Properties[n] = v;
            items[id] = it;
        }
    }

    // A Service Process (a Process of the M-Instance that is not a human's User: a meeting service, a machine that takes part), with a token, and
    // the Persona it manifests itself with, if any.
    public string AddService(string id, string? personaId = null, string? model = null)
    {
        lock (gate)
        {
            var proc = new Process { ID = id, Kind = "Service", Token = NewToken() };
            processes[id] = proc;
            if (personaId is not null)
                items[personaId] = new Item { ID = personaId, DataType = "OSD-3DO-V1.5", Holder = id, Model = model };
            return proc.Token!;
        }
    }

    // The Messages in the inbox of a Process, with who sent each (the Process that owns the Message Item).
    public IReadOnlyList<(string ID, string From)> InboxWithSenders(string process)
    {
        lock (gate) return (inboxes.GetValueOrDefault(process) ?? []).Select(i => (i, items.TryGetValue(i, out var it) ? it.Holder ?? "" : "")).ToList();
    }

    // The 3D Model that renders an Item - a Persona's, once it exists.
    private readonly Dictionary<string, string> models = new(StringComparer.Ordinal);
    // The 3D Models (GLB files) given in turn to the Personae of the Users that register without one of their own.
    public IReadOnlyList<string> DefaultModels { get; set; } = [];
    // The catalogue of Personae the M-Instance offers a registering human (Rocketbox avatars, AllApps/MMM/Viewer/personae/catalogue): the human chooses one, it does not bring its own.
    // A choice is the name of an entry (PersonaChoice in the Personal Profile); one that is not in the catalogue is refused (422).
    public IReadOnlySet<string> Catalogue { get; set; } = new HashSet<string>(StringComparer.Ordinal);
    // What the M-Instance shows a human that is about to register: the entries of the catalogue (Name, Group, Gender). It is the M-Instance's to offer; the human is not yet in it.
    public JsonArray CatalogueEntries { get; set; } = [];

    // A Persona of the catalogue is one human's: once chosen it is no longer offered, and a second choice of it is refused (409 Clash). It is the M-Instance's decision to offer
    // each Persona once, so that two people do not meet as the same avatar.
    public bool PersonaTaken(string name)
    {
        var model = CatalogueModel(name);
        lock (gate) return items.Values.Any(i => i.DataType == "OSD-3DO-V1.5" && i.Model == model);
    }

    // The entries of the catalogue that nobody has chosen.
    public JsonArray AvailableCatalogueEntries()
    {
        lock (gate) return new JsonArray(CatalogueEntries.Where(e => e?["Name"]?.GetValue<string>() is { } n && !items.Values.Any(i => i.DataType == "OSD-3DO-V1.5" && i.Model == CatalogueModel(n)))
            .Select(e => { var c = e!.DeepClone().AsObject(); c["AUser"] = ForAUsers((string)e["Name"]!); c["Visitor"] = ForVisitors((string)e["Name"]!); return (JsonNode)c; }).ToArray());
    }
    // THE ACCOUNT LEVELS the Register Service offers (MMM-TEC Register, step 2): what a human is shown when it selects Registration. Both are free: there is no Service Pricing Model to pay.
    // Visitor: the public places of the MPAI building. Participant: the meetings the chair invited it to (the MPAI Building Service gives the Rights to those on the chair's list that hold a Participant Account).
    public IReadOnlyList<(string Id, string Name, string Description)> AccountLevels { get; set; } =
    [
        ("Visitor", "Visitor", "The public places of the MPAI building: the atrium, where anybody may come in, look, talk and ask. A visit: you are forgotten when you leave, and register again next time. Free."),
        ("Participant", "Participant", "Meetings you have been invited to: the Rights to the meeting rooms, for the meeting, if you are on the chair's list. Free."),
        ("Officer", "Officer", "All of MPAI: the Rights to every place of the building - the members' rooms, the offices of MPAI, all the meeting rooms. Your request goes to the MMM Manager; meanwhile you are a Participant. Free.")
    ];
    public string? AccountLevelOf(string user) { lock (gate) return accountLevels.GetValueOrDefault(user); }
    private readonly Dictionary<string, string> accountLevels = new(StringComparer.Ordinal);
    public static string CatalogueModel(string name) => $"catalogue/{name}.glb";
    private static string NameOfModel(string model) => System.IO.Path.GetFileNameWithoutExtension(model);

    // The Users a Personal Profile names, each with the catalogue Persona chosen for it (the first PersonaChoice of its entry), or null.
    private static List<(string User, string? Choice)> WantedUsers(JsonObject profile)
    {
        var wanted = new List<(string, string?)>();
        foreach (var p in profile["PersonalProfile"]?["Processes"]?.AsArray() ?? [])
        {
            var choice = (p?["PersonaIDOrPersona"]?.AsArray() ?? []).Select(e => e?["PersonaChoice"]?.GetValue<string>()).FirstOrDefault(c => c is not null);
            foreach (var u in Ids(p?["ProcessIDOrProcess"], "ProcessID")) wanted.Add((u, choice));
        }
        return wanted;
    }
    private int registered;
    public void SetModel(string itemId, string model)
    {
        lock (gate) { models[itemId] = model; if (items.TryGetValue(itemId, out var it)) it.Model = model; }
    }

    public void AddUser(string id, string humanId, IEnumerable<string>? owns = null)
    {
        lock (gate)
        {
            processes[id] = new Process { ID = id, Kind = "User", HumanID = humanId, Token = NewToken() };
            foreach (var i in owns ?? []) if (items.TryGetValue(i, out var it)) it.Holder = id;
        }
    }

    // A User that comes back (its browser was closed, refreshed): it is the same person - the transport says so - so its Process is given a new token, and told where its Persona
    // stands. The old token is no longer valid. null: no such User.
    public (string Token, JsonObject Info)? Resume(string user)
    {
        lock (gate)
        {
            if (!processes.TryGetValue(user, out var p) || p.Kind != "User") return null;
            p.Token = NewToken();
            var persona = items.Values.FirstOrDefault(i => i.Holder == user && i.DataType == "OSD-3DO-V1.5");
            var info = new JsonObject { ["User"] = user };
            if (persona is not null)
            {
                info["Persona"] = persona.ID; info["Model"] = persona.Model; info["Location"] = persona.Location;
                if (persona.SpatialAttitude?["Position"]?["CartPosition"] is JsonArray a && a.Count >= 2) { info["X"] = a[0]?.GetValue<double>(); info["Y"] = a[1]?.GetValue<double>(); }
                info["Yaw"] = YawOf(persona);
            }
            return (p.Token!, info);
        }
    }

    public string? TokenOf(string process) { lock (gate) return processes.GetValueOrDefault(process)?.Token; }

    public string? ProcessOf(string token)
    {
        lock (gate) return processes.Values.FirstOrDefault(p => p.Token is not null && CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(p.Token), System.Text.Encoding.UTF8.GetBytes(token)))?.ID;
    }

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    // ---------------------------------------------------------------- reading
    public Item? Get(string id) { lock (gate) return items.GetValueOrDefault(id); }

    // An Item that a walk goes round (a table, a piece of furniture): its footprint, width east-west and depth north-south, metres, centred on the Item.
    public void SetFootprint(string id, double width, double depth) { lock (gate) if (items.TryGetValue(id, out var it)) it.Footprint = [width, depth]; }

    // An Item taken away by whoever runs the M-Instance (not a Process Action): a demonstration that starts its scene again removes what the last scene added.
    public bool Remove(string id) { lock (gate) return items.Remove(id); }

    public IReadOnlyList<Activity> ActivityData { get { lock (gate) return activity.ToList(); } }

    public IReadOnlyList<string> Inbox(string process) { lock (gate) return inboxes.GetValueOrDefault(process)?.ToList() ?? []; }

    public IReadOnlyList<JsonObject> RightsOf(string process)
    {
        lock (gate) return (holdings.GetValueOrDefault(process) ?? []).Select(r => items[r].Body!).ToList();
    }

    public IReadOnlyList<string> HoldersOf(string itemId)
    {
        lock (gate) return holdings.Where(h => h.Value.Any(r => Targets(items[r].Body!).Contains(itemId))).Select(h => h.Key).ToList();
    }

    public IReadOnlyList<(string ID, string DataType)> List(string? dataType = null)
    {
        lock (gate) return items.Values.Where(i => dataType is null || i.DataType.Contains(dataType, StringComparison.Ordinal))
                                       .Select(i => (i.ID, i.DataType)).OrderBy(i => i.ID, StringComparer.Ordinal).ToList();
    }

    // May this Process read this Item: the Process holding the Rights to it, a Public Location, a Process holding a
    // Right on it, the recipient of a Message.
    public bool MayRead(string process, string itemId)
    {
        lock (gate)
        {
            if (!items.TryGetValue(itemId, out var it)) return false;
            return it.Holder == process || it.Public || HeardBy(process, it) || SeenBy(process, it) || (inboxes.GetValueOrDefault(process)?.Contains(itemId) ?? false)
                   || (holdings.GetValueOrDefault(process) ?? []).Any(r => r == itemId)
                   // ...a Right that allows something on it: not one that withdraws a Right (May Not), and not one whose period is over
                   || Live(process).Any(b => Entries(b).Where(e => e.Verb != "May Not").SelectMany(e => e.PAs).Select(Template).OfType<PA>()
                                             .SelectMany(t => new[] { t.Target, t.Nil }).Contains(itemId));
        }
    }

    // ---------------------------------------------------------------- Speech Objects that are streams
    // The Speech Object a Process may feed: its own, still a stream. A streamed Speech Object has no duration: its Time is not in it until the stream is over.
    public bool StreamToFeed(string process, string id)
    {
        lock (gate) return items.TryGetValue(id, out var it) && it.DataType == "OSD-BSO-V1.5" && it.Holder == process && it.Streaming;
    }

    // The stream is over: what arrived is the Speech Object's data, and it is no longer a stream.
    public void StreamComplete(string id, byte[] pcm)
    {
        lock (gate)
        {
            if (!items.TryGetValue(id, out var it) || it.Body?["BasicSpeechObjectData"]?[0] is not JsonObject data) return;
            data["Data"] = Convert.ToBase64String(pcm);
            // The duration of the speech is known now, and it is the Speech Object's own Time: from when its Data was captured to the end of the stream.
            if (it.Body["BasicSpeechObjectTime"] is null)
                it.Body["BasicSpeechObjectTime"] = new JsonObject
                {
                    ["Header"] = "OSD-STM-V1.5", ["SimpleTimeID"] = id + "-T",
                    ["SimpleTimeData"] = new JsonArray(new JsonObject { ["FlagsByte"] = 3, ["StartTime"] = it.Started, ["EndTime"] = Math.Max(it.Started, it.Started + pcm.Length / 32), ["AccuracyMode"] = "single", ["AccuracyPlusMinus"] = 1 })
                };
            it.Streaming = false;
        }
    }

    // The data of a Speech Object that is complete, or null.
    public byte[]? SpeechData(string id)
    {
        lock (gate)
            return items.TryGetValue(id, out var it) && it.DataType == "OSD-BSO-V1.5" && !it.Streaming
                   && it.Body?["BasicSpeechObjectData"]?[0]?["Data"]?.GetValue<string>() is { } d ? Convert.FromBase64String(d) : null;
    }

    public bool IsStream(string id) { lock (gate) return items.TryGetValue(id, out var it) && it.DataType == "OSD-BSO-V1.5" && it.Streaming; }

    // ---------------------------------------------------------------- Animation Streams that arrive as they are captured
    // The Animation Stream a Process may feed: its own, still a stream (its Animation Qualifier names a stream format).
    public bool AnimationToFeed(string process, string id)
    {
        lock (gate) return items.TryGetValue(id, out var it) && it.DataType == "PAF-AMO-V1.6" && it.Holder == process && it.Streaming;
    }

    // An Animation Stream is seen by whoever has an Item in the Room around the seat of the Persona it animates, unless a Right bars it from the places in it (May Not).
    public bool AnimationSeenBy(string process, string id)
    {
        lock (gate)
        {
            if (!items.TryGetValue(id, out var stream) || !stream.Streaming) return false;
            var persona = items.Values.FirstOrDefault(i => i.Animation == id);
            if (persona is null) return false;
            if (persona.Holder == process) return true;
            if (persona.Location is null || !items.TryGetValue(persona.Location, out var seat) || seat.Location is null) return false;
            var room = seat.Location;
            bool InRoom(string? l) { for (var d = 0; l is not null && d < 8; d++, l = items.GetValueOrDefault(l)?.Location) if (l == room) return true; return false; }
            if (!items.Values.Any(i => i.Holder == process && InRoom(i.Location))) return false;
            return !Live(process).SelectMany(Entries).Where(e => e.Verb == "May Not")
                             .SelectMany(e => e.PAs).Select(Template).OfType<PA>().Any(t => t.Target is not null && InRoom(t.Target));
        }
    }

    // The stream is over: it no longer animates anything, and it is gone.
    public void AnimationEnded(string id)
    {
        lock (gate)
        {
            foreach (var i in items.Values.Where(i => i.Animation == id)) i.Animation = null;
            items.Remove(id);
        }
    }

    // ---------------------------------------------------------------- the screen shared in a room
    // A SHARED SCREEN is a Visual Object (OSD-BVO) that is a stream: its User - the one with the Right to place it in the room - declares it with the address its frames are delivered
    // from, and feeds it as it captures (a WebSocket, /ws/v2.2/screen/{id}); whoever has an Item in the room sees it, as it goes on. It has no data kept: when the stream is over the Item is gone.
    public const string ScreenHeader = "OSD-BVO-V1.5";
    public long? ScreenToFeed(string process, string id)
    {
        lock (gate) return items.TryGetValue(id, out var it) && it.DataType == ScreenHeader && it.Holder == process && it.Streaming ? 1 : null;
    }
    public void ScreenEnded(string id) { lock (gate) items.Remove(id); }
    public void StopScreens() { lock (gate) foreach (var id in items.Values.Where(i => i.DataType == ScreenHeader && i.Streaming).Select(i => i.ID).ToList()) items.Remove(id); }
    public bool ScreenLive(string id) { lock (gate) return items.TryGetValue(id, out var it) && it.DataType == ScreenHeader && it.Streaming; }

    // A shared screen is seen by whoever has an Item in the room it was placed at, unless a Right bars it from the places in it (May Not: it was taken out of the meeting).
    private bool SeenBy(string process, Item screen)
    {
        if (screen.DataType != ScreenHeader || screen.Location is null) return false;
        var room = screen.Location;
        bool InRoom(string? l) { for (var d = 0; l is not null && d < 8; d++, l = items.GetValueOrDefault(l)?.Location) if (l == room) return true; return false; }
        if (!items.Values.Any(i => i.Holder == process && i.DataType != ScreenHeader && InRoom(i.Location))) return false;
        return !Live(process).SelectMany(Entries).Where(e => e.Verb == "May Not")
                         .SelectMany(e => e.PAs).Select(Template).OfType<PA>().Any(t => t.Target is not null && InRoom(t.Target));
    }

    // A Speech Object is heard by whoever has an Item in the Room around the seat it was placed at - its User's Persona seated in it - unless a Right bars it from the
    // places in it (May Not: it was taken out of the meeting).
    private bool HeardBy(string process, Item speech)
    {
        if (speech.DataType != "OSD-BSO-V1.5" || speech.Location is null || !items.TryGetValue(speech.Location, out var seat) || seat.Location is null) return false;
        var room = seat.Location;
        bool InRoom(string? l) { for (var d = 0; l is not null && d < 8; d++, l = items.GetValueOrDefault(l)?.Location) if (l == room) return true; return false; }
        if (!items.Values.Any(i => i.Holder == process && InRoom(i.Location))) return false;
        return !Live(process).SelectMany(Entries).Where(e => e.Verb == "May Not")
                         .SelectMany(e => e.PAs).Select(Template).OfType<PA>().Any(t => t.Target is not null && InRoom(t.Target));
    }

    public JsonObject AsAnyItem(Item it) => new()
    {
        ["ItemName"] = it.ID,
        ["ItemValueOrRef"] = new JsonArray(it.Body?.DeepClone() ?? new JsonObject
        {
            ["ItemID"] = it.ID, ["DataType"] = it.DataType, ["Holder"] = it.Holder, ["Location"] = it.Location,
            ["Perceptible"] = it.Perceptible, ["Animation"] = it.Animation
        }),
        ["MInstanceID"] = ID, ["MEnvironmentID"] = EnvironmentID
    };

    // ---------------------------------------------------------------- what can be seen
    // THE M-ENVIRONMENT AS IT CAN BE SEEN: every Location and every Item that is at
    // one, where it is in metres - a Location at another (a Room on its Parcel) placed
    // within it - and whether it is perceptible. What the viewer draws; what the
    // tests compare.
    public JsonObject Snapshot()
    {
        lock (gate)
        {
            // Where an Item stands within its Location when its Spatial Attitude says so
            // (x east and y north of its Position, in metres - the axes of space: x east, y north, z up); an Item without, stands at the Location's centre.
            double[]? Spot(Item it) =>
                it.SpatialAttitude?["Position"]?["CartPosition"] is JsonArray a && a.Count >= 3
                && a[0] is not null && a[1] is not null && (a[0]!.GetValue<double>() != 0 || a[1]!.GetValue<double>() != 0)
                    ? [a[0]!.GetValue<double>(), a[1]!.GetValue<double>()] : null;
            double[] Where(Item it, int depth = 0)
            {
                var own = it.Place ?? Spot(it) ?? [0, 0];
                if (depth < 8 && it.Location is { } l && items.TryGetValue(l, out var parent))
                {
                    var p = Where(parent, depth + 1);
                    return [p[0] + own[0], p[1] + own[1]];
                }
                return own;
            }
            var located = items.Values.Where(i => i.DataType == "MMM-LOC" ||
                                                  (i.Location is not null && items.TryGetValue(i.Location, out var at) && at.DataType == "MMM-LOC"))
                                      .OrderBy(i => i.ID, StringComparer.Ordinal).ToList();
            // Items at the same Location stand side by side.
            var side = located.Where(i => i.DataType != "MMM-LOC" && i.DataType != "OSD-BSO-V1.5" && i.DataType != ScreenHeader).GroupBy(i => i.Location!)
                              .SelectMany(g => g.Select((it, k) => (it.ID, Offset: (k - (g.Count() - 1) / 2.0) * 0.9)))
                              .ToDictionary(x => x.ID, x => x.Offset);
            return new JsonObject
            {
                ["MInstanceID"] = ID,
                ["Recent"] = new JsonArray(activity.TakeLast(6).Select(a => (JsonNode)new JsonObject
                {
                    ["Source"] = a.Source, ["Action"] = a.Action, ["Destination"] = a.Destination, ["Http"] = a.Http, ["Status"] = a.Status
                }).ToArray()),
                ["Performed"] = activity.Count,
                ["Caption"] = Caption, ["Note"] = Note, ["Say"] = Say, ["Manual"] = Manual,
                ["Hidden"] = new JsonArray((Hidden ?? []).Select(h => (JsonNode)h).ToArray()),
                ["Things"] = new JsonArray(located.Select(i =>
                {
                    var w = Where(i);
                    var clock = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    double? walkingYaw = null;
                    if (i.WalkFrom is { } wf && i.WalkTo is { } wt && clock < i.WalkEnd)
                    {
                        var f = Math.Clamp((clock - i.WalkStart) / (double)Math.Max(1, i.WalkEnd - i.WalkStart), 0, 1);
                        if (i.WalkPath is { } path) (w, walkingYaw) = AlongPath(path, f);   // round an obstacle: along the way, facing where it goes
                        else w = [wf[0] + (wt[0] - wf[0]) * f, wf[1] + (wt[1] - wf[1]) * f];
                    }
                    var o = new JsonObject
                    {
                        ["ID"] = i.ID, ["Kind"] = i.Kind ?? (i.DataType == "MMM-LOC" ? "Location" : i.DataType == "OSD-3DO-V1.5" ? "Persona" : i.DataType == "OSD-BSO-V1.5" ? "Speech" : i.DataType == ScreenHeader ? "Screen" : i.DataType == "OSD-AUO-V1.5" ? "Audio" : "Item"),
                        ["At"] = i.Location, ["X"] = w[0] + (Spot(i) is null ? side.GetValueOrDefault(i.ID) : 0), ["Y"] = w[1], ["Z"] = 0.0,    // east, north, up: metres
                        ["Yaw"] = walkingYaw ?? YawOf(i), ["Depth"] = DepthOf(i),
                        ["Perceptible"] = i.Perceptible, ["Holder"] = i.Holder, ["Public"] = i.Public
                    };
                    if (i.DataType == "MMM-LOC") o["Size"] = i.Size;
                    if (i.Model is not null) o["Model"] = i.Model;
                    if (i.Footprint is { } fp) o["Footprint"] = new JsonArray(fp[0], fp[1]);   // an obstacle: width east-west, depth north-south
                    if (i.Properties.Count > 0) o["Properties"] = new JsonObject(i.Properties.Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone())));
                    if (i.Animation is not null) o["Animation"] = i.Animation;
                    if (i.Animation is not null) o["Animating"] = clock < i.AnimatedUntil;   // its Stream is still playing (walking, speaking)
                    if (i.WalkEnd > 0 && clock < i.WalkEnd) o["Walking"] = true;
                    if (i.LightSource?["Intensity"] is JsonValue intensity) o["Intensity"] = intensity.DeepClone();    // a light: how strong, in Watt
                    if (i.DataType == ScreenHeader) o["Streaming"] = i.Streaming;
                    if (i.DataType == "OSD-AUO-V1.5") o["Start"] = i.Started;                            // a sound of the place: when it was added (Unix milliseconds)
                    // A Speech Object says when it is heard: its own time (Unix milliseconds). Its data is read as an Item, once, by whoever may hear it.
                    if (i.DataType == "OSD-BSO-V1.5")
                    {
                        var st = i.Body?["BasicSpeechObjectTime"]?["SimpleTimeData"]?[0];
                        o["Start"] = st?["StartTime"]?.DeepClone() ?? JsonValue.Create(i.Started); o["End"] = st?["EndTime"]?.DeepClone(); o["Streaming"] = i.Streaming;    // a stream has no end yet
                    }
                    return (JsonNode)o;
                }).ToArray())
            };
        }
    }

    // The snapshot in one line: where each Persona is, which Locations are perceptible.
    public string View()
    {
        lock (gate)
        {
            var personae = items.Values.Where(i => i.DataType == "OSD-3DO-V1.5").OrderBy(i => i.ID, StringComparer.Ordinal)
                                .Select(i => $"{i.ID}@{i.Location ?? "-"}{(i.Animation is null ? "" : "~")}");
            var places = items.Values.Where(i => i.DataType == "MMM-LOC" && i.Location is not null).OrderBy(i => i.ID, StringComparer.Ordinal)
                              .Select(i => $"{i.ID}@{i.Location}{(i.Perceptible ? "" : "(not perceptible)")}");
            return string.Join(", ", personae.Concat(places));
        }
    }

    // ---------------------------------------------------------------- performing a Process Action
    public Outcome Perform(string? caller, ProcessActionRequest rq)
    {
        lock (gate)
        {
            var outcome = PerformLocked(caller, rq);
            if (rq.Action != "MM-Capture")     // looking changes nothing: it is not the record of what happened
                activity.Add(new Activity(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), rq.SourceProcessID, rq.Action,
                                          outcome.Response.DestinationProcessID, outcome.Http, outcome.Response.PaStatus.Code));
            return outcome;
        }
    }

    private Outcome PerformLocked(string? caller, ProcessActionRequest rq)
    {
        if (!ServiceOf.TryGetValue(rq.Action, out var service))
            return Fail(422, "", "FaultyPA", $"{rq.Action} is not performed by this M-Instance");
        if (rq.Action != "Register")
        {
            if (caller is null) return Fail(403, service, "InsRights", "no Process: the bearer token is missing or unknown");
            if (caller != rq.SourceProcessID) return Fail(403, service, "InsRights", $"the token is {caller}'s, the Request {rq.SourceProcessID}'s");
        }
        if (rq.DeonticVerb != "May") return Fail(422, service, "FaultyPA", "a Process requests what it May do");

        var c = rq.Complements;
        var with = new List<JsonObject>();     // the Items the Request carries, at Status=Model
        var refs = new List<string>();         // the Items it names
        foreach (var w in c.With ?? [])
        {
            switch (w)
            {
                case JsonValue v when v.TryGetValue<string>(out var id): refs.Add(id); break;
                case JsonObject o when o["itemID"] is not null && o["Header"] is null:   // a Statused Reference; an Item has a Header (an Item Property names its Item with ItemID, and the names are read without case)
                    var rid = o["itemID"]!.GetValue<string>();
                    if (!items.ContainsKey(rid)) return Fail(404, service, "IncID", $"no Item {rid}");
                    refs.Add(rid); break;
                case JsonObject o:
                    var item = o["ItemValueOrRef"] is JsonArray a && a.Count == 1 && a[0] is JsonObject inner ? inner : o;
                    if (schemas.Check(item) is { } fault) return Fail(422, service, "FaultyPA", fault);
                    with.Add((JsonObject)item.DeepClone()); break;
                default: return Fail(422, service, "FaultyPA", "a With Complement is an identifier, a Statused Reference or an Item");
            }
        }

        return rq.Action switch
        {
            "Register" => Register(service, with, caller),
            "Transact" => Transact(service, rq.SourceProcessID, with),
            "MM-Add" => MMAdd(service, rq.SourceProcessID, c, with),
            "UM-Capture" => UMCapture(service, rq.SourceProcessID, c, refs, with),
            "Identify" => Identify(service, rq.SourceProcessID, c, refs, with),
            "MM-Animate" => MMAnimate(service, rq.SourceProcessID, c, refs, with),
            "MM-Send" => MMSend(service, rq.SourceProcessID, c, with),
            "MM-Move" => MMMove(service, rq.SourceProcessID, c, with),
            "MM-Capture" => MMCapture(service, rq.SourceProcessID, c, with),
            "Property Change" => PropertyChange(service, rq.SourceProcessID, c, with),
            "MU-Actuate" => MUActuate(service, rq.SourceProcessID, c, refs, with),
            "Rights Change" => RightsChange(service, rq.SourceProcessID, c, with),
            _ => Fail(422, service, "FaultyPA", rq.Action)
        };
    }

    // ---------------------------------------------------------------- the Services
    // RGSrvc. The Personal Profile lists the User Processes the human asks for and
    // their Personae: an Account is opened, the Users created, the Personae theirs.
    private Outcome Register(string service, List<JsonObject> with, string? caller = null)
    {
        var profile = with.FirstOrDefault(i => Header(i) == "MMM-PPR-V2.2");
        var human = profile?["humanID"]?.GetValue<string>();
        if (profile is null || human is null) return Fail(422, service, "FaultyPA", "Register takes a Personal Profile with its humanID");
        var secret = profile["PersonalProfile"]?["Credential"]?.GetValue<string>();
        var deviceKey = profile["PersonalProfile"]?["DeviceKey"]?.GetValue<string>();      // "<device id>:<secret>": a device linked to the Account (2026-11-26)
        // A human that has an Account ENTERS with its credentials, with the key of a device linked to it, or as one of its Users (holding its token): its Users are given new tokens.
        if (processes.ContainsKey(human))
        {
            if (secret is null && deviceKey is null && (caller is null || processes.GetValueOrDefault(caller)?.HumanID != human)) return Fail(409, service, "Clash", $"{human} is registered already");
            if (deviceKey is not null && secret is null && !VerifiesDevice(human, deviceKey)) return Fail(403, service, "InsRights", "this device is not linked to the Account (or its link was removed)");
            return Enter(service, human, secret, profile);
        }
        if (RequireCredentials && (secret is not null || deviceKey is not null) && profile["PersonalProfile"]?["Data"] is null) return Fail(404, service, "IncID", $"there is no Account of {human}");   // entering an Account that is not there
        if (RequireCredentials)
        {
            var d = profile["PersonalProfile"]?["Data"]?[0];
            string? Of(string key) => d?[key]?.GetValue<string>() is { } v && v.Trim().Length > 0 ? v.Trim() : null;
            // THE ROLE AT THE FIRST ENTRY (the author, 2026-11-27): an Account that Thalia opens has no Account Level yet - the person chooses it when it first enters - and has all of
            // first name, last name, organisation and email (all four are required in Thalia).
            var lvl = profile["PersonalProfile"]?["AccountLevel"]?.GetValue<string>();
            if (Of("FirstName") is null || Of("LastName") is null) return Fail(422, service, "FaultyPA", "the first name and the last name are needed");
            if (secret is null || secret.Length < MinimumPassword) return Fail(422, service, "FaultyPA", $"a password of at least {MinimumPassword} characters is needed");
            if (lvl != "Visitor" && (Of("Email") is null || Of("Organisation") is null))
                return Fail(422, service, "FaultyPA", lvl is null ? "the first name, the last name, the organisation and the email are needed" : $"the {lvl} Account Level needs an email and an organisation too");
        }

        var level = profile["PersonalProfile"]?["AccountLevel"]?.GetValue<string>();
        if (level is not null && !AccountLevels.Any(l => l.Id == level)) return Fail(422, service, "FaultyPA", $"{level} is not an Account Level of this M-Instance");
        foreach (var p in profile["PersonalProfile"]?["Processes"]?.AsArray() ?? [])
            foreach (var e in p?["PersonaIDOrPersona"]?.AsArray() ?? [])
            {
                if (e?["PersonaChoice"]?.GetValue<string>() is not { } pick) continue;
                if (!Catalogue.Contains(pick)) return Fail(422, service, "FaultyPA", $"{pick} is not in the catalogue of Personae: a Persona is chosen from it, not brought");
                if (PersonaTaken(pick)) return Fail(409, service, "Clash", $"{pick} was chosen by another human");
            }
        // the level the Users will have: the Manager's, whatever was ticked, for the Account with the Manager's email
        var asked = IsManagerEmail(profile["PersonalProfile"]?["Data"]?[0]?["Email"]?.GetValue<string>()) ? Manager
                  : profile["PersonalProfile"]?["AccountLevel"]?.GetValue<string>();
        if (UsersAllowed(human, WantedUsers(profile), asked) is { } notAllowed) return Fail(422, service, "FaultyPA", notAllowed);

        processes[human] = new Process { ID = human, Kind = "Human" };
        var account = $"Account-{human}";
        var users = new List<string>();
        var createdItems = new List<string>();                  // the Personae of the Account: its Items
        var tokens = new Dictionary<string, string>();
        foreach (var p in profile["PersonalProfile"]?["Processes"]?.AsArray() ?? [])
        {
            var ids = Ids(p?["ProcessIDOrProcess"], "ProcessID");
            var personae = Ids(p?["PersonaIDOrPersona"], "PersonaID");
            var chosen = new Dictionary<string, string>();       // PersonaID -> the entry of the catalogue the human chose for it
            foreach (var e in p?["PersonaIDOrPersona"]?.AsArray() ?? [])
                if (e?["PersonaChoice"]?.GetValue<string>() is { } choice && e["PersonaID"]?.GetValue<string>() is { } cid)
                {
                    chosen[cid] = CatalogueModel(choice);
                }
            foreach (var u in ids)
            {
                if (processes.ContainsKey(u)) return Fail(409, service, "Clash", $"Process {u} exists already");
                var proc = new Process { ID = u, Kind = "User", HumanID = human, Token = NewToken(), Autonomous = chosen.Values.Any(m => ForAUsers(NameOfModel(m))) };
                processes[u] = proc; tokens[u] = proc.Token!; users.Add(u);
                if (level is not null) accountLevels[u] = level;
                foreach (var pid in personae)
                {
                    if (RequireCredentials && !chosen.ContainsKey(pid)) continue;      // with Accounts, a Persona is one the human chose (else it chooses when it enters)
                    createdItems.Add(pid);
                    items[pid] = new Item { ID = pid, DataType = "OSD-3DO-V1.5", Holder = u, Model = chosen.GetValueOrDefault(pid) ?? models.GetValueOrDefault(pid) ?? (DefaultModels.Count > 0 ? DefaultModels[registered++ % DefaultModels.Count] : null) };
                }
            }
        }
        // THE MANAGER (2026-11-26): the Account whose email is the Manager's is the Manager's, whatever level was ticked.
        if (IsManagerEmail(profile["PersonalProfile"]?["Data"]?[0]?["Email"]?.GetValue<string>()))
        {
            foreach (var u in users) accountLevels[u] = Manager;
            if (items.GetValueOrDefault(account) is { Body: { } ab }) ab["AccountLevel"] = Manager;
            level = Manager;
        }
        // The credentials are kept as a salted hash, never as given; the Personal Profile kept as an Item does not carry them.
        if (secret is not null) credentials[human] = HashOf(secret);
        var keptProfile = (JsonObject)profile.DeepClone();
        (keptProfile["PersonalProfile"] as JsonObject)?.Remove("Credential");
        items[profile["PersonalProfileID"]?.GetValue<string>() ?? $"Profile-{human}"] =
            new Item { ID = profile["PersonalProfileID"]?.GetValue<string>() ?? $"Profile-{human}", DataType = "MMM-PPR-V2.2", Body = keptProfile, Holder = human };
        items[account] = new Item
        {
            ID = account, DataType = "MMM-ACC-V2.2", Holder = human,
            Body = new JsonObject
            {
                ["Header"] = "MMM-ACC-V2.2", ["MInstanceID"] = ID, ["MEnvironmentID"] = EnvironmentID, ["HumanID"] = human, ["AccountID"] = account, ["AccountLevel"] = level,
                ["Email"] = profile["PersonalProfile"]?["Data"]?[0]?["Email"]?.GetValue<string>()?.Trim(),        // the Account's email (Participant, Officer): how the chair invites (2026-11-26)
                ["Processes"] = new JsonArray(users.Select(u => (JsonNode)new JsonObject { ["ProcessID"] = u }).ToArray()),
                ["Items"] = new JsonArray(createdItems.Select(i => (JsonNode)new JsonObject { ["ItemID"] = i }).ToArray())
            }
        };
        return Ok(201, service, new Complements { With = [JsonValue.Create(account)] }) with { Tokens = tokens };
    }

    // TRSrvc. A Transaction between its Sender (the requesting Process) and its
    // Receiver, of the Assets it names: the Assets pass to the Sender, the Transaction
    // becomes Final, and so do the Rights the Sender acquires with it.
    private Outcome Transact(string service, string source, List<JsonObject> with)
    {
        var tr = with.FirstOrDefault(i => Header(i) == "MMM-TRA-V2.2");
        if (tr is null) return Fail(422, service, "FaultyPA", "Transact takes a Transaction");
        var trId = tr["TransactionID"]!.GetValue<string>();
        if (tr["SenderData"]?["SenderID"]?.GetValue<string>() != source) return Fail(403, service, "InsRights", "the Sender of a Transaction is the Process requesting it");
        var receiver = tr["ReceiverData"]?["ReceiverID"]?.GetValue<string>();
        if (receiver is null || !processes.ContainsKey(receiver)) return Fail(404, service, "IncID", $"no Receiver {receiver}");
        var assets = (tr["AssetID"]?.AsArray() ?? []).Select(a => a?["ItemID"]?.GetValue<string>()).OfType<string>().ToList();
        foreach (var a in assets)
        {
            if (!items.TryGetValue(a, out var it)) return Fail(404, service, "IncID", $"no Asset {a}");
            if (it.Holder != receiver) return Fail(409, service, "Clash", $"{a} is not {receiver}'s to sell");
        }
        foreach (var a in assets) items[a].Holder = source;
        tr["TransactionStatus"] = "Final";
        items[trId] = new Item { ID = trId, DataType = "MMM-TRA-V2.2", Body = tr, Holder = source };
        var rs = new List<JsonNode?> { Wire.Ref(trId, "Final") };
        rs.AddRange(GrantAll(with, source));
        return Ok(200, service, new Complements { With = rs });
    }

    // LCSrvc. MM-Add places an Item its Process owns at an M-Location it may enter.
    private Outcome MMAdd(string service, string source, Complements c, List<JsonObject> with)
    {
        if (Need(service, c.Nil, "Nil") is { } f1) return f1;
        if (Need(service, c.At, "At") is { } f2) return f2;
        // A Speech Object is made where it is added: a User places what it said at its seat, and the Item is its own.
        var created = false;
        if (!items.ContainsKey(c.Nil!) && with.FirstOrDefault(i => Header(i) == "OSD-BSO-V1.5" && (string?)i["BasicSpeechObjectID"] == c.Nil) is { } speech)
        {
            // A Speech Object declared with a DataURI and no data is a stream: its data arrives, as it is captured, until its time is over or its User ends it.
            var first = speech["BasicSpeechObjectData"]?[0];
            items[c.Nil!] = new Item { ID = c.Nil!, DataType = "OSD-BSO-V1.5", Body = speech, Holder = source, Streaming = first?["DataURI"] is not null && first["Data"] is null };
            created = true;
        }
        // A shared screen: a Visual Object declared with an address its frames come from and no data, a stream. One at a time in a place: a second is refused (409), unless it is the same User's.
        if (!items.ContainsKey(c.Nil!) && with.FirstOrDefault(i => Header(i) == ScreenHeader && (string?)i["BasicVisualObjectID"] == c.Nil) is { } screen
            && screen["BasicVisualObjectData"]?[0] is JsonObject frames && frames["DataURI"] is not null && frames["Data"] is null)
        {
            if (c.At is not null && items.Values.FirstOrDefault(i => i.DataType == ScreenHeader && i.Streaming && i.Location == c.At && i.Holder != source) is { } other)
                return Fail(409, service, "Busy", $"{other.Holder} is sharing a screen here");
            foreach (var old in items.Values.Where(i => i.DataType == ScreenHeader && i.Holder == source && i.Streaming).Select(i => i.ID).ToList()) items.Remove(old);   // its own, earlier
            items[c.Nil!] = new Item { ID = c.Nil!, DataType = ScreenHeader, Body = screen, Holder = source, Streaming = true };
            created = true;
        }
        // A 3D Model Object the Request carries is made where it is added, its Process's own: an object of a virtual stage, placed by the show.
        if (!items.ContainsKey(c.Nil!) && with.FirstOrDefault(i => Header(i) == "OSD-3DO-V1.5" && (string?)i["3DModelObjectID"] == c.Nil) is { } model)
        {
            items[c.Nil!] = new Item { ID = c.Nil!, DataType = "OSD-3DO-V1.5", Body = model, Holder = source };
            created = true;
        }
        // An Audio Object the Request carries (OSD-AUO-V1.5) is made where it is added, its Process's own: a sound of the place (the applause of a theatre, N-26). It is heard by whoever
        // may read the M-Location it is in: public where the M-Location is public.
        if (!items.ContainsKey(c.Nil!) && with.FirstOrDefault(i => Header(i) == "OSD-AUO-V1.5" && (string?)i["AudioObjectID"] == c.Nil) is { } sound)
        {
            items[c.Nil!] = new Item { ID = c.Nil!, DataType = "OSD-AUO-V1.5", Body = sound, Holder = source, Public = items.TryGetValue(c.At!, out var place) && place.Public, Started = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            created = true;
        }
        if (!items.TryGetValue(c.Nil!, out var it)) return Fail(404, service, "IncID", $"no Item {c.Nil}");
        if (!items.ContainsKey(c.At!)) return Fail(404, service, "IncID", $"no M-Location {c.At}");
        if (it.Holder != source && !Allowed(source, "MM-Add", c.Nil, c.At)) return Fail(403, service, "InsRights", $"{c.Nil} is not {source}'s");
        if (!MayEnter(source, "MM-Add", c.Nil!, c.At!)) { if (created) items.Remove(c.Nil!); return Fail(403, service, "InsRights", $"{source} may not place Items at {c.At}"); }
        it.Location = c.At; it.SpatialAttitude = SpatialAttitude(with);
        return Ok(200, service, new Complements { With = GrantAll(with, source) });
    }

    // LCSrvc. MM-Move moves an Item from where it is to an M-Location the Process may enter.
    private Outcome MMMove(string service, string source, Complements c, List<JsonObject> with)
    {
        if (Need(service, c.Nil, "Nil") is { } f1) return f1;
        if (Need(service, c.To, "To") is { } f2) return f2;
        if (!items.TryGetValue(c.Nil!, out var it)) return Fail(404, service, "IncID", $"no Item {c.Nil}");
        if (!items.ContainsKey(c.To!)) return Fail(404, service, "IncID", $"no M-Location {c.To}");
        if (it.Holder != source) return Fail(403, service, "InsRights", $"{c.Nil} is not {source}'s");
        if (c.From is not null && it.Location != c.From) return Fail(409, service, "Clash", $"{c.Nil} is at {it.Location}, not {c.From}");
        if (!MayEnter(source, "MM-Move", c.Nil!, c.To!)) return Fail(403, service, "InsRights", $"{source} may not move {c.Nil} to {c.To}");
        it.Location = c.To; it.SpatialAttitude = SpatialAttitude(with) ?? it.SpatialAttitude;
        return Ok(200, service, new Complements { With = GrantAll(with, source) });
    }

    // How many M-Locations contain an Item, one inside the other: a Persona at the Square 1, one at a Seat in a Room on a Parcel 3.
    private int DepthOf(Item it) { var n = 0; for (var l = it.Location; l is not null && n < 8 && items.TryGetValue(l, out var p); l = p.Location) n++; return n; }

    // The yaw of an Item (degrees: the first angle of the Orientation of its Spatial Attitude; zero looks north, positive turns to the west - provisional,
    // until the specification states the convention), or null.
    private static double? YawOf(Item it) =>
        it.SpatialAttitude?["Orientation"]?["Orientation"] is JsonArray a && a.Count >= 1 && a[0] is not null ? a[0]!.GetValue<double>() : null;

    // LCSrvc. MM-Capture takes the part of the Audio-Visual Scene of an M-Location that a Point of View sees: here, the Things - Locations and Items, with
    // their place in metres (X east, Y north, Z up) and their yaw - that are in the M-Location (or in a Location inside it) and perceptible. A simplification of
    // the Scene Descriptors of OSD, to be replaced by them; the Aperture of the Point of View does not yet limit what is returned.
    private Outcome MMCapture(string service, string source, Complements c, List<JsonObject> with)
    {
        if (Need(service, c.At, "At") is { } f1) return f1;
        if (!items.ContainsKey(c.At!)) return Fail(404, service, "IncID", $"no M-Location {c.At}");
        // A Process captures an M-Location it may read or enter, or one that one of its Items is in (its Persona seated in the Room).
        bool IsIn(string? l) { for (var d = 0; l is not null && d < 8; d++, l = items.GetValueOrDefault(l)?.Location) if (l == c.At) return true; return false; }
        // ...unless a Right bars it from the places in it (May Not: the convener took it out of the meeting).
        bool Barred() => Live(source).SelectMany(Entries).Where(e => e.Verb == "May Not")
                             .SelectMany(e => e.PAs).Select(Template).OfType<PA>().Any(t => t.Target is not null && IsIn(t.Target));
        if (!MayRead(source, c.At!) && !MayEnter(source, "MM-Capture", source, c.At!) && !(items.Values.Any(i => i.Holder == source && IsIn(i.Location)) && !Barred()))
            return Fail(403, service, "InsRights", $"{source} may not capture {c.At}");
        var things = Snapshot()["Things"]!.AsArray().OfType<JsonObject>().ToList();
        bool Inside(JsonObject t)
        {
            for (var depth = 0; depth < 8 && t is not null; depth++)
            {
                if ((string?)t["ID"] == c.At) return true;
                var at = (string?)t["At"];
                t = at is null ? null! : things.FirstOrDefault(x => (string?)x["ID"] == at)!;
            }
            return false;
        }
        var heardSince = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 60_000;   // a Speech Object is heard while it lasts: what was said more than a minute ago is not returned
        var scene = things.Where(t => Inside(t) && (bool?)t["Perceptible"] != false && ((string?)t["Kind"] != "Speech" || (bool?)t["Streaming"] == true || (t["End"]?.GetValue<long>() ?? 0) >= heardSince)).Select(t => (JsonNode)t.DeepClone()).ToArray();
        var item = new JsonObject
        {
            ["ItemName"] = $"Scene-{c.At}", ["MInstanceID"] = ID, ["MEnvironmentID"] = EnvironmentID,
            ["ItemValueOrRef"] = new JsonArray(new JsonObject { ["At"] = c.At, ["Things"] = new JsonArray(scene) }),
            ["DescrMetadata"] = "The perceptible Things of the M-Location, simplified: not yet the Scene Descriptors of OSD"
        };
        return Ok(200, service, new Complements { With = [item] });
    }

    // LCSrvc. MM-Animate animates an Item its Process owns with an Animation Stream it owns.
    private Outcome MMAnimate(string service, string source, Complements c, List<string> refs, List<JsonObject> with)
    {
        if (Need(service, c.Nil, "Nil") is { } f1) return f1;
        if (!items.TryGetValue(c.Nil!, out var it)) return Fail(404, service, "IncID", $"no Item {c.Nil}");
        // The Stream: an Animation Stream the Process owns, or a Portable Avatar (PAF-PAV) the Request carries, which becomes an Item of the Process.
        var avatar = with.FirstOrDefault(i => Header(i) == "PAF-PAV-V1.6" && i["PortableAvatarID"] is not null);
        if (avatar is not null)
        {
            var id = (string)avatar["PortableAvatarID"]!;
            if (items.TryGetValue(id, out var old) && old.Holder != source) return Fail(409, service, "Clash", $"{id} is another Process's");
            items[id] = new Item { ID = id, DataType = "PAF-PAV-V1.6", Body = avatar, Holder = source };
        }
        var stream = avatar is not null ? (string)avatar["PortableAvatarID"]! : refs.FirstOrDefault(r => items.TryGetValue(r, out var s) && s.DataType is "PAF-AMO-V1.6" or "PAF-PAV-V1.6");
        if (stream is null) return Fail(422, service, "FaultyPA", "MM-Animate takes an Animation Stream or a Portable Avatar");
        if (it.Holder != source || items[stream].Holder != source) return Fail(403, service, "InsRights", $"{c.Nil} or {stream} is not {source}'s");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        it.Animation = stream;
        it.AnimatedUntil = now;
        // At an M-Location: the Stream takes the Item there, walking (WalkSpeed); the Item is at that M-Location from the start of the walk, and is seen moving along it.
        if (c.At is not null)
        {
            if (!items.TryGetValue(c.At, out var place) || place.DataType != "MMM-LOC") return Fail(404, service, "IncID", $"no M-Location {c.At}");
            if (!MayEnter(source, "MM-Animate", c.Nil!, c.At)) return Fail(403, service, "InsRights", $"{source} may not take {c.Nil} to {c.At}");
            var from = PlaceOf(it); var to = PlaceOf(place);
            // THE WAY: straight, or round the Items that have a footprint (furniture), turning at their corners; at the speed the Stream gives, if it gives one.
            var obstacles = items.Values.Where(o => o.Footprint is not null && o.ID != it.ID).Select(o => (At: PlaceOf(o), Size: o.Footprint!)).ToList();
            var path = Route(from, to, obstacles);
            var length = Enumerable.Range(1, path.Count - 1).Sum(k => Math.Sqrt(Math.Pow(path[k][0] - path[k - 1][0], 2) + Math.Pow(path[k][1] - path[k - 1][1], 2)));
            var seconds = length / (SpeedOf(avatar) ?? WalkSpeed);
            it.WalkFrom = from; it.WalkTo = to; it.WalkPath = path.Count > 2 ? path.ToArray() : null; it.WalkStart = now; it.WalkEnd = now + (long)(seconds * 1000);
            it.Location = c.At;
            var dx = to[0] - path[^2][0]; var dy = to[1] - path[^2][1];
            var yaw = Math.Abs(dx) + Math.Abs(dy) > 1e-6 ? Math.Atan2(-dx, dy) * 180 / Math.PI : YawOf(it) ?? 0;   // facing the way it walked its last stretch (zero north, positive west)
            it.SpatialAttitude = new JsonObject
            {
                ["Position"] = new JsonObject { ["CartPosition"] = new JsonArray(0.0, 0.0, 0.0) },
                ["Orientation"] = new JsonObject { ["Orientation"] = new JsonArray(yaw, 0.0, 0.0) }
            };
            it.AnimatedUntil = it.WalkEnd;
        }
        // A Portable Avatar that speaks: the Item is animated while the speech lasts (its PCM, 16 kHz, 16 bit, mono, or a WAV file).
        if (avatar?["SPOOrSPOID"]?[0] is JsonObject speech && SpeechSeconds(speech) is { } said)
            it.AnimatedUntil = Math.Max(it.AnimatedUntil, now + (long)(said * 1000));
        return Ok(200, service, new Complements { With = GrantAll(with.Where(i => i != avatar).ToList(), source) });
    }

    // The speed of a walk an Animation Stream makes, metres a second.
    public const double WalkSpeed = 1.0;

    // The speed a Stream gives its walk: a Portable Avatar whose Body Descriptors say "speed 1.4 m/s". None: WalkSpeed.
    private static double? SpeedOf(JsonObject? avatar)
    {
        foreach (var d in avatar?["BodyDescriptorsObject"]?["BodyDescriptorsData"] as JsonArray ?? [])
            if ((string?)d?["Data"] is { } text && System.Text.RegularExpressions.Regex.Match(text, @"speed\s+([0-9]+(?:\.[0-9]+)?)\s*m/s") is { Success: true } m)
                return Math.Clamp(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 0.2, 3.0);
        return null;
    }

    // THE WAY ROUND OBSTACLES: the shortest way from one place to another that does not cross an obstacle's footprint grown by the half-width of a walker (Clearance):
    // the corners of the grown footprints are the places where the way may turn (a visibility graph, then Dijkstra). No obstacle in the way: the straight line.
    public const double Clearance = 0.35;
    public static List<double[]> Route(double[] from, double[] to, IReadOnlyList<(double[] At, double[] Size)> obstacles)
    {
        var boxes = obstacles.Select(o => (X0: o.At[0] - o.Size[0] / 2 - Clearance, X1: o.At[0] + o.Size[0] / 2 + Clearance, Y0: o.At[1] - o.Size[1] / 2 - Clearance, Y1: o.At[1] + o.Size[1] / 2 + Clearance))
                             .Where(b => !Inside(b, from) && !Inside(b, to)).ToList();   // a walk that starts or ends by an obstacle is not stopped by it
        static bool Inside((double X0, double X1, double Y0, double Y1) b, double[] p) => p[0] > b.X0 && p[0] < b.X1 && p[1] > b.Y0 && p[1] < b.Y1;
        // Does the segment a-b pass through the inside of a box? (Liang-Barsky clipping against the box shrunk a little, so that walking along its side is allowed.)
        static bool Crosses((double X0, double X1, double Y0, double Y1) b, double[] a, double[] e)
        {
            const double Eps = 1e-3;
            double t0 = 0, t1 = 1, dx = e[0] - a[0], dy = e[1] - a[1];
            foreach (var (p, q) in new[] { (-dx, a[0] - (b.X0 + Eps)), (dx, (b.X1 - Eps) - a[0]), (-dy, a[1] - (b.Y0 + Eps)), (dy, (b.Y1 - Eps) - a[1]) })
            {
                if (Math.Abs(p) < 1e-12) { if (q < 0) return false; continue; }
                var r = q / p;
                if (p < 0) { if (r > t1) return false; if (r > t0) t0 = r; } else { if (r < t0) return false; if (r < t1) t1 = r; }
            }
            return t1 - t0 > 1e-9;
        }
        bool Free(double[] a, double[] e) => boxes.All(b => !Crosses(b, a, e));
        if (Free(from, to)) return [from, to];
        var nodes = new List<double[]> { from, to };
        foreach (var b in boxes) nodes.AddRange([[b.X0 - 0.01, b.Y0 - 0.01], [b.X1 + 0.01, b.Y0 - 0.01], [b.X1 + 0.01, b.Y1 + 0.01], [b.X0 - 0.01, b.Y1 + 0.01]]);
        nodes = nodes.Where((p, k) => k < 2 || boxes.All(b => !Inside(b, p))).ToList();
        double D(double[] a, double[] e) => Math.Sqrt((a[0] - e[0]) * (a[0] - e[0]) + (a[1] - e[1]) * (a[1] - e[1]));
        var dist = Enumerable.Repeat(double.MaxValue, nodes.Count).ToArray(); var prev = Enumerable.Repeat(-1, nodes.Count).ToArray(); var done = new bool[nodes.Count];
        dist[0] = 0;
        for (var round = 0; round < nodes.Count; round++)
        {
            var u = -1; for (var k = 0; k < nodes.Count; k++) if (!done[k] && dist[k] < double.MaxValue && (u < 0 || dist[k] < dist[u])) u = k;
            if (u < 0 || u == 1) break;
            done[u] = true;
            for (var v = 0; v < nodes.Count; v++)
                if (!done[v] && v != u && Free(nodes[u], nodes[v]) && dist[u] + D(nodes[u], nodes[v]) < dist[v]) { dist[v] = dist[u] + D(nodes[u], nodes[v]); prev[v] = u; }
        }
        if (prev[1] < 0) return [from, to];   // no way round: straight, as before
        var way = new List<double[]>(); for (var k = 1; k >= 0; k = prev[k]) way.Insert(0, nodes[k]);
        return way;
    }

    // Where a walk along a way is at a fraction of its length, and the yaw it faces there (zero north, positive west).
    private static (double[] At, double Yaw) AlongPath(double[][] path, double fraction)
    {
        var lengths = Enumerable.Range(1, path.Length - 1).Select(k => Math.Sqrt(Math.Pow(path[k][0] - path[k - 1][0], 2) + Math.Pow(path[k][1] - path[k - 1][1], 2))).ToArray();
        var left = fraction * lengths.Sum();
        for (var k = 0; k < lengths.Length; k++)
        {
            var (a, b) = (path[k], path[k + 1]);
            var yaw = Math.Atan2(-(b[0] - a[0]), b[1] - a[1]) * 180 / Math.PI;
            if (left <= lengths[k] || k == lengths.Length - 1)
            {
                var f = lengths[k] < 1e-9 ? 1 : Math.Clamp(left / lengths[k], 0, 1);
                return ([a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f], yaw);
            }
            left -= lengths[k];
        }
        return (path[^1], 0);
    }

    // Where an Item is now, in metres in the M-Environment (the centre of an M-Location).
    private double[] PlaceOf(Item it)
    {
        var things = Snapshot()["Things"]!.AsArray().OfType<JsonObject>();
        var t = things.FirstOrDefault(x => (string?)x["ID"] == it.ID);
        return t is null ? [0, 0] : [(double)t["X"]!, (double)t["Y"]!];
    }

    private static double? SpeechSeconds(JsonObject speech)
    {
        var b64 = (string?)speech["Data"] ?? (string?)speech["BasicSpeechObjectData"]?[0]?["Data"];
        if (b64 is null) return null;
        var n = b64.Length * 3 / 4;
        return Math.Max(0, n - 44) / 32000.0;
    }

    // LCSrvc. Property Change makes an Item its Process owns perceptible and, when the Request carries a Light Source (CVE-LTS-V1.0, RQ7 of MMM-PCP-V2.2), gives
    // the Item that Light Source: its Type, Colour and Intensity. A Light Source of another Item is refused.
    private Outcome PropertyChange(string service, string source, Complements c, List<JsonObject> with)
    {
        if (Need(service, c.Nil, "Nil") is { } f1) return f1;
        if (!items.TryGetValue(c.Nil!, out var it)) return Fail(404, service, "IncID", $"no Item {c.Nil}");
        if (it.Holder != source) return Fail(403, service, "InsRights", $"{c.Nil} is not {source}'s");
        var light = with.FirstOrDefault(i => Header(i) == "CVE-LTS-V1.0");
        if (light is not null && (string?)light["LightSourceID"] != c.Nil) return Fail(422, service, "FaultyPA", $"the Light Source is {(string?)light["LightSourceID"]}'s, not {c.Nil}'s");
        if (light is not null) it.LightSource = light;
        // RQ12 (MMM-PCP-V3.0): the Item Properties of the Item, an Item Property (MMM-IPR-V3.0) whose schema holds each Name to the vocabulary and its Value to the values of the Name.
        var props = with.Where(i => Header(i) == "MMM-IPR-V3.0").ToList();
        if (props.FirstOrDefault(p => (string?)p["ItemID"] != c.Nil) is { } other) return Fail(422, service, "FaultyPA", $"the Item Property is {(string?)other["ItemID"]}'s, not {c.Nil}'s");
        if (props.Count > 0 && !schemas.Knows("MMM-IPR-V3.0")) return Fail(422, service, "FaultyPA", "this M-Instance does not know the Item Property vocabulary (MMM-IPR-V3.0)");
        foreach (var p in props.SelectMany(p => p["Properties"]?.AsArray() ?? []).OfType<JsonObject>())
            it.Properties[(string)p["Name"]!] = p["Value"]?.DeepClone();
        it.Perceptible = true;
        return Ok(200, service, new Complements { With = GrantAll(with, source) });
    }

    // EISrvc (the Import Service). UM-Capture: a Process of a human - its Device - asks that a scene at a U-Location be captured as Data, With the Qualifier the Data is to have.
    // The Data is named by the requester (Nil) and belongs to it; it is not yet an Item of the M-Instance: Identify makes it one. A Qualifier whose transport is a stream says that
    // the Data arrives as it is captured (a Speech Qualifier, TFA-SPQ, names the stream format of the speech and the time it lasts).
    private Outcome UMCapture(string service, string source, Complements c, List<string> refs, List<JsonObject> with)
    {
        if (Need(service, c.Nil, "Nil") is { } f1) return f1;
        if (Need(service, c.At, "At") is { } f2) return f2;
        if (items.ContainsKey(c.Nil!)) return Fail(409, service, "Clash", $"{c.Nil} exists already");
        var qualifier = with.FirstOrDefault(i => Header(i)?.StartsWith("TFA-", StringComparison.Ordinal) == true);
        items[c.Nil!] = new Item { ID = c.Nil!, DataType = "MMM-DAT", Holder = source, Location = c.At, Qualifier = qualifier, Started = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        return Ok(201, service, new Complements { Nil = c.Nil, With = refs.Select(r => (JsonNode?)JsonValue.Create(r)).ToList() });
    }

    // IDSrvc. Identify makes an Item of Data and its Qualifier. Speech captured (a Speech Qualifier, TFA-SPQ) becomes a Speech Object (OSD-BSO): its time is the Qualifier's, its data
    // is delivered as the stream the Qualifier names, and its Identifier is the one the Data was named with; the Item is its requester's, and is not anywhere until MM-Added.
    // Any other Data is, here, the Animation Stream of the human captured.
    private Outcome Identify(string service, string source, Complements c, List<string> refs, List<JsonObject> with)
    {
        if (Need(service, c.Nil, "Nil") is { } f1) return f1;
        if (!items.TryGetValue(c.Nil!, out var data)) return Fail(404, service, "IncID", $"no Data {c.Nil}");
        if (data.Holder != source) return Fail(403, service, "InsRights", $"{c.Nil} is not {source}'s");
        var qualifier = with.FirstOrDefault(i => Header(i) == "TFA-SPQ-V1.5") ?? (data.Qualifier is { } q && Header(q) == "TFA-SPQ-V1.5" ? q : null);
        if (data.DataType == "MMM-DAT" && qualifier is not null)
        {
            var stream = qualifier["Formats"]?["TransportFormats"]?["StreamFormat"] is not null;
            if (!stream) return Fail(422, service, "IncDQ", "the Data of a speech arrives as a stream: its Speech Qualifier names the stream format");
            var id = data.ID;
            data.Body = new JsonObject
            {
                ["Header"] = "OSD-BSO-V1.5", ["MInstanceID"] = ID, ["BasicSpeechObjectID"] = id,
                ["BasicSpeechObjectData"] = new JsonArray(new JsonObject { ["DataURI"] = $"/ws/v2.2/speech/{id}/listen" }),
                ["BasicSpeechDataQualifier"] = qualifier.DeepClone()
            };
            data.DataType = "OSD-BSO-V1.5"; data.Streaming = true; data.Location = null; data.Qualifier = null;
            return Ok(201, service, new Complements { Nil = id });
        }
        // Any other Data is the Animation Stream of the human captured; if its Animation Qualifier (TFA-AMQ) names a stream format, it arrives as it is captured.
        var animation = with.FirstOrDefault(i => Header(i) == "TFA-AMQ-V1.5") ?? (data.Qualifier is { } aq && Header(aq) == "TFA-AMQ-V1.5" ? aq : null);
        var streamId = $"{c.Nil}-Stream";
        items[streamId] = new Item { ID = streamId, DataType = "PAF-AMO-V1.6", Holder = source, Qualifier = animation, Streaming = animation?["Formats"]?["TransportFormat"] is not null };
        return Ok(201, service, new Complements { Nil = streamId });
    }

    // COSrvc. MM-Send conveys a Message to a Process: the Message reaches its inbox and
    // the Rights to Use it are the recipient's.
    private Outcome MMSend(string service, string source, Complements c, List<JsonObject> with)
    {
        if (Need(service, c.Nil, "Nil") is { } f1) return f1;
        if (Need(service, c.To, "To") is { } f2) return f2;
        if (!processes.ContainsKey(c.To!)) return Fail(404, service, "IncID", $"no Process {c.To}");
        // What is sent: a Message, or a Speech Object - a User's own capture of what it said, with the time it lasts (a Basic Speech Object, OSD-BSO).
        var message = with.FirstOrDefault(i => Header(i) is "MMM-MSG-V2.2" or "OSD-BSO-V1.5");
        // A Speech Object the sender already has - its own capture, still a stream - is sent as it is: the recipient is given that Item (it may read it, and listen to it), not a copy.
        if (message is not null && Header(message) == "OSD-BSO-V1.5" && message["BasicSpeechObjectID"]?.GetValue<string>() is { } own && items.TryGetValue(own, out var sent) && sent.Holder == source)
        {
            var inbox = inboxes.TryGetValue(c.To!, out var ib) ? ib : inboxes[c.To!] = [];
            if (!inbox.Contains(own)) inbox.Add(own);
            return Ok(200, service, new Complements { With = GrantAll(with, c.To!) });
        }
        items[c.Nil!] = new Item { ID = c.Nil!, DataType = message is null ? "MMM-MSG-V2.2" : Header(message)!, Body = message, Holder = source };
        var box = inboxes.TryGetValue(c.To!, out var b) ? b : inboxes[c.To!] = [];
        if (!box.Contains(c.Nil!)) box.Add(c.Nil!);          // a Message sent again under the same identifier replaces the first
        return Ok(200, service, new Complements { With = GrantAll(with, c.To!) });
    }

    // EISrvc. MU-Actuate renders an Item its Process owns at a U-Location, as an R-Item.
    private Outcome MUActuate(string service, string source, Complements c, List<string> refs, List<JsonObject> with)
    {
        if (Need(service, c.Nil, "Nil") is { } f1) return f1;
        if (Need(service, c.At, "At") is { } f2) return f2;
        // A Speech Object is made where it is added: a User places what it said at its seat, and the Item is its own.
        var created = false;
        if (!items.ContainsKey(c.Nil!) && with.FirstOrDefault(i => Header(i) == "OSD-BSO-V1.5" && (string?)i["BasicSpeechObjectID"] == c.Nil) is { } speech)
        {
            // A Speech Object declared with a DataURI and no data is a stream: its data arrives, as it is captured, until its time is over or its User ends it.
            var first = speech["BasicSpeechObjectData"]?[0];
            items[c.Nil!] = new Item { ID = c.Nil!, DataType = "OSD-BSO-V1.5", Body = speech, Holder = source, Streaming = first?["DataURI"] is not null && first["Data"] is null };
            created = true;
        }
        if (!items.TryGetValue(c.Nil!, out var it)) return Fail(404, service, "IncID", $"no Item {c.Nil}");
        if (it.Holder != source) return Fail(403, service, "InsRights", $"{c.Nil} is not {source}'s");
        var ritem = $"RItem-{c.Nil}";
        items[ritem] = new Item
        {
            ID = ritem, DataType = "MMM-RIT-V2.2", Holder = source, Location = c.At,
            Body = new JsonObject { ["Header"] = "MMM-RIT-V2.2", ["MInstanceID"] = ID, ["RItemID"] = ritem, ["RItemType"] = refs.FirstOrDefault() }
        };
        var rs = new List<JsonNode?>(GrantAll(with, source));
        return Ok(201, service, new Complements { Nil = ritem, With = rs });
    }

    // RTSrvc. Rights Change gives a Process Rights (May) or takes them away (May Not),
    // on Locations the requesting Process owns.
    private Outcome RightsChange(string service, string source, Complements c, List<JsonObject> with)
    {
        if (Need(service, c.Nil, "Nil") is { } f1) return f1;
        if (!processes.ContainsKey(c.Nil!)) return Fail(404, service, "IncID", $"no Process {c.Nil}");
        var rights = with.Where(i => Header(i) == "MMM-RGT-V2.2").ToList();
        if (rights.Count == 0) return Fail(422, service, "FaultyPA", "Rights Change takes Rights");
        Store(with);
        // Rights on a place are given by the Process holding the Rights to it: the targets (To, At) of the Process
        // Actions the Rights name - the Room - not what they act on (the Persona moving in).
        foreach (var r in rights)
            foreach (var t in Entries(r).SelectMany(e => e.PAs).Select(Template).OfType<PA>().Select(t => t.Target).OfType<string>())
                if (items.TryGetValue(t, out var it) && it.Holder != source && !it.Public)
                    return Fail(403, service, "InsRights", $"{t} is not {source}'s to give Rights on");

        var rs = new List<JsonNode?>();
        foreach (var r in rights)
        {
            var id = r["RightsID"]!.GetValue<string>();
            var withdrawn = Entries(r).Where(e => e.Verb == "May Not").SelectMany(e => e.PAs).ToHashSet();
            if (withdrawn.Count > 0 && holdings.TryGetValue(c.Nil!, out var held))
                held.RemoveAll(h => Entries(items[h].Body!).Any(e => e.Verb == "May" && e.PAs.Any(p => withdrawn.Any(w => SamePA(p, w)))));
            rs.Add(Grant(r, c.Nil!));
        }
        return Ok(200, service, new Complements { With = rs });
    }

    // ---------------------------------------------------------------- Rights
    private sealed record Entry(string Verb, IReadOnlyList<string> PAs);
    private sealed record PA(string Action, string? Nil, string? Target);

    private static IEnumerable<Entry> Entries(JsonObject rights) =>
        (rights["RightsData"]?.AsArray() ?? []).OfType<JsonObject>().Select(e => new Entry(
            e["DeonticVerb"]?.GetValue<string>() ?? "",
            (e["PAIDOrPA"]?.AsArray() ?? []).Select(p => p?["ProcessActionID"]?.GetValue<string>()).OfType<string>().ToList()));

    // A Process Action the M-Instance keeps, as what it allows: its Action, by its
    // Header; its Nil; its target - To, At, or the Point of View of To.
    private PA? Template(string id)
    {
        if (!items.TryGetValue(id, out var it) || it.Body is null || schemas.ActionOf(it.DataType) is not { } action) return null;
        string? nil = null, target = null;
        foreach (var part in it.Body.Where(p => p.Key.EndsWith("PARQ", StringComparison.Ordinal)).SelectMany(p => p.Value?.AsObject() ?? []))
        {
            if (part.Value is not JsonObject rq) continue;
            var complement = rq["Complement"]?.GetValue<string>();
            var value = rq.Where(p => p.Key != "Complement").Select(p => p.Value switch
            {
                JsonValue v when v.TryGetValue<string>(out var s) => s,
                JsonObject o => o.Where(q => q.Key.EndsWith("ID", StringComparison.Ordinal)).Select(q => q.Value?.GetValue<string>()).FirstOrDefault(),
                _ => null
            }).FirstOrDefault(v => v is not null);
            if (complement == "Nil") nil = value;
            else if (complement is "To" or "At") target = value;
        }
        return new PA(action, nil, target);
    }

    private bool SamePA(string a, string b) => a == b || (Template(a) is { } x && Template(b) is { } y && x == y);

    private static bool Matches(PA t, string action, string? nil, string? target) =>
        t.Action == action && (t.Nil is null || t.Nil == nil) && (t.Target is null || t.Target == target);

    // Does a Right of the Process allow it? A May Not prevails.
    private bool Allowed(string process, string action, string? nil, string? target)
    {
        var held = Live(process).ToList();
        bool Any(string verb) => held.SelectMany(Entries).Where(e => e.Verb == verb)
                                     .SelectMany(e => e.PAs).Select(Template).OfType<PA>().Any(t => Matches(t, action, nil, target));
        return !Any("May Not") && Any("May");
    }

    // An M-Location a Process may enter: its own, a Public one, one inside its own (a
    // Room on its Parcel), or one a Right lets it enter.
    private bool MayEnter(string process, string action, string item, string location)
    {
        if (Allowed(process, action, item, location)) return true;
        var denied = Live(process).SelectMany(Entries)
                     .Where(e => e.Verb == "May Not").SelectMany(e => e.PAs).Select(Template).OfType<PA>()
                     .Any(t => Matches(t, action, item, location));
        if (denied) return false;
        for (var l = location; l is not null && items.TryGetValue(l, out var it); l = it.Location)
            if (it.Holder == process || it.Public) return true;
        return false;
    }

    // THE RIGHTS A PROCESS HOLDS NOW. A Rights Item may carry a RightsTime: a Simple Time whose Start and End differ is the period the Rights are valid for (the Rights of a meeting
    // last as long as the meeting does); after its End they are not held. A Rights Item without one, or whose times are the same instant, does not expire.
    private IEnumerable<JsonObject> Live(string process) =>
        (holdings.GetValueOrDefault(process) ?? []).Select(r => items[r].Body!).Where(b => !Expired(b));

    // The time the M-Instance goes by (a test moves it).
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    private bool Expired(JsonObject rights)
    {
        if (rights["RightsTime"]?["SimpleTimeData"]?[0] is not JsonObject t || t["StartTime"] is null || t["EndTime"] is null) return false;
        var (start, end) = (t["StartTime"]!.GetValue<long>(), t["EndTime"]!.GetValue<long>());
        return end > start && Clock().ToUnixTimeMilliseconds() > end;
    }

    // The Items a Rights Item concerns: the targets and Nils of its Process Actions.
    private IEnumerable<string> Targets(JsonObject rights) =>
        Entries(rights).SelectMany(e => e.PAs).Select(Template).OfType<PA>().SelectMany(t => new[] { t.Target, t.Nil }).OfType<string>().Distinct();

    // Keep the Process Actions a Request carries, so that the Rights naming them can be read.
    private void Store(List<JsonObject> with)
    {
        foreach (var i in with)
            if (Header(i) is { } h && schemas.ActionOf(h) is not null && IdOf(i) is { } id)
                items[id] = new Item { ID = id, DataType = h, Body = i };
    }

    // Every Rights Item of a Request, made Final and held by the Process: Statused
    // References at Status=Final for the Response.
    private List<JsonNode?> GrantAll(List<JsonObject> with, string holder)
    {
        Store(with);
        return with.Where(i => Header(i) == "MMM-RGT-V2.2").Select(r => Grant(r, holder)).ToList();
    }

    private JsonNode Grant(JsonObject rights, string holder)
    {
        var id = rights["RightsID"]!.GetValue<string>();
        items[id] = new Item { ID = id, DataType = "MMM-RGT-V2.2", Body = rights, Holder = holder, Status = "Final" };
        var held = holdings.TryGetValue(holder, out var h) ? h : holdings[holder] = [];
        if (!held.Contains(id)) held.Add(id);
        return Wire.Ref(id, "Final");
    }

    // ---------------------------------------------------------------- helpers
    private static string? Header(JsonObject i) => i["Header"]?.GetValue<string>();

    private static readonly HashSet<string> NotTheId = ["MInstanceID", "UEnvironmentID", "MEnvironmentID", "humanID", "HumanID", "AssetID"];
    private static string? IdOf(JsonObject i) =>
        i.Where(p => p.Key.EndsWith("ID", StringComparison.Ordinal) && !NotTheId.Contains(p.Key) && p.Value is JsonValue)
         .Select(p => p.Value!.GetValue<string>()).FirstOrDefault();

    private static IEnumerable<string> Ids(JsonNode? array, string key) =>
        (array as JsonArray ?? []).Select(x => x?[key]?.GetValue<string>()).OfType<string>();

    private static JsonNode? SpatialAttitude(List<JsonObject> with) =>
        with.FirstOrDefault(i => Header(i) == "OSD-OSA-V1.5")?.DeepClone();

    private Outcome? Need(string service, string? value, string complement) =>
        value is null ? Fail(422, service, "FaultyPA", $"the Request has no {complement} Complement") : null;

    private static Outcome Ok(int http, string service, Complements c) =>
        new(http, new ProcessActionResponse { DestinationProcessID = service, Complements = c, PaStatus = new PAStatus { Code = "Ack" } });

    private static Outcome Fail(int http, string service, string code, string detail) =>
        new(http, new ProcessActionResponse { DestinationProcessID = service, PaStatus = new PAStatus { Code = code, Detail = detail } });

    // ---------------------------------------------------------------- ACCOUNTS WITH CREDENTIALS (2026-11-26)
    // To enter the M-Instance a human registers and opens an Account (Register), with its credentials: a Visitor gives its first name, last name and a password; the higher levels
    // (Participant, Officer) an email and an organisation too. Afterwards the human enters with them (Register again, with its credentials): its Users are given new tokens. The
    // credentials are kept as a salted hash (PBKDF2, SHA-256), never as given. A Persona is chosen from the catalogue when the human registers or, if it has none (a Visitor whose
    // visit ended), when it enters. AT THE END OF A VISIT a Visitor's Persona is taken away and offered again to whoever registers; the Account stays.
    public bool RequireCredentials { get; set; }

    // THE MANAGER LEVEL (2026-11-26): not an Account Level a person may choose (it is not offered); one person is the Manager of the M-Instance - the Account whose email is ManagerEmail
    // (the server's --manager). The Manager covers every other level: the Rights of an Officer to every place, and a seat in any meeting.
    public const string Manager = "Manager";
    public string? ManagerEmail { get; set; }
    private bool IsManagerEmail(string? email) => ManagerEmail is { Length: > 0 } m && email is { Length: > 0 } e && string.Equals(m.Trim(), e.Trim(), StringComparison.OrdinalIgnoreCase);
    // An Officer's level or above.
    public bool IsOfficerOrAbove(string user) { lock (gate) return accountLevels.GetValueOrDefault(user) is "Officer" or Manager; }
    public const int MinimumPassword = 6;
    private readonly Dictionary<string, string> credentials = new(StringComparer.Ordinal);   // human -> "salt:hash" (base64)

    private static string HashOf(string secret, byte[]? salt = null)
    {
        salt ??= RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(System.Text.Encoding.UTF8.GetBytes(secret), salt, 100_000, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
    }

    private bool Verifies(string human, string secret)
    {
        if (!credentials.TryGetValue(human, out var kept)) return false;
        var parts = kept.Split(':');
        var again = HashOf(secret, Convert.FromBase64String(parts[0])).Split(':')[1];
        return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(again), Convert.FromBase64String(parts[1]));
    }

    public bool IsRegistered(string human) { lock (gate) return processes.TryGetValue(human, out var p) && p.Kind == "Human"; }

    // DEVICES LINKED TO AN ACCOUNT (2026-11-26). A program that acts for the person - Thalia, the person's User - is linked to the Account once, when the person, entered in the world with
    // its password, approves the link; the device is then given a key of its own, kept here as a salted hash (as the password), with which it enters without the password. The person
    // sees its devices and removes a link at any time; a removed device enters no more.
    public sealed record LinkedDevice(string Id, string Name, string Hash, string Linked);
    private readonly Dictionary<string, List<LinkedDevice>> devices = new(StringComparer.Ordinal);   // human -> its linked devices

    public (string Id, string Key)? LinkDevice(string user, string name)
    {
        lock (gate)
        {
            if (!processes.TryGetValue(user, out var p) || p.HumanID is not { } human) return null;
            var id = "dev-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();
            var key = NewToken();
            if (!devices.TryGetValue(human, out var list)) devices[human] = list = [];
            list.Add(new LinkedDevice(id, name, HashOf(key), DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm")));
            return (id, $"{id}:{key}");
        }
    }

    public IReadOnlyList<LinkedDevice> DevicesOf(string user)
    {
        lock (gate) return processes.GetValueOrDefault(user)?.HumanID is { } h && devices.TryGetValue(h, out var l) ? l.Select(d => d with { Hash = "" }).ToList() : [];
    }

    public bool UnlinkDevice(string user, string id)
    {
        lock (gate) return processes.GetValueOrDefault(user)?.HumanID is { } h && devices.TryGetValue(h, out var l) && l.RemoveAll(d => d.Id == id) > 0;
    }

    private bool VerifiesDevice(string human, string deviceKey)
    {
        var colon = deviceKey.IndexOf(':');
        if (colon <= 0 || !devices.TryGetValue(human, out var list)) return false;
        var d = list.FirstOrDefault(x => x.Id == deviceKey[..colon]);
        if (d is null) return false;
        var parts = d.Hash.Split(':');
        var again = HashOf(deviceKey[(colon + 1)..], Convert.FromBase64String(parts[0])).Split(':')[1];
        return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(again), Convert.FromBase64String(parts[1]));
    }
    public bool HasCredentials(string human) { lock (gate) return credentials.ContainsKey(human); }

    // A human with an Account enters: its credentials verified (an Account of a run before Accounts had credentials takes the first it is given), its Users given new tokens, and the
    // Persona it chooses now made, if it has none.
    private Outcome Enter(string service, string human, string? secret, JsonObject profile)
    {
        if (secret is not null)
        {
            if (!credentials.ContainsKey(human)) credentials[human] = HashOf(secret);
            else if (!Verifies(human, secret)) return Fail(403, service, "InsRights", "these are not the credentials of the Account");
        }
        var users = processes.Values.Where(p => p.Kind == "User" && p.HumanID == human).ToList();
        if (items.GetValueOrDefault($"Account-{human}") is { Body: { } account } && IsManagerEmail(account["Email"]?.GetValue<string>()))
        {
            foreach (var u in users) accountLevels[u.ID] = Manager;
            account["AccountLevel"] = Manager;
        }
        // THE ROLE, CHOSEN AT THE FIRST ENTRY (2026-11-27): an Account with no Account Level yet takes the one the person chooses now (not the Manager's: that is never offered).
        // Once chosen it stays; an Officer that the Manager has not approved enters as a Participant meanwhile (the server makes the request: RequestOfficer).
        if (profile["PersonalProfile"]?["AccountLevel"]?.GetValue<string>() is { } chosenLevel && users.All(x => accountLevels.GetValueOrDefault(x.ID) is null))
        {
            if (chosenLevel == Manager || !AccountLevels.Any(l => l.Id == chosenLevel)) return Fail(422, service, "FaultyPA", $"{chosenLevel} is not an Account Level offered");
            var data = items.Values.FirstOrDefault(i => i.DataType == "MMM-PPR-V2.2" && i.Holder == human)?.Body?["PersonalProfile"]?["Data"]?[0];
            if (chosenLevel != "Visitor" && (data?["Email"]?.GetValue<string>() is not { Length: > 0 } || data?["Organisation"]?.GetValue<string>() is not { Length: > 0 }))
                return Fail(422, service, "FaultyPA", $"the {chosenLevel} Account Level needs an email and an organisation");
            foreach (var u in users) accountLevels[u.ID] = chosenLevel;
            if (items.GetValueOrDefault($"Account-{human}") is { Body: { } chosenAccount }) chosenAccount["AccountLevel"] = chosenLevel;
        }
        // DEPLOYING THE USERS (2026-11-27): at the first entry the human deploys its Users with their Personae - the H-User that its Account has, and an A-User it adds now. A User
        // the Account does not have yet is made here, with a Persona chosen for it; no more than one H-User and one A-User.
        var wanted = WantedUsers(profile).Where(w => w.Choice is not null).ToList();
        foreach (var (_, pick) in wanted)
        {
            if (!Catalogue.Contains(pick!)) return Fail(422, service, "FaultyPA", $"{pick} is not in the catalogue of Personae");
        }
        if (UsersAllowed(human, wanted, users.Select(x => accountLevels.GetValueOrDefault(x.ID)).FirstOrDefault(l => l is not null)) is { } notAllowed) return Fail(422, service, "FaultyPA", notAllowed);
        foreach (var p in profile["PersonalProfile"]?["Processes"]?.AsArray() ?? [])
            foreach (var e in p?["PersonaIDOrPersona"]?.AsArray() ?? [])
                if (e?["PersonaChoice"]?.GetValue<string>() is { } pick && e["PersonaID"]?.GetValue<string>() is { } pid && !items.ContainsKey(pid))
                {
                    if (PersonaTaken(pick)) return Fail(409, service, "Clash", $"{pick} was chosen by another human");
                    var holder = Ids(p?["ProcessIDOrProcess"], "ProcessID").FirstOrDefault(u => users.Any(x => x.ID == u));
                    if (holder is null && Ids(p?["ProcessIDOrProcess"], "ProcessID").FirstOrDefault() is { } added && !processes.ContainsKey(added))
                    {
                        var level = users.Select(x => accountLevels.GetValueOrDefault(x.ID)).FirstOrDefault(l => l is not null);
                        var proc = new Process { ID = added, Kind = "User", HumanID = human, Autonomous = ForAUsers(pick) };
                        processes[added] = proc; users.Add(proc);
                        if (level is not null) accountLevels[added] = level;
                        if (items.GetValueOrDefault($"Account-{human}") is { Body: { } acc }) (acc["Processes"] as JsonArray)?.Add(new JsonObject { ["ProcessID"] = added });
                        holder = added;
                    }
                    if (holder is null) return Fail(403, service, "InsRights", $"{pid} would not be a Persona of {human}'s Users");
                    items[pid] = new Item { ID = pid, DataType = "OSD-3DO-V1.5", Holder = holder, Model = CatalogueModel(pick) };
                    if (items.GetValueOrDefault($"Account-{human}") is { Body: { } account2 }) (account2["Items"] as JsonArray)?.Add(new JsonObject { ["ItemID"] = pid });
                }
        var tokens = new Dictionary<string, string>();
        // an A-User that has a token keeps it: its agent acts with it, whenever its human enters (2026-11-27)
        foreach (var u in users) { if (!(u.Autonomous && u.Token is not null)) u.Token = NewToken(); tokens[u.ID] = u.Token!; }
        return Ok(200, service, new Complements { With = [JsonValue.Create($"Account-{human}")] }) with { Tokens = tokens };
    }

    // THE END OF A VISIT: a Visitor's Persona leaves the M-Instance and is offered again; its token is no longer valid. Returns whether it was a Visitor with a Persona.
    // A VISITOR IS FORGOTTEN WHEN IT LEAVES (the author, 2026-11-27: the Visitor status is transient; next time registration is needed): its Account goes - its Users, its Persona, its
    // Personal Profile, its credentials, its devices, its Rights and Messages. Its Persona is offered again.
    public bool EndVisit(string user)
    {
        lock (gate)
        {
            if (accountLevels.GetValueOrDefault(user) != "Visitor" || !processes.TryGetValue(user, out var p) || p.HumanID is not { } human) return false;
            var users = processes.Values.Where(q => q.Kind == "User" && q.HumanID == human).Select(q => q.ID).ToList();
            var owners = new HashSet<string>(users, StringComparer.Ordinal) { human };
            foreach (var id in items.Values.Where(i => i.Holder is { } o && owners.Contains(o)).Select(i => i.ID).ToList()) items.Remove(id);
            foreach (var o in owners) { processes.Remove(o); accountLevels.Remove(o); holdings.Remove(o); inboxes.Remove(o); }
            credentials.Remove(human); devices.Remove(human); officerRequests.Remove(human); aUserNever.Remove(human);
            return true;
        }
    }

    // ---------------------------------------------------------------- OFFICER REQUESTS AND THE MANAGER (2026-11-27)
    // A person who chooses Officer, and is not one the chair designated, enters as a Participant and its request goes to the MMM Manager, who approves or refuses it in the
    // Manager Console; the person is told the outcome when it enters (or at once, if it is in the world).
    public sealed class OfficerRequest
    {
        public string Human { get; set; } = "";
        public string? First { get; set; }
        public string? Last { get; set; }
        public string? Organisation { get; set; }
        public string? Email { get; set; }
        public string Asked { get; set; } = "";
        public string Status { get; set; } = "Pending";      // Pending, Approved, Refused
        public string? Reason { get; set; }
        public bool Told { get; set; }                        // the person has been shown the outcome
    }
    private readonly Dictionary<string, OfficerRequest> officerRequests = new(StringComparer.Ordinal);
    private readonly HashSet<string> aUserNever = new(StringComparer.Ordinal);       // the humans who said Never to an A-User: never is never (2026-11-27)

    public void RequestOfficer(string human)
    {
        lock (gate)
        {
            var d = items.Values.FirstOrDefault(i => i.DataType == "MMM-PPR-V2.2" && i.Holder == human)?.Body?["PersonalProfile"]?["Data"]?[0];
            officerRequests[human] = new OfficerRequest
            {
                Human = human, First = d?["FirstName"]?.GetValue<string>(), Last = d?["LastName"]?.GetValue<string>(), Organisation = d?["Organisation"]?.GetValue<string>(),
                Email = d?["Email"]?.GetValue<string>(), Asked = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm")
            };
        }
    }
    public IReadOnlyList<OfficerRequest> OfficerRequests() { lock (gate) return officerRequests.Values.OrderBy(r => r.Status != "Pending").ThenBy(r => r.Asked).ToList(); }

    // The Manager's answer: approved, the human's Users become Officers.
    public bool AnswerOfficer(string human, bool approve, string? reason)
    {
        lock (gate)
        {
            if (!officerRequests.TryGetValue(human, out var r) || r.Status != "Pending") return false;
            r.Status = approve ? "Approved" : "Refused"; r.Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(); r.Told = false;
            if (approve)
            {
                foreach (var u in processes.Values.Where(q => q.Kind == "User" && q.HumanID == human)) accountLevels[u.ID] = "Officer";
                if (items.GetValueOrDefault($"Account-{human}") is { Body: { } account }) account["AccountLevel"] = "Officer";
            }
            return true;
        }
    }
    public void OfficerAnswerSeen(string user) { lock (gate) if (processes.GetValueOrDefault(user)?.HumanID is { } h && officerRequests.TryGetValue(h, out var r) && r.Status != "Pending") r.Told = true; }
    public void NeverAUser(string user) { lock (gate) if (processes.GetValueOrDefault(user)?.HumanID is { } h) aUserNever.Add(h); }
    public bool IsManager(string user) { lock (gate) return accountLevels.GetValueOrDefault(user) == Manager; }
    // WHO ACTS ON THE MPAI BUILDING SERVICE (2026-11-27): the Manager's H-User - the User the Manager drives - not its A-User (the Manager Console).
    public bool ActsForTheService(string user) { lock (gate) return accountLevels.GetValueOrDefault(user) == Manager && processes.GetValueOrDefault(user) is { Autonomous: false }; }

    // The email of a human's Account (its Personal Profile), if it has one.
    public string? EmailOfHuman(string? human)
    {
        if (human is null) return null;
        lock (gate) return items.GetValueOrDefault($"Account-{human}")?.Body?["Email"]?.GetValue<string>()
            ?? items.Values.FirstOrDefault(i => i.DataType == "MMM-PPR-V2.2" && i.Holder == human)?.Body?["PersonalProfile"]?["Data"]?[0]?["Email"]?.GetValue<string>();
    }

    // THE EMAIL OF A USER'S ACCOUNT: an invitation names it (2026-11-26).
    public string? EmailOf(string user)
    {
        lock (gate)
        {
            if (!processes.TryGetValue(user, out var p) || p.HumanID is null) return null;
            return items.GetValueOrDefault($"Account-{p.HumanID}")?.Body?["Email"]?.GetValue<string>()
                ?? items.Values.FirstOrDefault(i => i.DataType == "MMM-PPR-V2.2" && i.Holder == p.HumanID)?.Body?["PersonalProfile"]?["Data"]?[0]?["Email"]?.GetValue<string>();
        }
    }

    // How a User's Account Level is called: an Officer whose organisation is MPAI is an MPAI Officer.
    public string? LevelNameOf(string user)
    {
        lock (gate)
        {
            var level = accountLevels.GetValueOrDefault(user);
            if (level == Manager) return "MMM Manager";
            if (level != "Officer" || !processes.TryGetValue(user, out var p)) return level;
            var org = items.Values.FirstOrDefault(i => i.DataType == "MMM-PPR-V2.2" && i.Holder == p.HumanID)?.Body?["PersonalProfile"]?["Data"]?[0]?["Organisation"]?.GetValue<string>();
            return string.Equals(org?.Trim(), "MPAI", StringComparison.OrdinalIgnoreCase) ? "MPAI Officer" : "Officer";
        }
    }

    // What a User's own client is told when it comes with its token: its Persona and where it stands (as Resume, without a new token).
    public JsonObject? InfoOf(string user)
    {
        lock (gate)
        {
            if (!processes.TryGetValue(user, out var p) || p.Kind != "User") return null;
            var persona = items.Values.FirstOrDefault(i => i.Holder == user && i.DataType == "OSD-3DO-V1.5");
            var info = new JsonObject { ["User"] = user, ["AccountLevel"] = accountLevels.GetValueOrDefault(user) };
            var levelName = accountLevels.GetValueOrDefault(user);
            if (levelName == "Officer" && items.Values.FirstOrDefault(i => i.DataType == "MMM-PPR-V2.2" && i.Holder == p.HumanID)?.Body?["PersonalProfile"]?["Data"]?[0]?["Organisation"]?.GetValue<string>() is { } org
                && string.Equals(org.Trim(), "MPAI", StringComparison.OrdinalIgnoreCase)) levelName = "MPAI Officer";
            if (levelName == Manager) levelName = "MMM Manager";
            info["AccountLevelName"] = levelName;
            if (persona is not null)
            {
                info["Persona"] = persona.ID; info["Model"] = persona.Model; info["Location"] = persona.Location;
                if (persona.SpatialAttitude?["Position"]?["CartPosition"] is JsonArray a && a.Count >= 2) { info["X"] = a[0]?.GetValue<double>(); info["Y"] = a[1]?.GetValue<double>(); }
                info["Yaw"] = YawOf(persona);
            }
            // THE OTHER USER OF THE SAME HUMAN (2026-11-27): the client of the H-User is told of the A-User (and the other way round), with its Persona and where it stands.
            if (p.Autonomous) info["Autonomous"] = true;
            if (p.HumanID is { } hid && aUserNever.Contains(hid)) info["AUserNever"] = true;
            if (p.HumanID is { } rid && officerRequests.TryGetValue(rid, out var orq) && (orq.Status == "Pending" || !orq.Told))
                info["OfficerRequest"] = new JsonObject { ["Status"] = orq.Status, ["Reason"] = orq.Reason, ["Asked"] = orq.Asked };
            if (processes.Values.FirstOrDefault(q => q.Kind == "User" && q.HumanID == p.HumanID && q.ID != user && q.Autonomous != p.Autonomous) is { } other)
            {
                var op = items.Values.FirstOrDefault(i => i.Holder == other.ID && i.DataType == "OSD-3DO-V1.5");
                var o = new JsonObject { ["User"] = other.ID, ["Persona"] = op?.ID, ["Model"] = op?.Model, ["Location"] = op?.Location };
                if (op?.SpatialAttitude?["Position"]?["CartPosition"] is JsonArray oa && oa.Count >= 2) { o["X"] = oa[0]?.GetValue<double>(); o["Y"] = oa[1]?.GetValue<double>(); }
                info[other.Autonomous ? "AUser" : "HUser"] = o;
            }
            return info;
        }
    }

    // ---------------------------------------------------------------- WHAT IT KEEPS FROM ONE RUN TO THE NEXT (2026-11-26)
    // An M-Instance starts once; when it closes and starts again it goes on from where it was: the Users that registered (their Accounts, Account Levels and Personae), where
    // each Persona is, the Items the Processes placed and their Item Properties, the Rights, the Messages to the Users, the Process Actions the Rights name, the log of the activity.
    // What is not kept: what was momentary (speech and sound on their way, a shared screen, Data captured and not identified, a walk or an animation in progress: the Item is where
    // it was going), the tokens (a User that comes back is given a new one), and what the Services were handling (their inboxes: a request is not handled twice). The Locations
    // are those of the world the M-Instance starts with: a new building is seen at once.
    private static readonly HashSet<string> Momentary = new(StringComparer.Ordinal) { "OSD-BSO-V1.5", "OSD-AUO-V1.5", "OSD-BAO-V1.5", ScreenHeader, "PAF-AMO-V1.6", "MMM-DAT", "OSD-STM-V1.5" };
    private static readonly System.Text.Json.JsonSerializerOptions StateJson = new()
    {
        IncludeFields = true, PreferredObjectCreationHandling = System.Text.Json.Serialization.JsonObjectCreationHandling.Populate
    };
    private const int KeptActivity = 5000;

    public JsonObject SaveState()
    {
        lock (gate)
        {
            var kept = items.Values.Where(i => !Momentary.Contains(i.DataType) && !i.Streaming && i.Qualifier is null).ToList();
            return new JsonObject
            {
                ["Kept"] = "The state of an M-Instance between its runs", ["Version"] = 1, ["MInstanceID"] = ID, ["Saved"] = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
                ["Items"] = System.Text.Json.JsonSerializer.SerializeToNode(kept, StateJson),
                ["Processes"] = new JsonArray(processes.Values.Where(p => p.Kind != "Service").Select(p => (JsonNode)new JsonObject { ["ID"] = p.ID, ["Kind"] = p.Kind, ["HumanID"] = p.HumanID, ["Autonomous"] = p.Autonomous ? true : null }).ToArray()),
                ["Holdings"] = System.Text.Json.JsonSerializer.SerializeToNode(holdings),
                ["Inboxes"] = System.Text.Json.JsonSerializer.SerializeToNode(inboxes.Where(kv => processes.GetValueOrDefault(kv.Key)?.Kind != "Service").ToDictionary(kv => kv.Key, kv => kv.Value)),
                ["Models"] = System.Text.Json.JsonSerializer.SerializeToNode(models),
                ["AccountLevels"] = System.Text.Json.JsonSerializer.SerializeToNode(accountLevels),
                ["Registered"] = registered,
                ["Credentials"] = System.Text.Json.JsonSerializer.SerializeToNode(credentials),
                ["Devices"] = System.Text.Json.JsonSerializer.SerializeToNode(devices),
                ["OfficerRequests"] = System.Text.Json.JsonSerializer.SerializeToNode(officerRequests.Values.ToList()),
                ["AUserNever"] = new JsonArray(aUserNever.Select(h => (JsonNode)JsonValue.Create(h)!).ToArray()),
                ["Activity"] = System.Text.Json.JsonSerializer.SerializeToNode(activity.Skip(Math.Max(0, activity.Count - KeptActivity)).ToList())
            };
        }
    }

    // The state of the last run, over the M-Instance as it was just set up (its world, its Services). Returns what was restored, in words.
    public string RestoreState(JsonObject state)
    {
        lock (gate)
        {
            var restored = 0;
            // a state kept before 2026-11-27 names the Process holding the Rights to an Item its "Owner": the MMM has no owners, only Rights holders
            foreach (var o in (state["Items"] as JsonArray ?? []).OfType<JsonObject>())
                if (o["Holder"] is null && o["Owner"] is { } old) { o.Remove("Owner"); o["Holder"] = old.DeepClone(); }
            foreach (var it in state["Items"]?.Deserialize<List<Item>>(StateJson) ?? [])
            {
                if (Momentary.Contains(it.DataType) || it.Streaming || it.Qualifier is not null) continue;
                if (items.TryGetValue(it.ID, out var now) && now.DataType is "MMM-LOC" or "MMM-ULC") continue;   // the world's Locations as the world now is
                it.Animation = null; it.WalkFrom = null; it.WalkTo = null; it.WalkPath = null; it.WalkStart = 0; it.WalkEnd = 0; it.AnimatedUntil = 0;
                items[it.ID] = it; restored++;
            }
            var users = 0;
            foreach (var p in (state["Processes"] as JsonArray ?? []).OfType<JsonObject>())
                if ((string?)p["ID"] is { } id && !processes.ContainsKey(id))
                { processes[id] = new Process { ID = id, Kind = (string?)p["Kind"] ?? "User", HumanID = (string?)p["HumanID"], Autonomous = (bool?)p["Autonomous"] ?? false }; users++; }
            foreach (var (k, v) in state["Holdings"]?.Deserialize<Dictionary<string, List<string>>>() ?? []) holdings[k] = v;
            foreach (var (k, v) in state["Inboxes"]?.Deserialize<Dictionary<string, List<string>>>() ?? [])
                if (processes.GetValueOrDefault(k)?.Kind != "Service") inboxes[k] = v.Where(items.ContainsKey).ToList();
            foreach (var (k, v) in state["Models"]?.Deserialize<Dictionary<string, string>>() ?? []) models[k] = v;
            foreach (var (k, v) in state["AccountLevels"]?.Deserialize<Dictionary<string, string>>() ?? []) accountLevels[k] = v;
            registered = Math.Max(registered, (int?)state["Registered"] ?? 0);
            foreach (var (k, v) in state["Credentials"]?.Deserialize<Dictionary<string, string>>() ?? []) credentials[k] = v;
            foreach (var (k, v) in state["Devices"]?.Deserialize<Dictionary<string, List<LinkedDevice>>>() ?? []) devices[k] = v;
            foreach (var r in state["OfficerRequests"]?.Deserialize<List<OfficerRequest>>() ?? []) officerRequests[r.Human] = r;
            foreach (var h in state["AUserNever"] as JsonArray ?? []) if ((string?)h is { } hn) aUserNever.Add(hn);
            activity.InsertRange(0, state["Activity"]?.Deserialize<List<Activity>>() ?? []);
            return $"{users} Users, {restored} Items and {holdings.Count} holdings of Rights of the run that closed at {(string?)state["Saved"]}";
        }
    }
}
