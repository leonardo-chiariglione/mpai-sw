using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

using Mpai.Mas.Server;

// THALIA - first version of the Full Profile of MPAI-THL V1.0 WD0.1.
//
//   dotnet Thalia.dll [--Urls http://localhost:5199] [--Mas https://localhost:5105/] [--Apps MTS,MCA,AMQ,NPR]
//
// The Launcher (http://localhost:5199/) shows one catalogue: the local Apps of this machine and the Apps of the MPAI as a Service instance, each with
// what its Module needs. Run starts an App, local or MAS-based, with the same control:
//   local      an instance of the local Controller for this App (Thalia\Controller, its own process: GEN-1), then the App's client pointed at it;
//   MAS-based  the App's client pointed at the MPAI as a Service instance (started here if it is on this machine and not running).
// The App's client is the browser User Agent of the MAS App (AIF\UserAgent\Clients\Browser): it reads the App's Workflow from the Controller it is
// given and runs it with the browser's devices, so a local App and a MAS-based App behave the same (GEN-3). Stop ends the App's client and, for a
// local App, its Controller instance.

var root = RepositoryRoot(AppContext.BaseDirectory);
// The Launcher's page is in Thalia\Core\wwwroot, wherever the program is started from.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = Path.Combine(root, "Thalia", "Core") });
builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://localhost:5199");
var app = builder.Build();

var appsRoot = Path.Combine(root, "Apps");
var amds     = Path.Combine(root, "AIMs", "AMDs");
var masUrl   = new Uri(builder.Configuration["Mas"] ?? "https://localhost:5105/");
var appIds   = (builder.Configuration["Apps"] ?? "MTS,MCA,AMQ,NPR").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var logs     = Path.Combine(Path.GetTempPath(), "Thalia");
Directory.CreateDirectory(logs);

var controllerDll = Path.Combine(root, "Thalia", "Controller", "bin", "Debug", "net10.0", "ThaliaController.dll");
var hostProject   = Path.Combine(root, "AIF", "UserAgent", "Clients", "Browser", "Host");
var hostDll       = Path.Combine(hostProject, "bin", "Debug", "net10.0", "RcaWeb.Host.dll");
var masProject    = Path.Combine(root, "Apps", "MAS", "Service", "src", "MasService.csproj");
var masConfig     = Path.Combine(root, "Apps", "MAS", "Service", "mas-server-AI.json");
var ollamaExe     = Path.Combine(root, "Support", "Ollama", "bin", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ollama.exe" : "ollama");

var masHttp = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }) { Timeout = TimeSpan.FromSeconds(10) };
var running = new ConcurrentDictionary<string, Run>();
var gpu = Gpu.Probe();

// ---- the catalogue (APP-2): local Apps and MAS-based Apps, each with the needs of its Module ----------------------------------------------------
app.MapGet("/api/catalogue", async () =>
{
    var local = AppCatalogue.Scan(appsRoot, appIds);
    var items = new JsonArray();
    foreach (var e in local.Apps.OrderBy(a => Array.IndexOf(appIds, a.Id)))
        items.Add(Item(e.Id, e.Name, e.Description, "local", e.Module, $"/api/icon/local/{e.Id}"));

    string? masProblem = null;
    try
    {
        var list = JsonNode.Parse(await masHttp.GetStringAsync(new Uri(masUrl, "MPAI/AIFU/Apps")))!.AsArray();
        foreach (var a in list.OfType<JsonObject>().Where(a => appIds.Contains((string?)a["id"])).OrderBy(a => Array.IndexOf(appIds, (string?)a["id"])))
        {
            var id = (string)a["id"]!;
            items.Add(Item(id, (string?)a["name"] ?? id, (string?)a["description"] ?? "", "mas", local.Apps.FirstOrDefault(l => l.Id == id)?.Module, $"/api/icon/mas/{id}"));
        }
    }
    catch (Exception ex) { masProblem = $"The MPAI as a Service instance at {masUrl} does not answer ({ex.GetBaseException().Message.Split('\n')[0]}). Its Apps appear when it runs; Run on MAS starts it if it is on this machine."; }

    return Results.Json(new JsonObject
    {
        ["machine"] = new JsonObject { ["os"] = RuntimeInformation.OSDescription, ["arch"] = RuntimeInformation.OSArchitecture.ToString(), ["gpu"] = gpu.Text },
        ["mas"] = masUrl.ToString(),
        ["masProblem"] = masProblem,
        ["apps"] = items,
        ["running"] = new JsonArray(running.Values.Select(r => (JsonNode)r.Json()).ToArray())
    });
});

app.MapGet("/api/icon/{where}/{id}", async (string where, string id) =>
{
    if (where == "local")
    {
        var e = AppCatalogue.Scan(appsRoot, new[] { id }).Apps.FirstOrDefault();
        var f = e?.IconFile is { } name ? (Path.IsPathRooted(name) ? name : Path.Combine(e.Folder, name)) : null;
        if (f is not null && File.Exists(f)) return Results.File(f, f.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? "image/svg+xml" : "image/png");
        return Results.NotFound();
    }
    try
    {
        var r = await masHttp.GetAsync(new Uri(masUrl, $"MPAI/AIFU/Apps/{Uri.EscapeDataString(id)}/Icon"));
        return r.IsSuccessStatusCode ? Results.File(await r.Content.ReadAsByteArrayAsync(), r.Content.Headers.ContentType?.ToString() ?? "image/svg+xml") : Results.NotFound();
    }
    catch { return Results.NotFound(); }
});

// ---- the App life cycle (APP-6): run and stop, local and MAS-based alike ------------------------------------------------------------------------
app.MapPost("/api/run/{where}/{id}", async (string where, string id) =>
{
    if (!appIds.Contains(id)) return Results.BadRequest(new { error = $"{id} is not in the catalogue" });
    var key = $"{where}:{id}";
    if (running.TryGetValue(key, out var already) && already.Alive) return Results.Json(already.Json());
    var run = new Run(key, id, where);
    running[key] = run;
    _ = Task.Run(() => StartAsync(run));
    return Results.Json(run.Json());
});

app.MapPost("/api/stop/{where}/{id}", (string where, string id) =>
{
    if (running.TryRemove($"{where}:{id}", out var run)) run.Stop("stopped by the User");
    return Results.Ok();
});

app.MapGet("/api/runs", () => Results.Json(new JsonArray(running.Values.Select(r => (JsonNode)r.Json()).ToArray())));
app.MapGet("/api/log/{where}/{id}", (string where, string id) =>
    running.TryGetValue($"{where}:{id}", out var r) ? Results.Text(string.Join('\n', r.Lines.TakeLast(200))) : Results.NotFound());

// The User's choices (season, side of the functions, search scope, welcome): one small file for this User on this machine, so that they stay whatever window shows Thalia.
var prefsFile = builder.Configuration["Prefs"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thalia", "preferences.json");
var prefsKeys = new[] { "season", "side", "scope", "welcomeHidden" };
var prefsLock = new object();
JsonObject ReadPrefs() { try { return File.Exists(prefsFile) ? (JsonNode.Parse(File.ReadAllText(prefsFile)) as JsonObject ?? new JsonObject()) : new JsonObject(); } catch { return new JsonObject(); } }
app.MapGet("/api/prefs", () => { lock (prefsLock) return Results.Json(ReadPrefs()); });
app.MapPost("/api/prefs", async (HttpRequest req) =>
{
    JsonObject? change;
    try { change = await JsonNode.ParseAsync(req.Body) as JsonObject; } catch { return Results.BadRequest(); }
    if (change is null) return Results.BadRequest();
    lock (prefsLock)
    {
        var now = ReadPrefs();
        foreach (var k in prefsKeys)
            if (change.ContainsKey(k)) now[k] = change[k] is JsonValue v && (v.TryGetValue<string>(out _) || v.TryGetValue<bool>(out _)) ? v.DeepClone() : null;
        Directory.CreateDirectory(Path.GetDirectoryName(prefsFile)!);
        File.WriteAllText(prefsFile, now.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return Results.Json(now);
    }
});

// Return to Windows: Thalia fills the screen; this minimises the window that shows it (the foreground one: the click that called this is in it).
app.MapPost("/api/windows", () =>
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) NativeWindows.MinimiseForeground();
    return Results.Ok();
});

app.MapGet("/", () => Results.File(Path.Combine(app.Environment.WebRootPath, "launcher.html"), "text/html; charset=utf-8"));
app.UseStaticFiles();

app.Lifetime.ApplicationStopping.Register(() => { foreach (var r in running.Values) r.Stop("Thalia closed"); });

Console.WriteLine("=== Thalia (MPAI-THL V1.0, Full Profile, first version) ===");
Console.WriteLine($"  Launcher:  {builder.Configuration["Urls"] ?? "http://localhost:5199"}");
Console.WriteLine($"  Apps:      {string.Join(", ", appIds)} (local: {appsRoot})");
Console.WriteLine($"  MAS:       {masUrl}");
Console.WriteLine($"  Machine:   {RuntimeInformation.OSDescription}, {RuntimeInformation.OSArchitecture}, {gpu.Text}");
await app.RunAsync();
return;

// ---- starting an App ----------------------------------------------------------------------------------------------------------------------------
async Task StartAsync(Run run)
{
    try
    {
        if (!File.Exists(hostDll)) throw new InvalidOperationException($"the App client is not built: {hostDll} (dotnet build {hostProject})");
        if (run.App == "MCA") await EnsureOllamaAsync(run);

        Uri service;
        if (run.Where == "local")
        {
            if (!File.Exists(controllerDll)) throw new InvalidOperationException($"the local Controller is not built: {controllerDll}");
            var port = FreePort();
            run.Phase = "starting its local Controller (its Module and AIMs load: up to a minute or two)";
            var controller = run.Spawn("controller", "dotnet", $"\"{controllerDll}\" --listen http://127.0.0.1:{port} --app {run.App}", root, null);
            if (!await run.WaitForAsync(controller, "[MAS] Listening", TimeSpan.FromMinutes(5)))
                throw new InvalidOperationException("its local Controller did not come up: " + run.LastLine(controller));
            service = new Uri($"http://127.0.0.1:{port}/");
        }
        else
        {
            if (!await AnswersAsync(masUrl))
            {
                if (!masUrl.IsLoopback) throw new InvalidOperationException($"the MPAI as a Service instance {masUrl} does not answer");
                run.Phase = "starting the MPAI as a Service instance on this machine (the first time: several minutes)";
                Shared.Mas ??= run.Spawn("mas-service", "dotnet", $"run --project \"{masProject}\" -- \"{masConfig}\"", root, null, keep: true);
                var until = DateTime.UtcNow.AddMinutes(10);
                while (!await AnswersAsync(masUrl))
                {
                    if (DateTime.UtcNow > until || run.Cancelled) throw new InvalidOperationException("the MPAI as a Service instance did not come up");
                    await Task.Delay(2000);
                }
            }
            service = masUrl;
        }

        var hostPort = FreePort();
        run.Phase = "starting the App's client";
        var host = run.Spawn("client", "dotnet", $"\"{hostDll}\" --Service {service} --Urls https://localhost:{hostPort}", hostProject,
                             new Dictionary<string, string> { ["ASPNETCORE_ENVIRONMENT"] = "Development" });
        if (!await run.WaitForAsync(host, "Now listening on", TimeSpan.FromMinutes(2)))
            throw new InvalidOperationException("the App's client did not come up: " + run.LastLine(host));
        run.Url = $"https://localhost:{hostPort}/app/{Uri.EscapeDataString(run.App)}";
        run.Phase = "running";
    }
    catch (Exception ex)
    {
        run.Phase = "failed: " + ex.Message;
        run.Stop(null);
    }
}

async Task EnsureOllamaAsync(Run run)
{
    if (await PortOpenAsync(11434)) return;
    if (!File.Exists(ollamaExe)) return;
    run.Phase = "starting the language model (Ollama)";
    Shared.Ollama ??= run.Spawn("ollama", ollamaExe, "serve", root,
        new Dictionary<string, string> { ["OLLAMA_MODELS"] = Path.Combine(root, "Support", "Ollama", "models"), ["OLLAMA_KEEP_ALIVE"] = "30m" }, keep: true);
    for (var i = 0; i < 60 && !await PortOpenAsync(11434); i++) await Task.Delay(1000);
}

async Task<bool> AnswersAsync(Uri service)
{
    try { using var r = await masHttp.GetAsync(new Uri(service, "MPAI/AIFU/Apps")); return r.IsSuccessStatusCode; }
    catch { return false; }
}

static async Task<bool> PortOpenAsync(int port)
{
    try { using var c = new TcpClient(); await c.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromSeconds(1)); return true; }
    catch { return false; }
}

static int FreePort()
{
    var l = new TcpListener(IPAddress.Loopback, 0); l.Start();
    var p = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p;
}

JsonObject Item(string id, string name, string description, string where, string? module, string icon)
{
    var needs = Needs(module);
    return new JsonObject
    {
        ["id"] = id, ["name"] = name, ["description"] = description, ["where"] = where, ["module"] = module, ["icon"] = icon,
        ["aims"] = needs.Aims, ["licences"] = needs.Licences, ["resources"] = needs.Resources, ["accelerators"] = needs.Accelerators,
        ["canRun"] = where == "mas" ? true : needs.CanRun, ["why"] = needs.Why
    };
}

// WHAT THE MODULE NEEDS, from its L3 (AIM Instance Metadata): its AIMs, the AIM Licences, the computing resources, and whether this machine can run it.
Needs Needs(string? module)
{
    var n = new Needs();
    if (module is null) { n.Why = "no Module named"; return n; }
    var file = Path.Combine(amds, module + ".json");
    if (!File.Exists(file)) { n.CanRun = false; n.Why = $"its L3 {module}.json is not on this machine"; return n; }
    try
    {
        var l3 = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        foreach (var s in l3["SubAIMs"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            if ((string?)(s["Identifier"]?["AIMInstID"] ?? s["AIMInstID"]) is { } a && !n.Aims.Any(x => (string?)x == a)) n.Aims.Add(a);
        foreach (var r in l3["ResourcePolicies"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            n.Resources.Add($"{r["Name"]}: {r["Request"]?.ToJsonString().Trim('"')}");
        // THE MODULE AND ITS SUB-AIMS, each by its own L3: an Implementation for this machine, the Terms of the data each obtains, and the builds by processing unit.
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" : "macOS";
        var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "ARM64" : "x86-64";
        var gpuWanted = false;
        foreach (var a in n.Aims.Select(x => x!.ToString()).Append(module))
        {
            var f = Path.Combine(amds, a + ".json");
            if (!File.Exists(f)) { n.CanRun = false; n.Why = $"the L3 of {a} is not on this machine"; continue; }
            var sub = JsonNode.Parse(File.ReadAllText(f))!.AsObject();
            var impls = sub["Implementations"]?.AsArray().OfType<JsonObject>().ToList() ?? new();
            if (impls.Count > 0 && !impls.Any(i => ((string?)i["OperatingSystem"] ?? os) == os && ((string?)i["Architecture"] ?? arch) == arch))
            { n.CanRun = false; n.Why = $"{a} has no Implementation for {os} {arch}"; }
            foreach (var s in sub["AccessSources"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            {
                if ((string?)s["Accelerator"] is { } acc && !n.Accelerators.Any(x => (string?)x == $"{a}: {acc}")) n.Accelerators.Add($"{a}: {acc}");
                if ((string?)s["Terms"]?["Licence"] is { } lic && !n.Licences.Any(x => (string?)x == lic)) n.Licences.Add(lic);
            }
            foreach (var r in sub["ResourcePolicies"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                if ((string?)r["Name"] == "GPU:Number" && r["Request"] is JsonValue v && v.TryGetValue<int>(out var want) && want >= 1) gpuWanted = true;
        }
        if (n.CanRun) n.Why = gpuWanted ? (gpu.Present ? $"yes - its speech recognition uses this machine's card ({gpu.Text})" : "yes - on the processor (no NVIDIA card)") : "yes";
    }
    catch (Exception ex) { n.CanRun = false; n.Why = "its L3 could not be read: " + ex.Message; }
    return n;
}

static string RepositoryRoot(string from)
{
    for (var d = new DirectoryInfo(from); d is not null; d = d.Parent)
        if (Directory.Exists(Path.Combine(d.FullName, "AIMs", "AMDs")) && Directory.Exists(Path.Combine(d.FullName, "Apps"))) return d.FullName;
    throw new InvalidOperationException("Thalia is not inside the MPAI software folder (no AIMs\\AMDs and Apps above " + from + ")");
}

sealed class Needs
{
    public JsonArray Aims { get; } = new();
    public JsonArray Licences { get; } = new();
    public JsonArray Resources { get; } = new();
    public JsonArray Accelerators { get; } = new();
    public bool CanRun { get; set; } = true;
    public string Why { get; set; } = "";
}

static class Shared { public static Process? Mas, Ollama; }

sealed record GpuInfo(bool Present, string Text);
static class Gpu
{
    public static GpuInfo Probe()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("nvidia-smi", "--query-gpu=name,compute_cap,memory.total --format=csv,noheader") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
            var line = p.StandardOutput.ReadLine(); p.WaitForExit(5000);
            return string.IsNullOrWhiteSpace(line) ? new(false, "no NVIDIA card") : new(true, line.Trim());
        }
        catch { return new(false, "no NVIDIA card"); }
    }
}

// ONE RUNNING APP: its processes, what it is doing, and where its client is.
sealed class Run(string key, string app, string where)
{
    public string Key { get; } = key; public string App { get; } = app; public string Where { get; } = where;
    public volatile string Phase = "starting";
    public volatile string? Url;
    public bool Cancelled { get; private set; }
    public bool Alive => !Cancelled && !Phase.StartsWith("failed", StringComparison.Ordinal);
    public ConcurrentQueue<string> Lines { get; } = new();
    private readonly List<(Process P, bool Keep)> processes = new();
    private readonly ConcurrentDictionary<Process, string> last = new();

    public Process Spawn(string name, string file, string arguments, string workDir, Dictionary<string, string>? env, bool keep = false)
    {
        var info = new ProcessStartInfo(file, arguments) { WorkingDirectory = workDir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        if (env is not null) foreach (var (k, v) in env) info.Environment[k] = v;
        var p = Process.Start(info) ?? throw new InvalidOperationException($"{name} could not be started");
        void Line(string? s) { if (s is null) return; last[p] = s; Lines.Enqueue($"[{name}] {s}"); while (Lines.Count > 2000) Lines.TryDequeue(out _); }
        p.OutputDataReceived += (_, e) => Line(e.Data);
        p.ErrorDataReceived  += (_, e) => Line(e.Data);
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        lock (processes) processes.Add((p, keep));
        return p;
    }

    public async Task<bool> WaitForAsync(Process p, string marker, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until && !Cancelled)
        {
            if (Lines.Any(l => l.Contains(marker, StringComparison.Ordinal))) return true;
            if (p.HasExited) return false;
            await Task.Delay(500);
        }
        return false;
    }

    public string LastLine(Process p) => last.TryGetValue(p, out var s) ? s : (p.HasExited ? $"it ended (exit code {p.ExitCode})" : "no output");

    public void Stop(string? why)
    {
        Cancelled = true;
        if (why is not null) Phase = why;
        lock (processes)
            foreach (var (p, keep) in processes)
                if (!keep) try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
    }

    public JsonObject Json() => new() { ["key"] = Key, ["app"] = App, ["where"] = Where, ["phase"] = Phase, ["url"] = Url };
}

static class NativeWindows
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    public static void MinimiseForeground() { var w = GetForegroundWindow(); if (w != IntPtr.Zero) ShowWindow(w, 6); }   // SW_MINIMIZE
}
