using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Mpai.Aif.Api;
using Mpai.Mmm;
using Mpai.Mmm.Client;
using Mpai.Mmm.Server;
using Mpai.Ua;
using Mpai.Wdl;
using Mpai.Xrv;
using Mpai.Xrv.VirtualVenue;

// THE XRV-LTP DEMO IN REAL-VIRTUAL ENVIRONMENTS (N-26, 2026-11-15; named so 2026-11-18, N-32): the Real Environment, the Theatre, and the Virtual Environment, the Square - both drawn as M-Instances.
//   1. Two M-Instances of MMM-TEC V2.2 behind their MMM-API: "Theatre" (the Real Environment) and "Square" (the Virtual Environment), their M-Locations and Processes as the
//      Process Action lists say (Test/Data/XRV/LongerEvening/ProcessActions-*.json).
//   2. The User Agent (Mpai.Ua: the workflow Apps/XRV/LTP/XRV-LTP.orch) binds three devices - the Show (the documents of the longer evening), the RealVenue (MmmVenue of the Theatre)
//      and the VirtualVenue (MmmVenue of the Square) - and deals with the Controller only through the Controller API. As in the tests, the Controller runs in this same program
//      (the App with its own AIM host is not written: README 'Not yet').
//   3. The two lists of time-stamped Process Actions are played live, at their times (--scale shortens them).
//   4. The run is recorded every 100 ms: what each M-Instance holds (its Snapshot, as its viewer is given it), the light of each Item, the body of each Persona from the frames the
//      venue watched, and - for the captions - each Data Out the Module delivered with its DescrMetadata (the Cue and the Condition the Module found met) and its Action IDs.
//   5. Unless --no-video: the recording is drawn by stage/index.html in headless Edge, frame by frame (DevTools protocol), and made into an MP4 with the applause (ffmpeg).
//   6. --live: no recording to draw afterwards - the two Venues are drawn on the screen while the show runs (stage/index.html?live=state.json in an Edge window),
//      the page asking every 150 ms what the two M-Instances hold now.
// Arguments: [--scale 1.0] [--out <folder>] [--video <mp4>] [--no-video] [--fps 20] [--live] [--port 8771] [--lead 10] [--controller <MPAI as a Service>]
static string Arg(string[] a, string name, string fallback) { var i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : fallback; }
static string FindRoot()
{
    for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar)))
        if (Directory.Exists(Path.Combine(dir, "AIMs", "AMDs")) && Directory.Exists(Path.Combine(dir, "schemas"))) return dir;
    throw new InvalidOperationException("repository root not found");
}

var root = FindRoot();
var scale = double.Parse(Arg(args, "--scale", "1"), CultureInfo.InvariantCulture);
var fps = int.Parse(Arg(args, "--fps", "20"), CultureInfo.InvariantCulture);
var outDir = Path.GetFullPath(Arg(args, "--out", Path.Combine(root, "Test", "Results", DateTime.Now.ToString("yyyy-MM-dd"), "xrv-two-metaverses")));
var video = Arg(args, "--video", Path.Combine(outDir, "XRV-LTP-Real-Virtual-Environments-demo.mp4"));
Directory.CreateDirectory(outDir);
var showDir = Path.Combine(root, "Test", "Data", "XRV", "LongerEvening");
var controller = Arg(args, "--controller", "") is { Length: > 0 } cu ? cu : null;   // the Module on that Controller (an instance of MPAI as a Service), not on one of this program
var events = new List<string>();
void Log(string line) { lock (events) events.Add(line); Console.WriteLine(line); }

if (args.Contains("--live"))                                                        // the two Venues on the screen, in real time
{
    var port = int.Parse(Arg(args, "--port", "8771"), CultureInfo.InvariantCulture);
    var lead = double.Parse(Arg(args, "--lead", "-1"), CultureInfo.InvariantCulture);   // -1: the show starts when the Start button is pressed
    using var listener = Video.Serve(root, outDir, port);
    var page = $"http://127.0.0.1:{port}/?live=state.json";
    var edge = new[] { @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", @"C:\Program Files\Microsoft\Edge\Application\msedge.exe" }.FirstOrDefault(File.Exists);
    Process? window = null;
    if (edge is not null && !args.Contains("--no-window")) window = Process.Start(new ProcessStartInfo(edge, $"--user-data-dir=\"{Path.Combine(outDir, "live-profile")}\" --no-first-run --start-fullscreen --autoplay-policy=no-user-gesture-required --app={page}") { UseShellExecute = false });
    Log($"the stage: {page}; the show starts {(lead < 0 ? "when Start is pressed" : $"in {lead:0} s")}; Close (top right) ends it");
    _ = Show.Quit.Task.ContinueWith(_ => { try { window?.Kill(true); } catch { } Environment.Exit(0); });
    var live = await Show.RunAsync(root, showDir, scale, outDir, Log, lead, controller);
    File.WriteAllLines(Path.Combine(outDir, "events.txt"), events);
    Log("the show is over; the last picture stays until Close is pressed.");
    await Show.Quit.Task.WaitAsync(TimeSpan.FromHours(2)).ContinueWith(_ => { });
    try { window?.Kill(true); } catch { }
    return live.Ok ? 0 : 1;
}

ShowResult result;
if (args.Contains("--render-only"))                                                // draw a recording made before (its log.json and sounds.json)
{
    var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(outDir, "sounds.json")))!;
    result = new ShowResult(true, Path.Combine(outDir, "log.json"), double.Parse(Arg(args, "--until", saved["duration"]!.ToString()), CultureInfo.InvariantCulture),
        saved["sounds"]!.AsArray().Select(s => (s!["t"]!.GetValue<double>(), s["side"]!.GetValue<string>(), s["sound"]!.GetValue<string>(), s["gain"]!.GetValue<double>(), s["until"]!.GetValue<double>())).ToList(), [], []);
}
else
{
    result = await Show.RunAsync(root, showDir, scale, outDir, Log, controller: controller);
    File.WriteAllText(Path.Combine(outDir, "sounds.json"), new JsonObject
    {
        ["duration"] = result.Duration,
        ["sounds"] = new JsonArray(result.Sounds.Select(s => (JsonNode)new JsonObject { ["t"] = s.T, ["side"] = s.Side, ["sound"] = s.Sound, ["gain"] = s.Gain, ["until"] = s.Until }).ToArray())
    }.ToJsonString());
}
File.WriteAllLines(Path.Combine(outDir, "events.txt"), events);
if (args.Contains("--no-video")) return result.Ok ? 0 : 1;
if (Arg(args, "--stills", "") is { Length: > 0 } stills)                           // pictures at these times only (seconds, comma-separated)
{
    await Video.MakeAsync(root, outDir, result, fps, video, Log, stills.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToList());
    return 0;
}
await Video.MakeAsync(root, outDir, result, fps, video, Log);
File.WriteAllLines(Path.Combine(outDir, "events.txt"), events);
return result.Ok ? 0 : 1;

sealed record ShowResult(bool Ok, string LogFile, double Duration, List<(double T, string Side, string Sound, double Gain, double Until)> Sounds, List<string> Fired, List<string> Problems);

static class Show
{
    // The Show device: the documents, one after the other; then nothing. It keeps the Participant Data the show gives back.
    sealed class ShowDevice(string folder) : IDevice
    {
        public readonly TaskCompletionSource Given = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Returned;
        public async IAsyncEnumerable<(string DataType, string Json)> ReadAsync([EnumeratorCancellation] CancellationToken cancel)
        {
            foreach (var (type, file) in new[] { (Types.ScriptVenueAssociations, "ScriptVenueAssociations.json"), (Types.REVenueSpecification, "REVenueSpecification.json"),
                (Types.VEVenueSpecification, "VEVenueSpecification.json"), (Types.ActionTable, "ActionTable.json"), (Types.ParticipantData, "ParticipantData.json"), (Types.Script, "Script.json") })
                yield return (type, File.ReadAllText(Path.Combine(folder, file)));
            Given.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancel);
        }
        public Task DeliverAsync(string dataType, string json, CancellationToken cancel) { Interlocked.Increment(ref Returned); return Task.CompletedTask; }
        public Task SafeStopAsync() => Task.CompletedTask;
    }

    // A venue whose deliveries are also told to whoever records the run (the Data Out is passed on unchanged).
    sealed class Watched(MmmVenue inner, Action<string, string> told) : IDevice
    {
        public IAsyncEnumerable<(string DataType, string Json)> ReadAsync(CancellationToken cancel) => inner.ReadAsync(cancel);
        public async Task DeliverAsync(string dataType, string json, CancellationToken cancel) { told(dataType, json); await inner.DeliverAsync(dataType, json, cancel); }
        public Task SafeStopAsync() => inner.SafeStopAsync();
    }

    sealed record Side(string Name, JsonObject Plan, MInstance M, WebApplicationHolder Server, HttpClient Http, MmmVenue Venue, Dictionary<string, string> Tokens);
    sealed class WebApplicationHolder(Microsoft.AspNetCore.Builder.WebApplication app) { public Microsoft.AspNetCore.Builder.WebApplication App => app; }

    static async Task<Side> MakeAsync(string root, string name, JsonObject plan, bool real)
    {
        var m = new MInstance(Path.Combine(root, "schemas"), plan["MInstance"]!.GetValue<string>(), "ME-" + name);
        foreach (var l in plan["Locations"]!.AsArray().OfType<JsonObject>())
            m.AddLocation(l["ID"]!.GetValue<string>(), owner: plan["Venue"]!.GetValue<string>(), isPublic: l["Public"]?.GetValue<bool>() ?? true,
                          x: l["X"]?.GetValue<double>() ?? 0, y: l["Y"]?.GetValue<double>() ?? 0, size: l["Size"]?.GetValue<double>() ?? 4, at: l["At"]?.GetValue<string>());
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal) { [plan["Venue"]!.GetValue<string>()] = m.AddService(plan["Venue"]!.GetValue<string>()) };
        foreach (var p in plan["Processes"]!.AsArray().OfType<JsonObject>())
            tokens[p["ID"]!.GetValue<string>()] = m.AddService(p["ID"]!.GetValue<string>(), p["Persona"]?.GetValue<string>(), p["Model"]?.GetValue<string>());
        var app = MmmServer.Build(m, "http://127.0.0.1:0");
        await app.StartAsync();
        var url = app.Urls.First();
        var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(30) };
        var items = plan["Items"]!.AsObject().ToDictionary(p => p.Key, p => p.Value!.GetValue<double>(), StringComparer.Ordinal);
        var venueName = plan["Venue"]!.GetValue<string>();
        var venue = new MmmVenue(new MmmClient(http, venueName, tokens[venueName]), real, plan["Root"]!.GetValue<string>(), plan["Stage"]!.GetValue<string>(), items,
                                 plan["UEnvironment"]!.GetValue<string>(), plan["MInstance"]!.GetValue<string>(), url.Replace("http", "ws"),
                                 plan["Consoles"]!.AsArray().Select(c => c!.GetValue<string>()), plan["Areas"]!.AsArray().Select(c => c!.GetValue<string>()));
        return new Side(name, plan, m, new WebApplicationHolder(app), http, venue, tokens);
    }

    static readonly Regex Why = new(@"^Cue (?<cue>\S+) \((?<kind>\w+)\): (?<cond>.*)$", RegexOptions.Singleline);

    // What the two M-Instances hold now, for the page in --live (null until the show starts).
    public static volatile string? LiveState;
    public static readonly TaskCompletionSource Go = new(TaskCreationOptions.RunContinuationsAsynchronously);     // --live: the Start button
    public static readonly TaskCompletionSource Quit = new(TaskCreationOptions.RunContinuationsAsynchronously);   // --live: the Close button

    public static async Task<ShowResult> RunAsync(string root, string showDir, double scale, string outDir, Action<string> log, double lead = 0, string? controller = null)
    {
        var theatrePlan = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(showDir, "ProcessActions-Theatre.json")))!;
        var squarePlan = (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(showDir, "ProcessActions-Square.json")))!;
        var actionNames = JsonNode.Parse(File.ReadAllText(Path.Combine(showDir, "ActionTable.json")))!["ActionTableData"]!.AsArray()
                          .ToDictionary(a => a!["ActionID"]!.GetValue<string>(), a => a!["ActionName"]!.GetValue<string>());
        var theatre = await MakeAsync(root, "Theatre", theatrePlan, true);
        var square = await MakeAsync(root, "Square", squarePlan, false);
        var problems = new List<string>();
        var fired = new List<string>();
        var sounds = new List<(double, string, string, double, double)>();

        var work = Path.Combine(outDir, "controller"); Directory.CreateDirectory(work);
        var settings = Path.Combine(work, "aim-settings.json"); File.WriteAllText(settings, "{}");
        IControllerApi api = controller is null ? new ControllerApi(Path.Combine(root, "AIMs", "AMDs"), settings, new XrvProvider())
                                                : new Mpai.Mas.Client.RemoteControllerApi(controller, Environment.GetEnvironmentVariable("MPAI_MAS_TOKEN"));
        using var disposeApi = api as IDisposable;
        if (controller is not null) log($"the Module runs on the Controller at {controller}");

        DateTimeOffset start = DateTimeOffset.MaxValue;
        double Now() => start == DateTimeOffset.MaxValue ? 0 : (DateTimeOffset.UtcNow - start).TotalSeconds / scale;
        var captions = new Dictionary<string, JsonObject>
        {
            ["theatre"] = new() { ["cue"] = "C0 · Real", ["condition"] = "waiting for the operator's Go", ["actions"] = new JsonArray() },
            ["square"] = new() { ["cue"] = "C0 · Real", ["condition"] = "waiting for the operator's Go", ["actions"] = new JsonArray() }
        };
        var applauseOn = -1.0;

        void Told(string side, string dataType, string json)
        {
            var o = JsonNode.Parse(json)!;
            var descr = o["DescrMetadata"]?.GetValue<string>() ?? "";
            var ids = new List<string>();
            foreach (var q in (o["REDataOut"]?["ControlObjects"]?.AsArray() ?? []).Select(c => c?["ControlObject"]?["ControlQualifier"]?["Attributes"]?["ActionID"])
                         .Concat((o["VEDataOut"]?["MetaverseObjects"]?.AsArray() ?? []).Select(c => c?["MetaverseObject"]?["MetaverseQualifier"]?["Attributes"]?["ActionID"])))
                if (q?.GetValue<string>() is { } id && !ids.Contains(id)) ids.Add(id);
            var t = Now();
            lock (captions)
            {
                var c = captions[side];
                var match = Why.Match(descr);
                var cue = match.Success ? $"{match.Groups["cue"].Value} · {match.Groups["kind"].Value}" : "";
                if (c["cue"]?.GetValue<string>() != cue || c["firedAt"]?.GetValue<double>() is not { } at || t - at > 1.0)
                    c = captions[side] = new JsonObject { ["cue"] = cue, ["firedAt"] = t, ["actions"] = new JsonArray() };
                var cond = match.Success ? match.Groups["cond"].Value : descr;
                c["condition"] = Regex.Replace(cond, @"\s*\((XRV-[A-Z]{3}-V1\.0)\)", "");
                c["dataType"] = string.Join(" + ", Regex.Matches(cond, @"XRV-[A-Z]{3}-V1\.0").Select(m => m.Value).Distinct());
                foreach (var id in ids) c["actions"]!.AsArray().Add(actionNames.GetValueOrDefault(id, id));
            }
            foreach (var id in ids)
            {
                lock (fired) fired.Add($"{t:0.0} s {side}: {id} <- {descr}");
                log($"[{t,6:0.0}] Module -> {side}: {id} ({actionNames.GetValueOrDefault(id, id)}) because {descr}");
                if (id == "VE-APPLAUSE") applauseOn = t;
                if (id == "VE-FIREFLIES" && applauseOn >= 0) { lock (sounds) sounds.Add((applauseOn, "square", "applause.wav", 0.55, t)); applauseOn = -1; }
            }
        }

        var show = new ShowDevice(showDir);
        var devices = new DeviceRegistry().RegisterDevice("Show", show)
            .RegisterDevice("RealVenue", new Watched(theatre.Venue, (d, j) => Told("theatre", d, j)))
            .RegisterDevice("VirtualVenue", new Watched(square.Venue, (d, j) => Told("square", d, j)))
            .RegisterPresent("operator", shown => { foreach (var (k, v) in shown) log($"[operator] {k}: {v}"); return Task.CompletedTask; });
        var interpreter = new WorkflowInterpreter(api.Async(), devices, line => log("[workflow] " + line));
        using var stop = new CancellationTokenSource();
        var workflow = Task.Run(() => interpreter.RunAsync(new WorkflowReader().Read(File.ReadAllText(Path.Combine(root, "Apps", "XRV", "LTP", "XRV-LTP.orch"))), stop.Token));

        await show.Given.Task.WaitAsync(TimeSpan.FromSeconds(60));
        for (var i = 0; i < 300 && Volatile.Read(ref show.Returned) == 0; i++) await Task.Delay(100);
        if (show.Returned == 0) problems.Add("the Participant Data did not come back: the documents may not have reached the Module");
        await Task.Delay(1500);                                                       // the venues place their Items

        var says = new Dictionary<string, (double T, string Text)>();
        void Played(string side, double t, string line)
        {
            log($"[{(t < 0 ? Now() : t),6:0.0}] {line}");
            var dash = line.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0 && t >= 0) lock (says) says[side] = (t, line[(dash + 3)..]);
        }
        MmmClient Client(Side s, string p) => new(s.Http, p, s.Tokens[p]);
        var theatrePlayer = new ProcessActionPlayer(theatrePlan, p => Client(theatre, p), theatre.Server.App.Urls.First().Replace("http", "ws"), scale, (t, l) => Played("theatre", t, l));
        var squarePlayer = new ProcessActionPlayer(squarePlan, p => Client(square, p), square.Server.App.Urls.First().Replace("http", "ws"), scale, (t, l) => Played("square", t, l));
        var end = theatrePlan["End"]!.GetValue<double>();
        if (lead != 0)                                                                // --live: the screen shows the Venues before the Go
        {
            var until = lead > 0 ? DateTimeOffset.UtcNow + TimeSpan.FromSeconds(lead) : DateTimeOffset.MaxValue;
            while (DateTimeOffset.UtcNow < until && !Go.Task.IsCompleted)
            {
                LiveState = new JsonObject { ["t"] = 0, ["clock"] = "00:00", ["theatre"] = Things(theatre), ["square"] = Things(square),
                    ["captions"] = new JsonObject { ["theatre"] = Caption("theatre", 0), ["square"] = Caption("square", 0) }, ["waiting"] = lead < 0 }.ToJsonString();
                await Task.Delay(100);
            }
        }
        start = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(300);
        log($"show starts {start:HH:mm:ss.fff} (scale {scale})");
        using var players = new CancellationTokenSource();
        var playing = Task.WhenAll(theatrePlayer.RunAsync(start, players.Token), squarePlayer.RunAsync(start, players.Token));

        foreach (var (plan, side) in new[] { (theatrePlan, "theatre"), (squarePlan, "square") })
        foreach (var a in plan["Actions"]!.AsArray().OfType<JsonObject>().Where(a => a["Sound"] is not null))
        {
            var file = Path.Combine(showDir, a["Sound"]!.GetValue<string>());                // as long as its sound (16 kHz mono 16-bit, after a 44-byte header)
            var seconds = File.Exists(file) ? (new FileInfo(file).Length - 44) / 32000.0 : 6.0;
            sounds.Add((a["t"]!.GetValue<double>(), side, a["Sound"]!.GetValue<string>(), 1.0, a["t"]!.GetValue<double>() + seconds));
        }

        // THE RECORDING
        var frames = new JsonArray();
        JsonObject Things(Side s)
        {
            var areas = s.Plan["Areas"]!.AsArray().Select(a => a!.GetValue<string>()).ToHashSet();
            var list = new JsonArray();
            foreach (var t in s.M.Snapshot()["Things"]!.AsArray().OfType<JsonObject>())
            {
                var id = t["ID"]!.GetValue<string>();
                var kind = (string?)t["Kind"];
                if (kind == "Location") continue;
                var item = s.M.Get(id);
                var isVenueItem = s.Plan["Items"]!.AsObject().ContainsKey(id);
                if (!isVenueItem && kind == "Persona" && !areas.Contains((string?)t["At"] ?? "")) continue;   // offstage: in the wings, in the street
                var o = new JsonObject { ["ID"] = id, ["Kind"] = isVenueItem ? "Item" : kind, ["At"] = t["At"]?.DeepClone(), ["X"] = t["X"]?.DeepClone(), ["Y"] = t["Y"]?.DeepClone(), ["Yaw"] = t["Yaw"]?.DeepClone() };
                if (t["Model"] is { } model && !isVenueItem) o["Model"] = model.DeepClone();
                if (item?.LightSource is { } light)
                {
                    o["Intensity"] = light["Intensity"]?.DeepClone();
                    if (light["ColourInformation"]?["ColourData"] is { } colour) o["Colour"] = colour.DeepClone();
                }
                if ((string?)t["Animation"] is { } stream && s.Venue.FrameOf(stream) is { } frame)
                    try
                    {
                        var data = JsonNode.Parse(frame)?["BodyDescriptorsData"]?[0]?["Data"]?.GetValue<string>();
                        if (data is not null && JsonNode.Parse(data)?["Joints"] is JsonObject joints) o["Pose"] = joints.DeepClone();
                    }
                    catch (System.Text.Json.JsonException) { }
                list.Add(o);
            }
            return new JsonObject { ["Things"] = list };
        }
        JsonObject Caption(string side, double t)
        {
            JsonObject c;
            lock (captions) c = (JsonObject)captions[side].DeepClone();
            c.Remove("firedAt");
            lock (says) if (says.TryGetValue(side, out var s) && t - s.T < 7) c["note"] = $"in the {(side == "theatre" ? "Theatre" : "Square")}: {s.Text} (time-stamped Process Action)";
            return c;
        }

        for (var t = 0.0; t <= end; t = Now())
        {
            var tt = Math.Round(t, 2);
            var frame = new JsonObject
            {
                ["t"] = tt, ["clock"] = $"{(int)tt / 60:00}:{(int)tt % 60:00}",
                ["theatre"] = Things(theatre), ["square"] = Things(square),
                ["captions"] = new JsonObject { ["theatre"] = Caption("theatre", tt), ["square"] = Caption("square", tt) }
            };
            var heard = new JsonArray();                                           // the sounds heard now (for the page in --live)
            lock (sounds) foreach (var (st, side, name, gain, until) in sounds) if (st <= tt && tt < until) heard.Add(new JsonObject { ["side"] = side, ["sound"] = name, ["gain"] = gain, ["loop"] = name == "applause.wav" });
            if (applauseOn >= 0) heard.Add(new JsonObject { ["side"] = "square", ["sound"] = "applause.wav", ["gain"] = 0.55, ["loop"] = true });
            frame["sounds"] = heard;
            LiveState = frame.ToJsonString();
            frame.Remove("sounds");
            frames.Add(frame);
            await Task.Delay(TimeSpan.FromSeconds(0.1 * scale));
        }

        players.Cancel();
        try { await playing; } catch (OperationCanceledException) { }
        problems.AddRange(theatrePlayer.Failed.Select(f => "Theatre: " + f));
        problems.AddRange(squarePlayer.Failed.Select(f => "Square: " + f));
        problems.AddRange(theatre.Venue.Unperformed.Select(f => "Theatre venue: " + f));
        problems.AddRange(square.Venue.Unperformed.Select(f => "Square venue: " + f));
        foreach (var a in api.Status(interpreter.ModuleId).Aims)
        {
            log($"AIM {a.Aim}: {a.Status} {a.Reason} {string.Join(" / ", a.Reports)}");
            if (a.Reports.Count > 0) problems.Add($"{a.Aim} {a.Status}: {string.Join(" / ", a.Reports)}");
        }
        await theatrePlayer.CloseAsync(); await squarePlayer.CloseAsync();
        stop.Cancel();
        try { await workflow.WaitAsync(TimeSpan.FromSeconds(30)); } catch (Exception) { }
        await theatre.Server.App.StopAsync(); await square.Server.App.StopAsync();

        var logFile = Path.Combine(outDir, "log.json");
        File.WriteAllText(logFile, new JsonObject { ["duration"] = end, ["frames"] = frames }.ToJsonString());
        // the sound mix reads every sound of the show (applause and the performers' voices) from the output folder
        foreach (var wav in Directory.GetFiles(showDir, "*.wav")) File.Copy(wav, Path.Combine(outDir, Path.GetFileName(wav)), true);
        foreach (var p in problems) log("PROBLEM " + p);
        log($"fired: {fired.Count} Action IDs; frames: {frames.Count}; problems: {problems.Count}");
        return new ShowResult(problems.Count == 0, logFile, end, sounds.Select(s => (s.Item1, s.Item2, s.Item3, s.Item4, s.Item5)).ToList(), fired, problems);
    }
}

static class Video
{
    // A small file server for the page: the stage folder, the three.js of the MMM viewer, the Personae, and the recording.
    public static HttpListener Serve(string root, string outDir, int port)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var stage = Path.Combine(root, "Apps", "XRV", "LTP", "TwoMetaverses", "stage");
        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { return; }
                _ = Task.Run(() =>
                {
                    var path = Uri.UnescapeDataString(ctx.Request.Url!.AbsolutePath).TrimStart('/');
                    if (path is "go" or "quit")
                    {
                        (path == "go" ? Show.Go : Show.Quit).TrySetResult();
                        ctx.Response.StatusCode = 204; ctx.Response.Close(); return;
                    }
                    if (path == "state.json")
                    {
                        var state = Show.LiveState;
                        if (state is null) { ctx.Response.StatusCode = 404; ctx.Response.Close(); return; }
                        var body = Encoding.UTF8.GetBytes(state);
                        ctx.Response.ContentType = "application/json"; ctx.Response.ContentLength64 = body.Length;
                        try { ctx.Response.OutputStream.Write(body); } catch { }
                        ctx.Response.Close(); return;
                    }
                    string? file = path.StartsWith("lib/") ? Path.Combine(root, "AllApps", "MMM", "Viewer", path)
                                 : path.StartsWith("personae/") ? Path.Combine(root, "AllApps", "MMM", "Viewer", path)
                                 : path == "mpai-units.js" ? Path.Combine(root, "AIF", "UserAgent", "PhysicalLayer", "Browser", "mpai-units.js")      // the Units of a browser: one file for every page, from where it is
                                 : path == "log.json" ? Path.Combine(outDir, "log.json")
                                 : path.StartsWith("sounds/") ? Path.Combine(root, "Test", "Data", "XRV", "LongerEvening", path[7..])
                                 : Path.Combine(stage, path.Length == 0 ? "index.html" : path);
                    file = Path.GetFullPath(file);
                    if (!File.Exists(file)) { ctx.Response.StatusCode = 404; ctx.Response.Close(); return; }
                    ctx.Response.ContentType = Path.GetExtension(file) switch { ".html" => "text/html", ".js" => "text/javascript", ".json" => "application/json", ".glb" => "model/gltf-binary", ".png" => "image/png", ".wav" => "audio/wav", _ => "application/octet-stream" };
                    var bytes = File.ReadAllBytes(file);
                    ctx.Response.ContentLength64 = bytes.Length;
                    try { ctx.Response.OutputStream.Write(bytes); } catch { }
                    ctx.Response.Close();
                });
            }
        });
        return listener;
    }

    sealed class Cdp(ClientWebSocket socket)
    {
        private int next;
        public async Task<JsonNode?> CallAsync(string method, JsonObject? p = null)
        {
            var id = Interlocked.Increment(ref next);
            var msg = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = p ?? new JsonObject() }.ToJsonString();
            await socket.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, CancellationToken.None);
            var buffer = new byte[1 << 20];
            while (true)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult r;
                do { r = await socket.ReceiveAsync(buffer, CancellationToken.None); ms.Write(buffer, 0, r.Count); } while (!r.EndOfMessage);
                var o = JsonNode.Parse(ms.ToArray());
                if (o?["id"]?.GetValue<int>() == id)
                {
                    if (o["error"] is { } e) throw new InvalidOperationException($"{method}: {e.ToJsonString()}");
                    return o["result"];
                }
            }
        }
        public async Task<JsonNode?> EvalAsync(string expression) =>
            (await CallAsync("Runtime.evaluate", new JsonObject { ["expression"] = expression, ["awaitPromise"] = true, ["returnByValue"] = true }))?["result"]?["value"];
    }

    public static async Task MakeAsync(string root, string outDir, ShowResult show, int fps, string mp4, Action<string> log, List<double>? stills = null)
    {
        var framesDir = Path.Combine(outDir, stills is null ? "frames" : "stills");
        if (stills is not null && Directory.Exists(framesDir)) Directory.Delete(framesDir, true);
        Directory.CreateDirectory(framesDir);                                         // frames already drawn are kept: a render that stopped resumes
        const int port = 8771, debug = 9339;
        using var server = Serve(root, outDir, port);
        var profile = Path.Combine(outDir, "edge-profile");
        Process? edge = null;
        ClientWebSocket? socket = null;
        Cdp? cdp = null;

        // Headless Edge with the page loaded and ready; a page that stops answering is closed and opened again.
        async Task OpenAsync()
        {
            try { socket?.Dispose(); } catch { }
            try { edge?.Kill(true); } catch { }
            edge = Process.Start(new ProcessStartInfo(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                $"--headless=new --remote-debugging-port={debug} --user-data-dir=\"{profile}\" --window-size=1920,1080 --hide-scrollbars --mute-audio --enable-unsafe-swiftshader --ignore-gpu-blocklist about:blank")
                { UseShellExecute = false })!;
            using var http = new HttpClient();
            JsonArray? targets = null;
            for (var i = 0; i < 100 && targets is null; i++)
            {
                try { targets = JsonNode.Parse(await http.GetStringAsync($"http://127.0.0.1:{debug}/json"))?.AsArray(); } catch { await Task.Delay(200); }
            }
            var page = targets!.OfType<JsonObject>().First(t => (string?)t["type"] == "page");
            socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
            await socket.ConnectAsync(new Uri(page["webSocketDebuggerUrl"]!.GetValue<string>()), CancellationToken.None);
            cdp = new Cdp(socket);
            await cdp.CallAsync("Emulation.setDeviceMetricsOverride", new JsonObject { ["width"] = 1920, ["height"] = 1080, ["deviceScaleFactor"] = 1, ["mobile"] = false });
            await cdp.CallAsync("Page.enable");
            await cdp.CallAsync("Page.navigate", new JsonObject { ["url"] = $"http://127.0.0.1:{port}/index.html?log=/log.json" });
            for (var i = 0; i < 600; i++)
            {
                await Task.Delay(200);
                if ((await cdp.EvalAsync("window.stageReady === true"))?.GetValue<bool>() == true) return;
                if ((await cdp.EvalAsync("String(window.stageError || '')"))?.GetValue<string>() is { Length: > 0 } error) throw new InvalidOperationException("the page: " + error);
            }
            throw new TimeoutException("the page did not become ready");
        }

        try
        {
            await OpenAsync();
            var n = stills is null ? (int)Math.Floor(show.Duration * fps) : stills.Count - 1;
            var clock = Stopwatch.StartNew();
            for (var f = 0; f <= n; f++)
            {
                var file = Path.Combine(framesDir, $"{f:00000}.jpg");
                if (stills is null && File.Exists(file) && new FileInfo(file).Length > 0) continue;
                var t = stills is null ? f / (double)fps : stills[f];
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        await cdp!.EvalAsync($"window.setShowTime({t.ToString(CultureInfo.InvariantCulture)}).then(() => true)").WaitAsync(TimeSpan.FromSeconds(45));
                        var shot = await cdp.CallAsync("Page.captureScreenshot", new JsonObject { ["format"] = "jpeg", ["quality"] = 90 }).WaitAsync(TimeSpan.FromSeconds(45));
                        await File.WriteAllBytesAsync(file, Convert.FromBase64String(shot!["data"]!.GetValue<string>()));
                        break;
                    }
                    catch (Exception e) when (attempt < 4 && e is TimeoutException or WebSocketException or InvalidOperationException)
                    {
                        log($"video: frame {f} ({t:0.00} s) did not come ({e.GetType().Name}); the page is opened again (attempt {attempt + 1})");
                        await OpenAsync();
                    }
                }
                if (f % (fps * 10) == 0) log($"video: frame {f}/{n} ({clock.Elapsed.TotalSeconds:0} s)");
            }
        }
        finally { try { socket?.Dispose(); } catch { } try { edge?.Kill(true); } catch { } }
        if (stills is not null) { log($"stills: {framesDir}"); return; }

        // THE SOUND: stereo, the Theatre on the left, the Square on the right (as the two screens).
        var wav = Path.Combine(outDir, "sound.wav");
        Mix(outDir, show, wav);
        var ffmpeg = "ffmpeg";
        var psi = new ProcessStartInfo(ffmpeg, $"-y -framerate {fps} -i \"{Path.Combine(framesDir, "%05d.jpg")}\" -i \"{wav}\" -c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p -c:a aac -b:a 160k -shortest \"{mp4}\"")
                  { UseShellExecute = false, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        var err = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        log(p.ExitCode == 0 ? $"video: {mp4}" : "video: ffmpeg failed: " + err[^Math.Min(800, err.Length)..]);
    }

    static void Mix(string outDir, ShowResult show, string wav)
    {
        const int rate = 16000;
        var n = (int)(show.Duration * rate) + rate;
        var left = new double[n]; var right = new double[n];
        foreach (var (t, side, sound, gain, until) in show.Sounds)
        {
            var pcm = Read16(Path.Combine(outDir, sound));
            var at = (int)(t * rate); var stop = (int)(until * rate);
            for (var i = 0; i < pcm.Length && at + i < n && at + i < stop + rate / 2; i++)
            {
                var fade = at + i > stop ? 1 - (at + i - stop) / (rate / 2.0) : 1;
                var s = pcm[i] / 32768.0 * gain * fade;
                if (side == "theatre") { left[at + i] += s; right[at + i] += s * 0.25; } else { right[at + i] += s; left[at + i] += s * 0.25; }
            }
        }
        using var w = new BinaryWriter(File.Create(wav));
        w.Write("RIFF"u8); w.Write(36 + n * 4); w.Write("WAVE"u8); w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
        w.Write("data"u8); w.Write(n * 4);
        for (var i = 0; i < n; i++) { w.Write((short)Math.Clamp(left[i] * 32767, -32767, 32767)); w.Write((short)Math.Clamp(right[i] * 32767, -32767, 32767)); }
    }

    static short[] Read16(string file)
    {
        var b = File.ReadAllBytes(file);
        var pcm = new short[(b.Length - 44) / 2];
        Buffer.BlockCopy(b, 44, pcm, 0, pcm.Length * 2);
        return pcm;
    }
}
