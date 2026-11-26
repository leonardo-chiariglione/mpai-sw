using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using AIF.Controller;
using AIF.Store;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Mpai.Aif.Api;
using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Mmm.Client;
using Mpai.Pgm;

namespace Mpai.Mmm.Server;

// "OPEN THE DOOR WHEN THE BELL RINGS" (M3313), WITH THE A-USER OF PGM-AUA. A room with one door; the human of the Host User gives a spoken or written command and sees and
// hears the room (the room and its sound rendered to the human: the only MU-Actuate); the Persona of another User (the Visitor) comes to the door and rings the bell; the
// Host's Persona walks to the door; the door opens (Property Change, Item Property OpenState); the Persona says "welcome". Everything else is MM.
// A PERSONA WALKS WITH MM-ANIMATE (author, 2026-11-16: a walk with MM-Move would be a very long list of MM-Moves): MM-Animate of the Persona, At the M-Location it goes to,
// With its Stream - for the A-User, the Portable Avatar that A-User Formation formed. The M-Instance takes the Persona there along the walk and says when it is over.
//
// The Host User is an A-User App: its User Agent (this class, the Host part) and the A-User Module (1PGM-ATU-V1.0-I01) that the AIF Controller runs, through the Controller
// API only. The User Agent writes to the Module the Human Command and the words, and the sounds its Persona hears in the room (each Audio Object of an MM-Capture of the room,
// as a Basic Audio Object); it reads from the Module the Human Command Responses, the Actions, the Portable Avatars and the Process Action Requests, which it performs on the
// M-Instance through the MMM-API (MM-Animate With the Portable Avatar named as its Stream) and whose Responses it writes back - for an MM-Animate, when the M-Instance says the
// animation is over (the Persona has reached the place, the speech is said).
// In the Module: Basic Knowledge reads the command with a language model and finds its Condition; A-User Control keeps the goal pending and asks Context Description to report
// the event; Context Description recognises the bell in the Basic Audio Object; A-User Control asks Basic Knowledge again, the Condition held; A-User Formation forms the walk;
// A-User Control requests MM-Animate (walk), then Property Change (door), then has the speech formed and requests MM-Animate (speech).
// What is a stand-in: the walk is a straight line at WalkSpeed (the M-Instance's reading of the Stream); the wave of the Greeting is drawn by the page while the Persona speaks.
public sealed class DoorUseCase
{
    public const string Room = "Room", Doorway = "Doorway", Porch = "Porch", Hall = "Hall", Street = "Street", Door = "Door", Host = "Host", Visitor = "Visitor", HostPersona = "HostPersona", VisitorPersona = "VisitorPersona", Human = "ULocHuman";
    public const string Module = "1PGM-ATU-V1.0-I01";
    public const double WalkSpeed = 1.0;            // metres a second: the page draws the walk at this speed
    public const double RoomSize = 6;               // the room is 6 m by 6 m, its centre at 0,0; the door in the south wall, at 0,-3
    private static readonly double[] DoorAt = [0, -3], HostStart = [1.6, 1.4], DoorwayAt = [0, -2.2], StreetCentre = [0, -7.5], VisitorStart = [3.5, -9.5], OutsideDoor = [0, -3.8], VisitorIn = [0.6, -1.6];
    private static readonly string[] ModuleAims =
    [
        "1PGM-AUC-V1.0-I01", "1PGM-CXT-V1.0-I01", "1PGM-PRC-V1.0-I01", "1PGM-BKN-V1.0-I01", "1PGM-DAC-V1.0-I01", "1PGM-USR-V1.0-I01", "1PGM-PAL-V1.0-I01", "1PGM-AUF-V1.0-I01",
        "1PAF-PDX-V1.6-I01", Module, "1PGM-PSD-V1.0-I01", "1PGM-FRD-V1.0-I01", "1PGM-BPL-V1.0-I01", "1PGM-AOG-V1.0-I01", "1PGM-EFD-V1.0-I01", "1PGM-EBD-V1.0-I01", "1PGM-ASD-V1.0-I01"
    ];

    private static string MI = "MI1";
    private readonly MInstance m;
    private readonly HttpClient http;
    private readonly SummaryServices voice;
    private readonly WhisperAsr? whisper;
    private readonly string repo, llm;
    private MmmClient host = null!, visitor = null!;
    private IControllerApi api = null!;
    private readonly string? controllerUrl;   // --controller: the A-User Module runs on that Controller (an instance of MPAI as a Service), not on one of this program
    private int moduleId;
    private readonly List<string> log = [];
    private readonly HashSet<string> heard = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, double[]> positions = new() { [HostPersona] = HostStart, [VisitorPersona] = VisitorStart };
    private readonly SemaphoreSlim toModule = new(1, 1);
    private readonly ConcurrentDictionary<string, JsonObject> avatars = new(StringComparer.Ordinal);   // the Portable Avatars of the Module, by ID: the Streams its MM-Animates name
    private int sounds, commands;
    private string? pending, lastCommand;   // the page shows the last command accepted until the Module requests a Property Change
    private int acceptedOf;                 // the command (its number) whose first acceptance was seen

    public DoorUseCase(MInstance m, HttpClient http, SummaryServices voice, WhisperAsr? whisper, string repo, string llm, string? controllerUrl = null)
    {
        this.controllerUrl = controllerUrl;
        this.m = m; this.http = http; this.voice = voice; this.whisper = whisper; this.repo = repo; this.llm = llm;
    }

    // ---------------------------------------------------------------- the M-Instance as it starts
    public static void Setup(MInstance m)
    {
        MI = m.ID;
        m.AddLocation(Street, isPublic: true, x: StreetCentre[0], y: StreetCentre[1], size: 9);
        m.AddLocation(Room, owner: Host, isPublic: true, x: 0, y: 0, size: RoomSize);
        m.AddLocation(Doorway, owner: Host, isPublic: true, x: DoorwayAt[0], y: DoorwayAt[1], size: 1, at: Room);   // the place inside the door, an M-Location in the Room
        m.AddLocation(Hall, owner: Host, isPublic: true, x: VisitorIn[0], y: VisitorIn[1], size: 1, at: Room);       // where a visitor stands, inside
        m.AddLocation(Porch, isPublic: true, x: OutsideDoor[0] - StreetCentre[0], y: OutsideDoor[1] - StreetCentre[1], size: 1, at: Street);   // outside the door
        m.AddLocation(Human, universe: true);
        PlaceItems(m);
        m.AddUser(Host, "human1");
        m.AddUser(Visitor, "human2");
    }

    // The Items as the scene starts: the door closed, each Persona at its place. Called again for a new scene (the M-Instance as it was set up, not a Process Action).
    private static void PlaceItems(MInstance m)
    {
        // The door is the Host's: an Item (a 3D Model Object) in the south wall, with its Item Properties.
        m.AddItem(Door, "OSD-3DO-V1.5", Host, Room, DoorAt[0], DoorAt[1], yaw: 0, kind: "Door", ("OpenState", JsonValue.Create("Closed")), ("LockState", JsonValue.Create("Unlocked")));
        m.AddItem(HostPersona, "OSD-3DO-V1.5", Host, Room, HostStart[0], HostStart[1], yaw: 160);
        m.AddItem(VisitorPersona, "OSD-3DO-V1.5", Visitor, Street, VisitorStart[0] - StreetCentre[0], VisitorStart[1] - StreetCentre[1], yaw: 0);
        m.SetModel(HostPersona, "Persona1.glb");
        m.SetModel(VisitorPersona, "Persona2.glb");
        // A table with two stools in the middle of the room, between the Host's Persona and the door: its walk goes round it (the M-Instance computes the way).
        m.AddItem(Table, "OSD-3DO-V1.5", Host, Room, TableAt[0], TableAt[1], yaw: 0, kind: "Table");
        m.SetFootprint(Table, 1.8, 0.7);
    }
    public const string Table = "Table";
    private static readonly double[] TableAt = [0.8, -0.4];

    // dotnet run --project AllApps/MMM/Server -- [url] --door [--llm llama3.2:3b] [--controller <MPAI as a Service>]: the M-Instance of the door, its page at <url>/viewer/door.html.
    public static async Task<int> RunAsync(MInstance m, string url, string repo, string[] args)
    {
        Setup(m);
        var app = MmmServer.Build(m, url, Path.Combine(repo, "AllApps", "MMM", "Viewer"));
        using var voice = new SummaryServices(repo);
        var llm = Array.IndexOf(args, "--llm") is var k and >= 0 && k + 1 < args.Length ? args[k + 1] : "llama3.2:3b";   // the language model of Basic Knowledge
        var whisper = new WhisperAsr(repo);          // started by the warm-up, AFTER the language model is loaded (N-48)
        var http = new HttpClient { BaseAddress = new Uri(url) };
        var controller = Array.IndexOf(args, "--controller") is var c and >= 0 && c + 1 < args.Length ? args[c + 1] : null;
        var door = new DoorUseCase(m, http, voice, whisper.Available ? whisper : null, repo, llm, controller);
        // The human's side (outside the MMM-API): the command to its User, written or spoken; what its User tells it; the sound of the room, rendered (MU-Actuate).
        app.MapPost("/door/command", async (HttpContext ctx) => { var text = await new StreamReader(ctx.Request.Body).ReadToEndAsync(); await door.CommandAsync(text); return Results.Ok(); });
        app.MapPost("/door/speak", async (HttpContext ctx) => { using var ms = new MemoryStream(); await ctx.Request.Body.CopyToAsync(ms); return Results.Text(await door.SpokenAsync(ms.ToArray())); });
        app.MapPost("/door/visitor", (string? sound) => { _ = Task.Run(() => door.VisitAsync(sound ?? "bell")); return Results.Ok(); });
        app.MapGet("/door/log", () => Results.Json(new
        {
            log = door.Log, pending = door.pending, walkSpeed = WalkSpeed, asr = whisper.Available && door.asrReady,
            ready = door.Ready, readiness = door.Readiness, scene = door.scene, resetting = door.resetting
        }));
        // The Face and Body Descriptors of a Portable Avatar that animates a Persona (the Stream of an MM-Animate), as the page renders it: their Data, the timed Behaviours.
        app.MapGet("/door/avatar/{id}", (string id) =>
        {
            if (m.Get(id)?.Body is not { } b) return Results.NotFound();
            var pav = b["ItemValueOrRef"]?[0] as JsonObject ?? b;
            static string[] Data(JsonNode? o, string key) => (o?[key] as JsonArray ?? []).Select(d => (string?)d?["Data"]).Where(d => d is not null).Select(d => d!).ToArray();
            return Results.Json(new { body = Data(pav["BodyDescriptorsObject"], "BodyDescriptorsData"), face = Data(pav["FaceDescriptorsObject"], "FaceDescriptorsData") });
        });
        app.MapPost("/door/reset",async () => { await door.ResetAsync(); return Results.Ok(); });   // "New scene": the door closed, the Personae at their places, the Module started again
        app.MapGet("/door/status", () => Results.Json(door.ModuleStatus()));   // the AIMs of the A-User Module, as the Controller reports them
        app.MapGet("/door/sound/{id}", (string id) => m.Get(id) is { } it && it.Body is { } body && PcmOf(body) is { } pcm ? Results.Bytes(Wav(pcm), "audio/wav") : Results.NotFound());
        await app.StartAsync();
        Console.WriteLine($"M-Instance {m.ID} on {url}; the door page at {url.TrimEnd('/')}/viewer/door.html");
        await door.StartAsync();
        await app.WaitForShutdownAsync();
        return 0;
    }

    public object ModuleStatus()
    {
        var s = api.Status(moduleId);   // over MPAI-MAS: FAILED, no AIMs (the instance does not report them yet)
        var auc = provider?.Made.GetValueOrDefault("1PGM-AUC-V1.0-I01") as AucAim;
        return new
        {
            error = s.Error.ToString(), aims = s.Aims.Select(a => new { a.Aim, status = a.Status.ToString(), a.Reason, a.Reports }),
            auc = auc is null ? null : new { requested = auc.Requested, pending = auc.PendingGoal?.ToJsonString(), auc.Outcome, speech = auc.SpeechToCome, trace = auc.Trace },
            auf = provider?.Made.GetValueOrDefault("1PGM-AUF-V1.0-I01") is AufAim auf ? new { trace = auf.Trace } : null,
            made = provider?.Made.Keys.Order().ToArray(),
            channels = (api as ControllerApi)?.ChannelAccounts(moduleId).Where(c => c.Contains("AUC") || c.Contains("AUF")).ToArray()
        };
    }

    public IReadOnlyList<string> Log { get { lock (log) return log.ToList(); } }
    private void Say(string who, string text) { lock (log) log.Add($"{DateTime.Now:HH:mm:ss} {who}: {text}"); Console.WriteLine($"{who}: {text}"); }

    // ---------------------------------------------------------------- the Host: the User Agent of the A-User App
    public async Task StartAsync()
    {
        host = new MmmClient(http, Host, m.TokenOf(Host));
        visitor = new MmmClient(http, Visitor, m.TokenOf(Visitor));
        // The room and its sound are rendered to the human of the Host: MU-Actuate (M-Instance -> Universe), the one Process Action of the use case that is not MM.
        var r = await host.PerformAsync("MU-Actuate", Uc2.C(Room, at: Human));
        Say("M-Instance", $"MU-Actuate of the Room to the human of the Host: {r.Http} {r.Response.PaStatus.Code}");

        // ON AN INSTANCE OF MPAI AS A SERVICE (2026-11-26): the A-User Module is the instance's, built with its settings and its language model; this program only asks its Controller.
        if (controllerUrl is not null)
        {
            api = new Mpai.Mas.Client.RemoteControllerApi(controllerUrl, Environment.GetEnvironmentVariable("MPAI_MAS_TOKEN"));
            var startedThere = await Task.Run(() => api.StartFlow(Module, out moduleId));
            Say("Controller", $"the A-User Module {Module} started on {controllerUrl}: {startedThere}");
            llmReady = true;   // the language model is the instance's
        }
        else
        {
        // The A-User Module, on the Controller: the L3s of the repository, copied with the settings of this App.
        var folder = Path.Combine(Path.GetTempPath(), "door-a-user-" + Environment.ProcessId);
        Directory.CreateDirectory(folder);
        foreach (var aim in ModuleAims) File.Copy(Path.Combine(repo, "AIMs", "AMDs", aim + ".json"), Path.Combine(folder, aim + ".json"), overwrite: true);
        var settings = new JsonObject
        {
            ["1PGM-PRC-V1.0-I01"] = new JsonObject { [PrcAim.ItemsSetting] = Door, [PrcAim.MLocationsSetting] = Doorway },
            ["1PGM-BKN-V1.0-I01"] = new JsonObject { [BknAim.PersonaSetting] = HostPersona, [BknAim.LevelSetting] = "1" },
            ["1PGM-AUC-V1.0-I01"] = new JsonObject { [AucAim.LevelSetting] = "1", [BknAim.PersonaSetting] = HostPersona },
            ["1PGM-AUF-V1.0-I01"] = new JsonObject { ["Avatar.Id"] = HostPersona, [BknAim.PersonaSetting] = HostPersona }
        };
        File.WriteAllText(Path.Combine(folder, "settings.json"), settings.ToJsonString());
        // The language model stays loaded for the whole demonstration (Ollama unloads an idle model after 5 minutes: the next command would wait for it to load again).
        model = new OllamaModel(llm) { KeepAlive = "3h" };
        api = new ControllerApi(folder, Path.Combine(folder, "settings.json"), store => provider = new Provider(store, model, new Voice(voice)));
        var started = api.StartFlow(Module, out moduleId);
        Say("Controller", $"the A-User Module {Module} started: {started} (Basic Knowledge with {llm})");
        }

        foreach (var type in new[] { "PGM-HCM-V1.0", "MMM-ACT-V2.2", "MMM-PAC-V2.2", "PAF-PAV-V1.6" })
            _ = Task.Run(() => ReadLoopAsync(type));
        _ = Task.Run(ListenAsync);
        _ = Task.Run(WarmUpAsync);
    }

    private OllamaModel? model;

    // READY WHEN THE PAGE OPENS (N-37 1): the language model, the voice and Whisper are loaded before the first command; the page says when the A-User is ready.
    private volatile bool llmReady, voiceReady;
    internal volatile bool asrReady;
    private volatile string? warmUpProblem;
    public bool Ready => llmReady && voiceReady && (whisper is null || asrReady);
    public string Readiness => Ready ? "The A-User is ready." + (whisper is null ? " (No speech recognition on this machine: write the command.)" : "")
        : "The A-User is getting ready: " + string.Join(", ", new[] { llmReady ? null : $"the language model ({llm})", voiceReady ? null : "the voice", whisper is null || asrReady ? null : "the speech recognition" }.Where(x => x is not null))
          + (warmUpProblem is null ? "..." : " - " + warmUpProblem);

    private async Task WarmUpAsync()
    {
        var t = DateTime.Now;
        var llmTask = Task.Run(async () =>
        {
            for (var i = 0; i < 30 && !llmReady && model is not null; i++)
                try { await model.AnswerAsync("Answer in JSON.", "Say ready.", new JsonObject { ["type"] = "object" }); llmReady = true; Say("User Agent", $"the language model {llm} is loaded ({(DateTime.Now - t).TotalSeconds:0.0} s)"); }
                catch (Exception e) { warmUpProblem = "the language model does not answer (is Ollama running?): " + e.Message; await Task.Delay(2000); }
        });
        var voiceTask = Task.Run(async () =>
        {
            try { await voice.SynthesiseAsync("Ready.", "en"); voiceReady = true; Say("User Agent", $"the voice is loaded ({(DateTime.Now - t).TotalSeconds:0.0} s)"); }
            catch (Exception e) { warmUpProblem = "the voice: " + e.Message; }
        });
        var asrTask = Task.Run(async () =>
        {
            if (whisper is null) return;
            // THE LANGUAGE MODEL FIRST, THEN WHISPER (N-48, 2026-11-24). Both use the 4 GB card. When Whisper was started first, the model loaded after it read and wrote 2.3 times slower
            // (the same 83 % of it on the card): the command was accepted in 10-16 s instead of 4-5 and the door opened 17-20 s after the bell instead of 10. Loaded first, it keeps its speed.
            await llmTask;
            whisper.Start();
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var up = false;
            for (var i = 0; i < 120 && !up; i++)
            {
                try { using var r = await probe.GetAsync("http://127.0.0.1:18179/"); up = true; }
                catch { await Task.Delay(500); }
            }
            if (up) { await whisper.RecognizeAsync(new byte[16000]); asrReady = true; Say("User Agent", $"the speech recognition is loaded ({(DateTime.Now - t).TotalSeconds:0.0} s)"); }
            else warmUpProblem = "the speech recognition (Whisper) did not start";
        });
        await Task.WhenAll(llmTask, voiceTask, asrTask);
        if (Ready) { warmUpProblem = null; Say("User Agent", "the A-User is ready: give the command"); }
    }

    // ---------------------------------------------------------------- a new scene (N-37 2)
    // The door closed, the Personae back at their places, the A-User Module stopped and started again on the Controller (nothing pending), without restarting the server.
    internal volatile int scene;
    internal volatile bool resetting;
    private readonly SemaphoreSlim resetOne = new(1, 1);
    public async Task ResetAsync()
    {
        await resetOne.WaitAsync();
        try
        {
            resetting = true;
            Interlocked.Increment(ref visit);                 // the Visitor stops waiting at the door
            await toModule.WaitAsync();
            try
            {
                api.StopFlow(moduleId);
                avatars.Clear(); pending = null; lastCommand = null;
                foreach (var (id, _) in m.List("OSD-AUO-V1.5")) m.Remove(id);   // the sounds of the last scene
                PlaceItems(m);
                var started = api.StartFlow(Module, out moduleId);
                scene++;
                Say("M-Instance", $"New scene: the door closed, the Personae at their places; the A-User Module started again: {started}");
            }
            finally { toModule.Release(); }
        }
        finally { resetting = false; resetOne.Release(); }
    }

    // The Module's provider: the PGM-AUA AIMs, with the language model, the voice, and PAF-PDX.
    private sealed class Provider(AmdStore store, ILanguageModel model, ISpeechSynthesiser voice) : IAimProvider
    {
        private readonly PgmProvider pgm = new(store, model, null, voice);
        public readonly ConcurrentDictionary<string, IAimProcessor> Made = new();   // what it made, for the status page
        public bool CanCreate(string aimName) => pgm.CanCreate(aimName) || aimName == "1PAF-PDX-V1.6-I01";
        public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings, AIF.SharedStorage.ISharedStorage? storage) =>
            Made[aimName] = aimName == "1PAF-PDX-V1.6-I01" ? new Mpai.Paf.Pdx.PdxAimProcessor(aimName, AimPortReader.Load(store, aimName)) : pgm.Make(aimName, settings);
    }
    private Provider? provider;

    // The voice of A-User Formation: the Text-To-Speech of MMC (Piper), as a Speech Object whose data is a WAV file (16 kHz, 16 bit, mono).
    private sealed class Voice(SummaryServices tts) : ISpeechSynthesiser
    {
        public async Task<JsonObject> SynthesiseAsync(string text)
        {
            var pcm = await tts.SynthesiseAsync(text, "en");
            var speech = BasicSpeechObject.FromData(Wav(pcm), new SpeechQualifier
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

    private async Task WriteAsync(string dataType, JsonNode data)
    {
        await toModule.WaitAsync();
        try { var e = await Task.Run(() => api.InputWrite(moduleId, dataType, 1, data.ToJsonString(), 10000)); if (e != AifError.OK) Say("User Agent", $"write {dataType}: {e}"); }
        finally { toModule.Release(); }
    }

    // The human's command: the Human Command (PGM-HCM, instruct) and its words (a Basic Text Object).
    public async Task CommandAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        text = text.Trim();
        Say("Human", text);
        lastCommand = text;
        var hc = new JsonObject
        {
            ["Header"] = "PGM-HCM-V1.0", ["Mode"] = "RQ", ["MInstance"] = MI, ["UEnvironment"] = Human, ["SessionID"] = "door-session",
            ["HumanCommandID"] = $"hc-{++commands}", ["HumanCommandTime"] = PgmAim.Time(), ["CommandType"] = "instruct", ["CommandIntent"] = text
        };
        await WriteAsync("PGM-HCM-V1.0", hc);
        await WriteAsync("OSD-BTO-V1.5", PgmAim.Text(text));
    }

    // A spoken command: recognised with Whisper (the ASR of the L3 of Automatic Speech Recognition) by the User Agent.
    public async Task<string> SpokenAsync(byte[] pcm)
    {
        if (whisper is null || !whisper.Available) return "";
        var text = (await whisper.RecognizeAsync(pcm)).Trim();
        if (text.Length > 0) await CommandAsync(text);
        return text;
    }

    // What the Persona hears: the User Agent MM-Captures the room from the Persona and gives the Module each new sound (the Basic Audio Object of an Audio Object).
    private async Task ListenAsync()
    {
        while (true)
        {
            try
            {
                var r = await host.PerformAsync("MM-Capture", Uc2.C(at: Room, with: Uc2.PointOfView("PoV-Host")));
                var things = r.Response.Complements?.With?.OfType<JsonObject>().FirstOrDefault()?["ItemValueOrRef"]?[0]?["Things"]?.AsArray().OfType<JsonObject>() ?? [];
                foreach (var t in things.Where(t => (string?)t["Kind"] == "Audio" && heard.Add((string)t["ID"]!)))
                {
                    var (_, item) = await host.ReadAsync((string)t["ID"]!);
                    var bao = (item?["ItemValueOrRef"]?[0]?["BasicAudioObjects"]?[0]?["BAObjectIDOrBAObject"]?[0] as JsonObject)?.DeepClone();
                    if (bao is null) continue;
                    Say("User Agent", $"the Persona hears {t["ID"]}: its Basic Audio Object goes to the A-User Module");
                    await WriteAsync("OSD-BAO-V1.5", bao);
                }
            }
            catch (Exception e) { Say("User Agent", "listening: " + e.Message); }
            await Task.Delay(300);
        }
    }

    private async Task ReadLoopAsync(string type)
    {
        while (true)
        {
            var read = await Task.Run(() => api.OutputRead(moduleId, type, 1, 1000));
            if (read.Error != AifError.OK && read.Error != AifError.Timeout) { await Task.Delay(200); continue; }   // the Module is starting again (a new scene)
            if (read.Error != AifError.OK || read.Json is null) continue;
            try { await OnOutputAsync(type, JsonNode.Parse(read.Json)!.AsObject()); }
            catch (Exception e) { Say("User Agent", $"{type}: {e.Message}"); }
        }
    }

    private async Task OnOutputAsync(string type, JsonObject data)
    {
        switch (type)
        {
            case "PGM-HCM-V1.0":
                Say("A-User Control", $"Human Command Response: {data["ExecutionResult"]}");
                // the first acceptance of a command makes it pending (A-User Control answers again as it goes on: those leave it as it is)
                if ((string?)data["ExecutionResult"] == "accepted" && acceptedOf != commands) { acceptedOf = commands; pending = lastCommand; }
                break;
            case "MMM-ACT-V2.2":
                Say("A-User Control", $"Action: {data["Action"]}");
                break;
            case "MMM-PAC-V2.2":
                await PerformAsync(data);
                break;
            case "PAF-PAV-V1.6":
                avatars[(string?)data["PortableAvatarID"] ?? ""] = data;
                Say("A-User Formation", $"Portable Avatar {data["PortableAvatarID"]}{(data["SPOOrSPOID"] is null ? " (motion)" : " (speech)")}");
                // The words of a speech, for the log and the page's speech bubble: from the report of A-User Formation (the Controller's report of the Module, as /door/status).
                if (data["SPOOrSPOID"] is not null && (provider?.Made.GetValueOrDefault("1PGM-AUF-V1.0-I01") as AufAim)?.Trace.LastOrDefault() is { } formed
                    && formed.IndexOf("speech \"", StringComparison.Ordinal) is var q and >= 0 && formed.IndexOf('"', q + 8) is var e and > 0)
                    Say("A-User", $"says \"{formed[(q + 8)..e]}\"");
                break;
        }
    }

    // A Process Action Request of the Module, performed on the M-Instance through the MMM-API; its Response written back to the Module.
    private async Task PerformAsync(JsonObject pac)
    {
        var kind = (string?)pac["DescrMetadata"] ?? "";
        var sceneOf = scene;   // a Response that comes after a new scene started is not written to the new Module
        if (kind == "Property Change") pending = null;   // the command is being done (its first words - "I will open the door when..." - leave it pending)
        var rq = pac["RQ-Complements"] as JsonArray ?? [];
        string? Of(string key) => rq.OfType<JsonObject>().Select(o => (string?)o[key]).FirstOrDefault(v => v is not null);
        var with = rq.OfType<JsonObject>().Select(o => o["Item"]).OfType<JsonObject>().Select(i => (JsonNode)i.DeepClone()).ToArray();
        MmmClient.Result r;
        switch (kind)
        {
            case "MM-Animate":
                // The Stream is a Portable Avatar of the Module: it goes to the M-Instance with the Request.
                var stream = Of("WithItem") ?? "";
                JsonObject? avatar = null;
                for (var i = 0; i < 50 && !avatars.TryGetValue(stream, out avatar); i++) await Task.Delay(100);
                if (avatar is null) { Say("User Agent", $"MM-Animate: no Portable Avatar {stream}"); return; }
                r = await host.PerformAsync("MM-Animate", Uc2.C(Of("NilItem"), at: Of("AtItem"), with: [avatar.DeepClone()]));
                Say("User Agent", $"MM-Animate of {Of("NilItem")}{(Of("AtItem") is { } at ? " At " + at : "")} With the Portable Avatar {stream}: the M-Instance answers {r.Http} {r.Response.PaStatus.Code}{(r.Response.PaStatus.Detail is { } d1 ? " (" + d1 + ")" : "")}");
                if (r.Http is >= 200 and < 300) await AnimatedAsync(host, Of("NilItem") ?? HostPersona);
                break;
            case "Property Change":
                r = await host.PerformAsync("Property Change", Uc2.C(Of("NilItem"), with: with));
                var detail = string.Join(" ", rq.OfType<JsonObject>().Select(o => o.ToJsonString()));
                Say("User Agent", $"Property Change {detail}: the M-Instance answers {r.Http} {r.Response.PaStatus.Code}{(r.Response.PaStatus.Detail is { } d2 ? " (" + d2 + ")" : "")}");
                break;
            default:
                Say("User Agent", $"{kind}: not performed by this App");
                return;
        }
        var ok = r.Http is >= 200 and < 300;
        if (sceneOf != scene) return;
        var rs = new JsonObject
        {
            ["Header"] = "MMM-PAC-V2.2", ["MInstanceID"] = MI, ["ProcessActionID"] = (string?)pac["ProcessActionID"] ?? "", ["ProcessActionTime"] = PgmAim.Time(),
            ["RQ-Complements"] = rq.DeepClone(),
            ["PAStatus"] = new JsonArray(ok ? new JsonObject { ["Ack"] = "Ack" } : new JsonObject { ["Error"] = ErrorOf(r.Response.PaStatus.Code) })
        };
        await WriteAsync("MMM-PAC-V2.2", rs);
        Say("User Agent", $"the Response of {kind} ({(ok ? "Ack" : "Error")}) goes to the A-User Module");
    }

    private static string ErrorOf(string code) => code switch
    {
        "Clash" or "IncID" or "IncDQ" or "InsRights" or "InsValue" or "LocOOR" or "PostRef" or "QualNS" => code,
        _ => "FaultyReq"
    };

    // Until the M-Instance says that the animation of the Item is over (its walk, its speech): what it captures of the Item's M-Location.
    private async Task AnimatedAsync(MmmClient who, string item, string where = Room)
    {
        for (var i = 0; i < 600; i++)
        {
            await Task.Delay(200);
            var cap = await who.PerformAsync("MM-Capture", Uc2.C(at: where, with: Uc2.PointOfView("PoV-" + who.ProcessID)));
            var things = cap.Response.Complements?.With?.OfType<JsonObject>().FirstOrDefault()?["ItemValueOrRef"]?[0]?["Things"]?.AsArray().OfType<JsonObject>() ?? [];
            var t = things.FirstOrDefault(x => (string?)x["ID"] == item);
            if (t is null || (bool?)t["Animating"] != true) { Say("M-Instance", $"the animation of {item} is over{(t is null ? "" : $" (at {t["At"]})")}"); return; }
        }
    }

    // ---------------------------------------------------------------- the Visitor
    // The Visitor's Persona walks to an M-Location: MM-Animate At it, With a Stream of its own (a Portable Avatar that only walks). Returns when the walk is over.
    private async Task<int> WalkAsync(MmmClient who, string persona, string to)
    {
        var stream = new JsonObject { ["Header"] = "PAF-PAV-V1.6", ["MInstanceID"] = MI, ["PortableAvatarID"] = $"pav-{persona}-{++sounds}" };
        var r = await who.PerformAsync("MM-Animate", Uc2.C(persona, at: to, with: [stream]));
        if (r.Http is >= 200 and < 300) await AnimatedAsync(who, persona, to == Porch ? Street : Room);
        return r.Http;
    }

    public static JsonObject Attitude(string id, double x, double y, double yaw)
    {
        var a = Uc2.SpatialAttitude(id, x, y);
        a["Orientation"]!["Orientation"] = new JsonArray(yaw, 0.0, 0.0);
        return a;
    }

    private int visit;

    public async Task VisitAsync(string sound = "bell")
    {
        var me = Interlocked.Increment(ref visit);   // a new visit (the Visitor rings after knocking) ends the wait of the earlier one
        // To the door, from the street.
        Say("Visitor", $"MM-Animate of {VisitorPersona} At the Porch: {await WalkAsync(visitor, VisitorPersona, Porch)}");
        if (me != visit) return;   // a new scene started during the walk
        // The bell: the sound of the bell at the door, an Audio Object MM-Added in the room (the bell rings inside).
        var id = $"Sound-{++sounds}";
        var pcm = sound == "knock" ? Knock() : Bell();
        var audio = new JsonObject
        {
            ["Header"] = "OSD-AUO-V1.5", ["MInstanceID"] = MI, ["AudioObjectID"] = id,
            ["AudioObjectSpaceTime"] = new JsonObject { ["Header"] = "OSD-SPT-V1.5", ["SpaceTimeID"] = id + "-ST" },
            ["BasicAudioObjects"] = new JsonArray(new JsonObject
            {
                ["BAObjectIDOrBAObject"] = new JsonArray(new JsonObject
                {
                    ["Header"] = "OSD-BAO-V1.5", ["BasicAudioObjectID"] = id + "-B",
                    ["BasicAudioObjectData"] = new JsonArray(new JsonObject { ["Data"] = Convert.ToBase64String(pcm) })
                })
            }),
            ["DescrMetadata"] = "a sound at the door, 16 kHz, 16 bit, mono PCM"
        };
        var r = await visitor.PerformAsync("MM-Add", Uc2.C(id, at: Room, with: [audio, Attitude($"SA-{id}", DoorAt[0], DoorAt[1] + 0.3, 0)]));
        Say("Visitor", $"{(sound == "knock" ? "knocks" : "rings the bell")}: MM-Add of the Audio Object {id} at the door: {r.Http} {r.Response.PaStatus.Code}");
        // The Visitor waits for the door to open (its MM-Capture of the room), then goes in; if nobody opens, it stays outside.
        var opened = false;
        for (var i = 0; i < 90 && !opened; i++)
        {
            if (me != visit) return;
            var cap = await visitor.PerformAsync("MM-Capture", Uc2.C(at: Room, with: Uc2.PointOfView("PoV-Visitor")));
            var things = cap.Response.Complements?.With?.OfType<JsonObject>().FirstOrDefault()?["ItemValueOrRef"]?[0]?["Things"]?.AsArray().OfType<JsonObject>() ?? [];
            opened = things.Any(t => (string?)t["ID"] == Door && (string?)t["Properties"]?["OpenState"] == "Open");
            if (!opened) await Task.Delay(500);
        }
        if (!opened) { if (me == visit) Say("Visitor", "nobody opened the door: the Visitor waits outside"); return; }
        await Task.Delay(4000);
        if (me != visit) return;   // a new scene started meanwhile: the Visitor is back in the street
        Say("Visitor", $"MM-Animate of {VisitorPersona} At the Hall, inside: {await WalkAsync(visitor, VisitorPersona, Hall)}");
    }

    public static byte[]? PcmOf(JsonObject anyItem)
    {
        var body = anyItem["ItemValueOrRef"]?[0] ?? anyItem;
        foreach (var b in body["BasicAudioObjects"]?.AsArray() ?? [])
            foreach (var o in b?["BAObjectIDOrBAObject"]?.AsArray() ?? [])
                foreach (var d in o?["BasicAudioObjectData"]?.AsArray() ?? [])
                    if ((string?)d?["Data"] is { } b64) return Convert.FromBase64String(b64);
        foreach (var d in body["BasicSpeechObjectData"]?.AsArray() ?? [])
            if ((string?)d?["Data"] is { } b64) return Bare(Convert.FromBase64String(b64));
        // A Portable Avatar: the speech it carries (in the wire form, or with its data as the reference software keeps it)
        if (body["SPOOrSPOID"]?[0] is JsonObject spo) return PcmOf(spo) ?? ((string?)spo["Data"] is { Length: > 0 } d64 ? Bare(Convert.FromBase64String(d64)) : null);
        return null;
    }

    // PCM, without the WAV header when there is one.
    private static byte[] Bare(byte[] data) => data.Length > 44 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' ? Speech.PcmOfWav(data) : data;

    // A doorbell: two strikes (E5, then C5), each with its partials, dying away. 16 kHz, 16 bit, mono.
    public static byte[] Bell()
    {
        const int Rate = 16000; var n = (int)(Rate * 2.4); var s = new double[n];
        foreach (var (start, f) in new[] { (0.0, 659.3), (0.75, 523.3) })
            for (var i = (int)(start * Rate); i < n; i++)
            {
                var t = (i - start * Rate) / Rate;
                s[i] += Math.Exp(-t * 2.2) * (Math.Sin(2 * Math.PI * f * t) + 0.45 * Math.Sin(2 * Math.PI * f * 2.76 * t) * Math.Exp(-t * 3) + 0.25 * Math.Sin(2 * Math.PI * f * 5.4 * t) * Math.Exp(-t * 6));
            }
        return Pcm(s, 0.35);
    }

    // A knock: three short bursts of filtered noise.
    public static byte[] Knock()
    {
        const int Rate = 16000; var n = Rate * 1; var s = new double[n]; var rnd = new Random(7); double lp = 0;
        foreach (var start in new[] { 0.05, 0.3, 0.55 })
            for (var i = 0; i < Rate * 0.06; i++) { var t = (double)i / Rate; lp = 0.7 * lp + 0.3 * (rnd.NextDouble() * 2 - 1); s[(int)(start * Rate) + i] += lp * Math.Exp(-t * 60) * 3; }
        return Pcm(s, 0.5);
    }

    private static byte[] Pcm(double[] s, double gain)
    {
        var max = s.Max(Math.Abs); var b = new byte[s.Length * 2];
        for (var i = 0; i < s.Length; i++) BitConverter.GetBytes((short)(s[i] / max * gain * 32767)).CopyTo(b, i * 2);
        return b;
    }

    public static byte[] Wav(byte[] pcm, int rate = 16000)
    {
        using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + pcm.Length); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(pcm.Length); w.Write(pcm); w.Flush();
        return ms.ToArray();
    }
}
