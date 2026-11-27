using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using AIF.Controller;
using AIF.Store;
using Mpai.Aif.Api;
using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Mmm.Client;
using Mpai.Pgm;

namespace Mpai.Mmm.Server;

// THE A-USER IN THE WORLD (the author, 2026-11-27: "build the A-User Module so it takes commands"). A registered human may deploy an A-User: a User of its own, commanded by text or
// speech, that acts with autonomy. This is its User Agent: it runs the A-User Module (1PGM-ATU-V1.0-I01, the PGM-AUA AIMs) on an AIF Controller, writes to it the human's
// command (the Human Command and its words) and what the A-User's Persona hears, and performs the Process Action Requests of the Module on the M-Instance through the MMM-API,
// as the A-User (its own token) - a walk is MM-Animate At the M-Location With the Portable Avatar the Module formed - writing back their Responses. What the A-User says goes to
// its human's page (the words and the speech). It is the door demo's Host (DoorUseCase), for any A-User of the world: the places it knows are the world's.
public sealed class AUserAgent
{
    public const string Module = "1PGM-ATU-V1.0-I01";
    private static readonly string[] ModuleAims =
    [
        "1PGM-AUC-V1.0-I01", "1PGM-CXT-V1.0-I01", "1PGM-PRC-V1.0-I01", "1PGM-BKN-V1.0-I01", "1PGM-DAC-V1.0-I01", "1PGM-USR-V1.0-I01", "1PGM-PAL-V1.0-I01", "1PGM-AUF-V1.0-I01",
        "1PAF-PDX-V1.6-I01", Module, "1PGM-PSD-V1.0-I01", "1PGM-FRD-V1.0-I01", "1PGM-BPL-V1.0-I01", "1PGM-AOG-V1.0-I01", "1PGM-EFD-V1.0-I01", "1PGM-EBD-V1.0-I01", "1PGM-ASD-V1.0-I01"
    ];

    public string User { get; }
    public string Persona { get; }
    private readonly MInstance m;
    private readonly MmmClient me;
    private readonly IReadOnlyDictionary<string, string> places;      // the name of a place, as the human says it -> its M-Location
    private readonly IReadOnlyDictionary<string, string> doors;       // the name of a door, as the human says it -> the Item (2026-11-27: the doors are Items)
    private readonly ILanguageModel model;
    private readonly ISpeechSynthesiser voice;
    private readonly string repo;
    private ControllerApi api = null!;
    private int moduleId, commands, acceptedOf;
    private readonly SemaphoreSlim toModule = new(1, 1);
    private readonly ConcurrentDictionary<string, JsonObject> avatars = new(StringComparer.Ordinal);
    private readonly HashSet<string> heard = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource stop = new();
    private Provider? provider;

    // WHAT THE HUMAN IS TOLD: the A-User's words and how its commands go, numbered, for the page (it asks for those after the last it has).
    public sealed record Entry(int N, string Time, string Who, string Text, string? Speech);
    private readonly List<Entry> log = [];
    public IReadOnlyList<Entry> Since(int n) { lock (log) return log.Where(e => e.N > n).ToList(); }
    public volatile bool Ready;
    private void Say(string who, string text, string? speech = null)
    {
        lock (log) { log.Add(new Entry(log.Count + 1, DateTime.Now.ToString("HH:mm:ss"), who, text, speech)); if (log.Count > 400) log.RemoveRange(0, 100); }
        Console.WriteLine($"A-User {User} - {who}: {text}");
    }

    public AUserAgent(MInstance m, HttpClient inside, string user, string persona, IReadOnlyDictionary<string, string> places, IReadOnlyDictionary<string, string> doors, ILanguageModel model, ISpeechSynthesiser voice, string repo)
    {
        this.m = m; User = user; Persona = persona; this.places = places; this.doors = doors; this.model = model; this.voice = voice; this.repo = repo;
        me = new MmmClient(inside, user, m.Resume(user)?.Token);
    }

    // THE A-USER MODULE, on a Controller of its own: the L3s of the repository with the settings of this A-User - its Persona, and the places of the world it may go to.
    public void Start()
    {
        var folder = Path.Combine(Path.GetTempPath(), "mmm-a-user", $"{User}-{Environment.ProcessId}");
        Directory.CreateDirectory(folder);
        foreach (var aim in ModuleAims) File.Copy(Path.Combine(repo, "AIMs", "AMDs", aim + ".json"), Path.Combine(folder, aim + ".json"), overwrite: true);
        var settings = new JsonObject
        {
            ["1PGM-PRC-V1.0-I01"] = new JsonObject { [PrcAim.ItemsSetting] = doors.Count > 0 ? string.Join(",", doors.Keys) : " ", [PrcAim.MLocationsSetting] = string.Join(",", places.Keys) },
            ["1PGM-BKN-V1.0-I01"] = new JsonObject { [BknAim.PersonaSetting] = Persona, [BknAim.LevelSetting] = "1" },
            ["1PGM-AUC-V1.0-I01"] = new JsonObject { [AucAim.LevelSetting] = "1", [BknAim.PersonaSetting] = Persona },
            ["1PGM-AUF-V1.0-I01"] = new JsonObject { ["Avatar.Id"] = Persona, [BknAim.PersonaSetting] = Persona }
        };
        File.WriteAllText(Path.Combine(folder, "settings.json"), settings.ToJsonString());
        api = new ControllerApi(folder, Path.Combine(folder, "settings.json"), store => provider = new Provider(store, model, voice));
        var started = api.StartFlow(Module, out moduleId);
        Say("A-User", started == AifError.OK ? "I am ready: tell me what to do." : $"my Module did not start ({started}).");
        Ready = started == AifError.OK;
        foreach (var type in new[] { "PGM-HCM-V1.0", "MMM-ACT-V2.2", "MMM-PAC-V2.2", "PAF-PAV-V1.6" })
            _ = Task.Run(() => ReadLoopAsync(type));
        _ = Task.Run(ListenAsync);
    }

    public void Stop()
    {
        stop.Cancel();
        try { api.StopFlow(moduleId); } catch { }
    }

    // The Module's provider: the PGM-AUA AIMs, with the language model, the voice, and PAF-PDX.
    private sealed class Provider(AmdStore store, ILanguageModel model, ISpeechSynthesiser voice) : IAimProvider
    {
        private readonly PgmProvider pgm = new(store, model, null, voice);
        public readonly ConcurrentDictionary<string, IAimProcessor> Made = new();
        public bool CanCreate(string aimName) => pgm.CanCreate(aimName) || aimName == "1PAF-PDX-V1.6-I01";
        public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings, AIF.SharedStorage.ISharedStorage? storage) =>
            Made[aimName] = aimName == "1PAF-PDX-V1.6-I01" ? new Mpai.Paf.Pdx.PdxAimProcessor(aimName, AimPortReader.Load(store, aimName)) : pgm.Make(aimName, settings);
    }

    private async Task WriteAsync(string dataType, JsonNode data)
    {
        await toModule.WaitAsync();
        try { var e = await Task.Run(() => api.InputWrite(moduleId, dataType, 1, data.ToJsonString(), 10000)); if (e != AifError.OK) Console.WriteLine($"A-User {User}: write {dataType}: {e}"); }
        finally { toModule.Release(); }
    }

    // The human's command: the Human Command (PGM-HCM, instruct) and its words (a Basic Text Object).
    public async Task CommandAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        text = text.Trim();
        Say("You", text);
        var hc = new JsonObject
        {
            ["Header"] = "PGM-HCM-V1.0", ["Mode"] = "RQ", ["MInstance"] = m.ID, ["UEnvironment"] = "ULocHuman", ["SessionID"] = $"{User}-session",
            ["HumanCommandID"] = $"hc-{++commands}", ["HumanCommandTime"] = PgmAim.Time(), ["CommandType"] = "instruct", ["CommandIntent"] = text
        };
        await WriteAsync("PGM-HCM-V1.0", hc);
        await WriteAsync("OSD-BTO-V1.5", PgmAim.Text(text));
    }

    // Where the A-User's Persona is: the M-Location it stands in.
    private string? Where() => (string?)m.InfoOf(User)?["Location"];

    // WHAT THE PERSONA HEARS: each new sound where it stands (an Audio Object of its M-Location, as MM-Capture gives it) goes to the Module - a bell, a knock - for a command that
    // waits for one ("when the bell rings, ...").
    private async Task ListenAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                if (Where() is { } at)
                {
                    var r = await me.PerformAsync("MM-Capture", Uc2.C(at: at, with: Uc2.PointOfView("PoV-" + User)));
                    var things = r.Response.Complements?.With?.OfType<JsonObject>().FirstOrDefault()?["ItemValueOrRef"]?[0]?["Things"]?.AsArray().OfType<JsonObject>() ?? [];
                    foreach (var t in things.Where(t => (string?)t["Kind"] == "Audio" && heard.Add((string)t["ID"]!)))
                    {
                        var (_, item) = await me.ReadAsync((string)t["ID"]!);
                        if ((item?["ItemValueOrRef"]?[0]?["BasicAudioObjects"]?[0]?["BAObjectIDOrBAObject"]?[0] as JsonObject)?.DeepClone() is { } bao) await WriteAsync("OSD-BAO-V1.5", bao);
                    }
                }
            }
            catch (Exception e) { Console.WriteLine($"A-User {User}: listening: {e.Message}"); }
            await Task.Delay(500);
        }
    }

    private async Task ReadLoopAsync(string type)
    {
        while (!stop.IsCancellationRequested)
        {
            var read = await Task.Run(() => api.OutputRead(moduleId, type, 1, 1000));
            if (read.Error != AifError.OK && read.Error != AifError.Timeout) { await Task.Delay(200); continue; }
            if (read.Error != AifError.OK || read.Json is null) continue;
            try { await OnOutputAsync(type, JsonNode.Parse(read.Json)!.AsObject()); }
            catch (Exception e) { Console.WriteLine($"A-User {User}: {type}: {e.Message}"); }
        }
    }

    private async Task OnOutputAsync(string type, JsonObject data)
    {
        switch (type)
        {
            case "PGM-HCM-V1.0":
                if ((string?)data["ExecutionResult"] is { } result && acceptedOf != commands)
                {
                    acceptedOf = commands;
                    if (result != "accepted") Say("A-User", $"I cannot do that ({result}).");
                }
                break;
            case "MMM-PAC-V2.2":
                await PerformAsync(data);
                break;
            case "PAF-PAV-V1.6":
                avatars[(string?)data["PortableAvatarID"] ?? ""] = data;
                // what it says: the words A-User Formation formed (its report), with the speech of the Portable Avatar for the human's page
                if (data["SPOOrSPOID"] is not null && (provider?.Made.GetValueOrDefault("1PGM-AUF-V1.0-I01") as AufAim)?.Trace.LastOrDefault() is { } formed
                    && formed.IndexOf("speech \"", StringComparison.Ordinal) is var q and >= 0 && formed.IndexOf('"', q + 8) is var e and > 0)
                    Say("A-User", formed[(q + 8)..e], (string?)data["PortableAvatarID"]);
                break;
        }
    }

    // The speech of a Portable Avatar the A-User formed, as a WAV file (for its human's page).
    public byte[]? SpeechOf(string avatarId) => avatars.TryGetValue(avatarId, out var a) && DoorUseCase.PcmOf(a) is { } pcm ? DoorUseCase.Wav(pcm) : null;

    // A Process Action Request of the Module, performed on the M-Instance as the A-User; its Response written back to the Module.
    private async Task PerformAsync(JsonObject pac)
    {
        var kind = (string?)pac["DescrMetadata"] ?? "";
        var rq = pac["RQ-Complements"] as JsonArray ?? [];
        string? Of(string key) => rq.OfType<JsonObject>().Select(o => (string?)o[key]).FirstOrDefault(v => v is not null);
        var with = rq.OfType<JsonObject>().Select(o => o["Item"]).OfType<JsonObject>().Select(i => (JsonNode)i.DeepClone()).ToArray();
        MmmClient.Result r;
        switch (kind)
        {
            case "MM-Animate":
                var stream = Of("WithItem") ?? "";
                JsonObject? avatar = null;
                for (var i = 0; i < 50 && !avatars.TryGetValue(stream, out avatar); i++) await Task.Delay(100);
                if (avatar is null) return;
                // the place as the Module names it (the name the human says) is the M-Location of that name
                var at = Of("AtItem") is { } place ? places.GetValueOrDefault(place, place) : null;
                // A CLOSED DOOR ON THE WAY is opened first, as the A-User may (2026-11-27: the doors are Items; closed, a door stops a Persona)
                if (at is not null)
                    foreach (var door in m.ClosedBetween(Where(), at))
                    {
                        var opened = await me.PerformAsync("Property Change", Uc2.C(door, with: [OpenState(door, "Open")]));
                        Say("A-User", opened.Http is >= 200 and < 300 ? $"opening {NameOfDoor(door)}" : $"{NameOfDoor(door)} is closed and I may not open it");
                    }
                r = await me.PerformAsync("MM-Animate", Uc2.C(Of("NilItem") ?? Persona, at: at, with: [avatar.DeepClone()]));
                if (r.Http is >= 200 and < 300) { if (at is not null) Say("A-User", $"walking to {Of("AtItem")}"); await AnimatedAsync(Of("NilItem") ?? Persona); }
                else Say("A-User", $"I could not {(at is null ? "do it" : "go to " + Of("AtItem"))}: {r.Response.PaStatus.Detail ?? r.Response.PaStatus.Code}");
                break;
            case "Property Change":
                // the Item as the Module names it (a door, by the name the human says) is the Item of that name
                var item = Of("NilItem") is { } named ? doors.GetValueOrDefault(named, named) : null;
                foreach (var w in with.OfType<JsonObject>().Where(w => (string?)w["Header"] == "MMM-IPR-V3.0")) w["ItemID"] = item;
                r = await me.PerformAsync("Property Change", Uc2.C(item, with: with));
                Say("A-User", r.Http is >= 200 and < 300 ? $"{Of("NilItem")}: done" : $"I could not change {Of("NilItem")}: {r.Response.PaStatus.Detail ?? r.Response.PaStatus.Code}");
                break;
            default:
                return;
        }
        var ok = r.Http is >= 200 and < 300;
        await WriteAsync("MMM-PAC-V2.2", new JsonObject
        {
            ["Header"] = "MMM-PAC-V2.2", ["MInstanceID"] = m.ID, ["ProcessActionID"] = (string?)pac["ProcessActionID"] ?? "", ["ProcessActionTime"] = PgmAim.Time(),
            ["RQ-Complements"] = rq.DeepClone(),
            ["PAStatus"] = new JsonArray(ok ? new JsonObject { ["Ack"] = "Ack" } : new JsonObject { ["Error"] = r.Response.PaStatus.Code is "Clash" or "IncID" or "InsRights" or "LocOOR" ? r.Response.PaStatus.Code : "FaultyReq" })
        });
    }

    private static JsonObject OpenState(string item, string value) =>
        new() { ["Header"] = "MMM-IPR-V3.0", ["ItemID"] = item, ["Properties"] = new JsonArray(new JsonObject { ["Name"] = "OpenState", ["Value"] = value }) };
    private string NameOfDoor(string id) => doors.FirstOrDefault(d => d.Value == id).Key ?? id;

    // Until the M-Instance says that the animation of the Item is over (its walk, its speech).
    private async Task AnimatedAsync(string item)
    {
        for (var i = 0; i < 900 && !stop.IsCancellationRequested; i++)
        {
            await Task.Delay(200);
            if (Where() is not { } at) return;
            var cap = await me.PerformAsync("MM-Capture", Uc2.C(at: at, with: Uc2.PointOfView("PoV-" + User)));
            var things = cap.Response.Complements?.With?.OfType<JsonObject>().FirstOrDefault()?["ItemValueOrRef"]?[0]?["Things"]?.AsArray().OfType<JsonObject>() ?? [];
            var t = things.FirstOrDefault(x => (string?)x["ID"] == item);
            if (t is null || (bool?)t["Animating"] != true) return;
        }
    }
}

// THE A-USERS OF THE WORLD: each A-User whose Persona is in the world has its agent, started when the Persona is placed (at its deployment, or when the world starts again) and
// stopped when the A-User is no more (a Visitor's Account removed).
public sealed class AUserAgents(MInstance m, HttpClient inside, IReadOnlyDictionary<string, string> places, IReadOnlyDictionary<string, string> doors, ILanguageModel model, ISpeechSynthesiser voice, string repo)
{
    private readonly ConcurrentDictionary<string, AUserAgent> agents = new(StringComparer.Ordinal);
    public AUserAgent? Of(string aUser) => agents.GetValueOrDefault(aUser);

    public async Task KeepAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                var now = m.AUsers();
                foreach (var (user, persona) in now)
                    if (!agents.ContainsKey(user) && m.InfoOf(user)?["Location"] is not null)
                    {
                        var a = new AUserAgent(m, inside, user, persona, places, doors, model, voice, repo);
                        agents[user] = a;
                        _ = Task.Run(a.Start);
                    }
                foreach (var gone in agents.Keys.Except(now.Select(x => x.User)).ToList())
                    if (agents.TryRemove(gone, out var a)) a.Stop();
            }
            catch (Exception e) { Console.Error.WriteLine("A-Users: " + e.Message); }
            await Task.Delay(3000, stopping).ContinueWith(_ => { });
        }
    }

    // The doors of the world, by the names a human says (world.json): Items of the M-Instance (2026-11-27).
    public static Dictionary<string, string> DoorsOf(string repo)
    {
        var doors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var w = JsonNode.Parse(File.ReadAllText(Path.Combine(repo, "AllApps", "MMM", "Viewer", "world", "world.json")))!;
        foreach (var b in w["buildings"]?.AsArray() ?? [])
            foreach (var d in (b?["doors"] as JsonArray ?? []).Concat((b?["rooms"]?.AsArray() ?? []).Select(r => r?["door"])).OfType<JsonObject>())
                if ((string?)d["id"] is { } id) doors.TryAdd(((string?)d["name"] ?? id).Replace(",", ""), id);
        return doors;
    }

    // The places of the world, by the names a human says: the square, the buildings and their rooms (world.json).
    public static Dictionary<string, string> PlacesOf(string repo)
    {
        var places = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var w = JsonNode.Parse(File.ReadAllText(Path.Combine(repo, "AllApps", "MMM", "Viewer", "world", "world.json")))!;
        void Add(JsonNode? n) { if ((string?)n?["id"] is { } id) places.TryAdd(((string?)n["name"] ?? id).Replace(",", ""), id); }
        Add(w["square"]);
        foreach (var b in w["buildings"]?.AsArray() ?? []) { Add(b); foreach (var r in b?["rooms"]?.AsArray() ?? []) Add(r); }
        return places;
    }

    // The voice of A-User Formation: the Text-To-Speech of MMC (Piper), as a Speech Object whose data is a WAV file (16 kHz, 16 bit, mono).
    public sealed class Voice(SummaryServices tts) : ISpeechSynthesiser
    {
        public async Task<JsonObject> SynthesiseAsync(string text)
        {
            var pcm = await tts.SynthesiseAsync(text, "en");
            var speech = BasicSpeechObject.FromData(DoorUseCase.Wav(pcm), new SpeechQualifier
            {
                SpeechQualifierID = PgmAim.NewId("sq"),
                Format = new SpeechFormat
                {
                    ContentFormats = new SpeechContentFormats { RawData = new Pcm { SamplingFrequency = 16000, Precision = 16 } },
                    TransportFormats = new SpeechTransportFormats { FileFormat = SpeechFileFormat.Wav }
                }
            });
            return (JsonObject)JsonNode.Parse(MpaiJson.ToJson(speech))!;
        }
    }
}
