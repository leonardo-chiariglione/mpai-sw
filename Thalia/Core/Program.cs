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

// THE PROFILE (MPAI-THL V1.0 WD0.1, Profiles): Full - local and MAS-based Apps, a Controller on this machine (the default);
// Remote - MAS-based Apps only, no Controller here: the User's laptop or phone using the MPAI as a Service of the User's own machine, over Tailscale.
//   dotnet Thalia.dll --Profile Remote        (reads remote.json - {"Service": "https://...", "Token": "..."} - from the folder of the package)
var remote = args.Zip(args.Skip(1)).Any(p => p.First.Equals("--Profile", StringComparison.OrdinalIgnoreCase) && p.Second.Equals("Remote", StringComparison.OrdinalIgnoreCase));
var root = remote ? RemotePackageRoot(AppContext.BaseDirectory) : RepositoryRoot(AppContext.BaseDirectory);
string? masToken = null;
string? remoteService = null;
if (remote)
{
    var rj = Path.Combine(root, "remote.json");
    if (!File.Exists(rj)) throw new InvalidOperationException($"{rj} is missing: it names the Service of the User's machine and the User's token.");
    var j = JsonNode.Parse(File.ReadAllText(rj))!.AsObject();
    remoteService = (string?)j["Service"] ?? throw new InvalidOperationException("remote.json has no Service");
    masToken = (string?)j["Token"];
}
// The Launcher's page is in Thalia\Core\wwwroot (Full) or beside the program (Remote), wherever the program is started from.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = remote ? AppContext.BaseDirectory : Path.Combine(root, "Thalia", "Core") });
builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://localhost:5199");
var app = builder.Build();

var appsRoot = Path.Combine(root, "Apps");
var amds     = Path.Combine(root, "AIMs", "AMDs");
var thaliaData   = builder.Configuration["Data"] ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Thalia");     // --Data: another folder of the User's data (tests)
var prefsFile = builder.Configuration["Prefs"] ?? Path.Combine(thaliaData, "preferences.json");
JsonObject ReadPrefsEarly() { try { return File.Exists(prefsFile) ? (JsonNode.Parse(File.ReadAllText(prefsFile)) as JsonObject ?? new JsonObject()) : new JsonObject(); } catch { return new JsonObject(); } }
var masUrl   = new Uri(remoteService ?? builder.Configuration["Mas"] ?? (!remote ? (string?)ReadPrefsEarly()["masUrl"] : null) ?? "https://localhost:5105/");
var appIds   = (builder.Configuration["Apps"] ?? "MTS,MCA,AMQ,NPR").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var logs     = Path.Combine(Path.GetTempPath(), "Thalia");
Directory.CreateDirectory(logs);

// THE APPS THE USER OBTAINED (APP-4): each in its own folder under Thalia's data of this User, apart from the Apps that come with the software (appsRoot); installed.json says where each came from.
var userApps     = Path.Combine(thaliaData, "Apps");
var installedFile = Path.Combine(thaliaData, "installed.json");
Directory.CreateDirectory(userApps);
var storeUrl = new Uri(builder.Configuration["Store"] ?? (string?)ReadPrefsEarly()["storeUrl"] ?? "http://127.0.0.1:5020/");
var installed = new InstalledApps(installedFile);
string[] AllIds() => appIds.Concat(installed.Ids()).Distinct().ToArray();

var fromStore = new Dictionary<string, JsonObject>();                       // L3s asked of the Store, kept for the session
var controllerAims = Path.Combine(thaliaData, "Controller", "AIMs");       // what the Controller of this machine obtained from the Store: its folder of its own
var controllerDll = Path.Combine(root, "Thalia", "Controller", "bin", "Debug", "net10.0", "ThaliaController.dll");
var hostProject   = remote ? Path.Combine(root, "Host") : Path.Combine(root, "AIF", "UserAgent", "Clients", "Browser", "Host");
var hostDll       = remote ? Path.Combine(hostProject, "RcaWeb.Host.dll") : Path.Combine(hostProject, "bin", "Debug", "net10.0", "RcaWeb.Host.dll");
var hostExe       = Path.Combine(hostProject, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "RcaWeb.Host.exe" : "RcaWeb.Host");
var masProject    = Path.Combine(root, "Apps", "MAS", "Service", "src", "MasService.csproj");
var masConfig     = Path.Combine(root, "Apps", "MAS", "Service", "mas-server-AI.json");
var ollamaExe     = Path.Combine(root, "Support", "Ollama", "bin", RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ollama.exe" : "ollama");

// A Service on this machine's loopback has a development certificate, accepted; any other Service must present a certificate that is valid for its name.
var masHandler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (message, _, _, errors) => errors == System.Net.Security.SslPolicyErrors.None || (message.RequestUri?.IsLoopback ?? false) };
var masHttp = new HttpClient(masHandler) { Timeout = TimeSpan.FromSeconds(10) };
if (!string.IsNullOrEmpty(masToken)) masHttp.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", masToken);
var running = new ConcurrentDictionary<string, Run>();
var shownReset = 0;
var gpu = Gpu.Probe();

// ---- the catalogue (APP-2): local Apps and MAS-based Apps, each with the needs of its Module ----------------------------------------------------
app.MapGet("/api/catalogue", async () =>
{
    var local = remote ? AppCatalogue.Scan(null) : AppCatalogue.Scan(appsRoot, appIds);
    var obtained = remote ? AppCatalogue.Scan(null) : AppCatalogue.Scan(userApps, installed.Ids());
    var items = new JsonArray();
    foreach (var e in local.Apps.OrderBy(a => Array.IndexOf(appIds, a.Id)))
    {
        var item = Item(e.Id, e.Name, e.Description, "local", e.Module, $"/api/icon/local/{e.Id}", e);
        item["version"] = VersionOf(e); item["obtained"] = false;
        items.Add(item);
    }
    foreach (var e in obtained.Apps.Where(o => !local.Apps.Any(l => l.Id == o.Id)))          // what the User obtained, after what comes with the software
    {
        var item = Item(e.Id, e.Name, e.Description, "local", e.Module, $"/api/icon/local/{e.Id}", e);
        item["version"] = VersionOf(e); item["obtained"] = true; item["from"] = installed.Get(e.Id)?.From;
        items.Add(item);
    }

    string? masProblem = null;
    try
    {
        var list = JsonNode.Parse(await masHttp.GetStringAsync(new Uri(masUrl, "MPAI/AIFU/Apps")))!.AsArray();
        // EVERY App the instance offers (the User looks for an App there too), those of this Thalia's list first, in its order, then the others by name. The Module is known where the App is also in the software.
        var offered = list.OfType<JsonObject>().Where(a => (string?)a["id"] is not null).ToList();
        var known = remote ? local : AppCatalogue.Scan(appsRoot, offered.Select(a => (string)a["id"]!).ToArray());
        int Rank(string id) { var i = Array.IndexOf(AllIds(), id); return i < 0 ? int.MaxValue : i; }
        foreach (var a in offered.OrderBy(a => Rank((string)a["id"]!)).ThenBy(a => (string?)a["name"] ?? "", StringComparer.CurrentCultureIgnoreCase))
        {
            var id = (string)a["id"]!;
            var software = known.Apps.FirstOrDefault(l => l.Id == id);
            items.Add(Item(id, (string?)a["name"] ?? id, (string?)a["description"] ?? "", "mas", software?.Module, $"/api/icon/mas/{id}", software));
        }
    }
    catch (Exception ex) { masProblem = remote
        ? $"Your Service at {masUrl.Host} does not answer ({ex.GetBaseException().Message.Split('\n')[0]}). Is Tailscale connected here, and is Thalia-Service running on your desktop?"
        : $"The MPAI as a Service instance at {masUrl} does not answer ({ex.GetBaseException().Message.Split('\n')[0]}). Its Apps appear when it runs; Run on MAS starts it if it is on this machine."; }

    return Results.Json(new JsonObject
    {
        ["machine"] = new JsonObject { ["os"] = RuntimeInformation.OSDescription, ["arch"] = RuntimeInformation.OSArchitecture.ToString(), ["gpu"] = gpu.Text },
        ["profile"] = remote ? "remote" : "full",
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
        var e = AppCatalogue.Scan(appsRoot, new[] { id }).Apps.FirstOrDefault() ?? AppCatalogue.Scan(userApps, new[] { id }).Apps.FirstOrDefault();
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
    // An App of the MPAI as a Service instance is run from here whether or not it is also in this Thalia's own list: the catalogue shows every App the instance offers.
    if (where != "mas" && !AllIds().Contains(id)) return Results.BadRequest(new { error = $"{id} is not in the catalogue" });
    if (remote && where != "mas") return Results.BadRequest(new { error = "this is a Remote Thalia: it runs the Apps of MPAI as a Service only" });
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

// PAUSE AND RESUME (APP-6): Thalia asks the local Controller of the App, through the Controller-User Agent API, to pause the Module - every AIM completes what it is doing and waits - and to resume it.
// This Controller serves this one App for this one User, so the Module is theirs to pause. A MAS-based App's Module is the Service's, shared with others: its Pause is a label, not offered here.
app.MapPost("/api/pause/{id}", (string id) => Control(id, "Pause"));
app.MapPost("/api/resume/{id}", (string id) => Control(id, "Resume"));

app.MapGet("/api/runs", () => Results.Json(new JsonArray(running.Values.Select(r => (JsonNode)r.Json()).ToArray())));
app.MapGet("/api/log/{where}/{id}", (string where, string id) =>
    running.TryGetValue($"{where}:{id}", out var r) ? Results.Text(string.Join('\n', r.Lines.TakeLast(200))) : Results.NotFound());

// The User's choices (season, side of the functions, search scope, welcome): one small file for this User on this machine, so that they stay whatever window shows Thalia.
var prefsKeys = new[] { "season", "side", "welcomeHidden", "micLabel", "camLabel", "screenLabel", "masUrl", "storeUrl", "micCheck" };   // micCheck: what the last Find my microphone found (text)
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

// THE WINDOW OF AN APP, ON THE STAGE (2026-11-25). An App with a window of its own (ASM's) is shown where Thalia shows Apps: its page says where the stage is on the screen, and Thalia
// places the App's window there, in front of Thalia's (owned by it: it stays in front of Thalia and goes with it); hide=true hides it, when the User hides the App or shows another.
// A dialog (it cannot be resized) is only brought in front. Only the windows of the processes this run started, and of theirs.
app.MapPost("/api/place/{where}/{id}", (string where, string id, int x, int y, int w, int h, bool? hide, int? fx, int? fy, int? fw, int? fh) =>
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !running.TryGetValue($"{where}:{id}", out var run)) return Results.NotFound();
    return Results.Json(new { windows = ThaliaWindow.Place(run.Pids(), x, y, w, h, hide == true, fx ?? 0, fy ?? 0, fw ?? 0, fh ?? 0) });
});

// THE RESULTS OF AN APP, SHOWN IN THALIA (2026-11-25). An App that works with files (ARP, EES, EAE, CAS, ...) writes its results in a folder under the User's Downloads and says where;
// Thalia lists that folder and gives its files to its page (a WAV to play, a JSON to read), and opens it in Windows when the User asks. Only folders under Downloads.
string? UnderDownloads(string? path)
{
    if (string.IsNullOrWhiteSpace(path)) return null;
    var downloads = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")) + Path.DirectorySeparatorChar;
    try { var full = Path.GetFullPath(path); return full.StartsWith(downloads, StringComparison.OrdinalIgnoreCase) ? full : null; } catch { return null; }
}
app.MapGet("/api/results", (string dir) =>
{
    if (UnderDownloads(dir) is not { } d || !Directory.Exists(d)) return Results.NotFound();
    var files = Directory.GetFiles(d, "*", SearchOption.AllDirectories).Take(200)
        .Select(f => new { path = f, name = Path.GetRelativePath(d, f), size = new FileInfo(f).Length });
    return Results.Json(new { dir = d, files });
});
app.MapGet("/api/results/file", (string path) =>
{
    if (UnderDownloads(path) is not { } f || !File.Exists(f)) return Results.NotFound();
    var type = Path.GetExtension(f).ToLowerInvariant() switch
    {
        ".wav" => "audio/wav", ".mp3" => "audio/mpeg", ".json" => "application/json; charset=utf-8", ".txt" or ".log" or ".csv" => "text/plain; charset=utf-8",
        ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".html" => "text/plain; charset=utf-8", ".mp4" => "video/mp4", _ => "application/octet-stream"
    };
    return Results.File(f, type, enableRangeProcessing: true);
});
app.MapPost("/api/results/open", (string dir) =>
{
    if (UnderDownloads(dir) is not { } d || !Directory.Exists(d)) return Results.NotFound();
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{d}\"") { UseShellExecute = true });
    return Results.Ok();
});

// CLOSE: Thalia ends. The windows of the browser that show it (those titled Thalia) are closed, and Thalia stops with the Apps it started (ApplicationStopping below). Return to Windows only
// hides the window; this ends Thalia.
app.MapPost("/api/quit", () =>
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(300);                                                              // the answer goes first
        if (builder.Configuration["NoWindows"] != "true")                                    // --NoWindows true: a test instance leaves the windows alone
          foreach (var name in new[] { "msedge", "chrome" })
            foreach (var p in Process.GetProcessesByName(name))
                try { if (p.MainWindowTitle.StartsWith("Thalia", StringComparison.Ordinal)) p.CloseMainWindow(); } catch { }
        await Task.Delay(300);
        app.Lifetime.StopApplication();
    });
    return Results.Ok();
});

// The Store: finding, obtaining, updating and removing Apps (APP-4, APP-5, APP-8). A Remote Thalia runs the Apps of MPAI as a Service only, and has no Store.
StoreContext? storeContext = null;
if (!remote)
    StoreRoutes.Map(app, storeContext = new StoreContext
    {
        StoreUrl = storeUrl, Root = root, AppsRoot = appsRoot, UserApps = userApps, Data = thaliaData, Installed = installed, Amds = amds,
        ControllerAims = controllerAims, L3Of = L3Of,
        NeedsOf = Needs,
        IsRunning = id => running.Values.Any(r => r.App == id && r.Alive),
        IsBundled = id => appIds.Contains(id),
        ModuleOfLocal = id => (AppCatalogue.Scan(appsRoot, new[] { id }).Apps.FirstOrDefault() ?? AppCatalogue.Scan(userApps, new[] { id }).Apps.FirstOrDefault())?.Module
    });

// SETTINGS (APP-1): where Thalia looks. The MPAI as a Service instance and the Store the User uses: the address of each, tested before it is kept. A Remote Thalia has the Service of its package.
app.MapGet("/api/settings", () => Results.Json(new JsonObject
{
    ["profile"] = remote ? "remote" : "full", ["mas"] = masUrl.ToString(), ["store"] = remote ? null : storeUrl.ToString(),
    ["data"] = remote ? null : thaliaData, ["apps"] = remote ? null : userApps, ["fixed"] = remote
}));

app.MapPost("/api/settings/test", async (HttpRequest req) =>
{
    JsonObject? body = null;
    try { body = await JsonNode.ParseAsync(req.Body) as JsonObject; } catch { }
    var kind = (string?)body?["kind"]; var text = (string?)body?["url"];
    if (!Uri.TryCreate(text, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https")) return Results.Json(new { ok = false, text = "That is not an address (http:// or https://...)." });
    try
    {
        using var r = await masHttp.GetAsync(new Uri(u, kind == "store" ? "MPAI/Store/App" : "MPAI/AIFU/Apps"));
        if (!r.IsSuccessStatusCode) return Results.Json(new { ok = false, text = $"It answers, but not as {(kind == "store" ? "an MPAI Store" : "an MPAI as a Service instance")} does (HTTP {(int)r.StatusCode})." });
        var n = JsonNode.Parse(await r.Content.ReadAsStringAsync()) is JsonArray a ? a.Count : 0;
        return Results.Json(new { ok = true, text = kind == "store" ? $"It is an MPAI Store with {n} Apps." : $"It is an MPAI as a Service instance offering {n} Apps." });
    }
    catch (Exception ex) { return Results.Json(new { ok = false, text = "It does not answer: " + ex.GetBaseException().Message.Split('\n')[0] }); }
});

app.MapPost("/api/settings", async (HttpRequest req) =>
{
    if (remote) return Results.BadRequest(new { error = "A Remote Thalia uses the Service of its package." });
    JsonObject? body = null;
    try { body = await JsonNode.ParseAsync(req.Body) as JsonObject; } catch { }
    if (body is null) return Results.BadRequest();
    var changes = new JsonObject();
    foreach (var (key, prefKey) in new[] { ("mas", "masUrl"), ("store", "storeUrl") })
    {
        if (!body.ContainsKey(key)) continue;
        var text = (string?)body[key];
        if (string.IsNullOrWhiteSpace(text)) { changes[prefKey] = null; continue; }                               // empty: the address Thalia came with
        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https")) return Results.BadRequest(new { error = $"{key}: that is not an address." });
        changes[prefKey] = u.ToString();
    }
    lock (prefsLock)
    {
        var now = ReadPrefs();
        foreach (var (k, v) in changes) now[k] = v?.DeepClone();
        Directory.CreateDirectory(Path.GetDirectoryName(prefsFile)!);
        File.WriteAllText(prefsFile, now.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
    if (changes.ContainsKey("masUrl")) masUrl = new Uri((string?)changes["masUrl"] ?? builder.Configuration["Mas"] ?? "https://localhost:5105/");
    if (changes.ContainsKey("storeUrl") && storeContext is not null) { storeUrl = new Uri((string?)changes["storeUrl"] ?? builder.Configuration["Store"] ?? "http://127.0.0.1:5020/"); storeContext.StoreUrl = storeUrl; }
    shownReset++;
    return Results.Json(new JsonObject { ["mas"] = masUrl.ToString(), ["store"] = storeUrl.ToString() });
});

app.MapGet("/", () => Results.File(Path.Combine(app.Environment.WebRootPath, "launcher.html"), "text/html; charset=utf-8"));
// THE UNITS OF A BROWSER (AIF\UserAgent\PhysicalLayer\Browser\mpai-units.js): one file for every page, served from where it is in the repository, or, in the Remote package, from units\ beside the program.
app.MapGet("/mpai-units.js", (HttpContext ctx) =>
{
    var inRepository = Path.Combine(root, "AIF", "UserAgent", "PhysicalLayer", "Browser", "mpai-units.js");
    var beside = Path.Combine(AppContext.BaseDirectory, "units", "mpai-units.js");
    var file = File.Exists(inRepository) ? inRepository : beside;
    ctx.Response.Headers.CacheControl = "no-store";
    return File.Exists(file) ? Results.File(file, "text/javascript") : Results.NotFound();
});
// The four seasonal pictures of the Launcher (Sacra di San Michele, CC BY-SA 4.0) are kept in Access, with the data of the other standards: Access\THL\seasons.
var seasons = Path.Combine(root, "Access", "THL", "seasons");
if (Directory.Exists(seasons))
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(seasons), RequestPath = "/seasons" });
app.UseStaticFiles();

app.Lifetime.ApplicationStopping.Register(() => { foreach (var r in running.Values) r.Stop("Thalia closed"); try { if (Shared.Store is { HasExited: false }) Shared.Store.Kill(entireProcessTree: true); } catch { } });
// THALIA'S WINDOW IS THALIA'S (ThaliaWindow.cs): on the taskbar with Thalia's icon, apart from Edge; pinned, it starts this Thalia again (the demonstration or the User's)
if (!remote && builder.Configuration["NoWindows"] != "true")
{
    var demo = thaliaData.TrimEnd('\\', '/').EndsWith("Thalia-demo", StringComparison.OrdinalIgnoreCase);
    ThaliaWindow.Keep(Path.Combine(app.Environment.WebRootPath, "thalia.ico"), $"\"{Path.Combine(root, "Thalia", demo ? "Thalia-demo.bat" : "Thalia.bat")}\"",
                      () => app.Lifetime.ApplicationStopping.IsCancellationRequested);
}

Console.WriteLine($"=== Thalia (MPAI-THL V1.0, {(remote ? "Remote" : "Full")} Profile, first version) ===");
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
        // AN APP WITH ITS OWN CONTROLLER AND CLIENT (OwnClients.cs, N-49): started as its launcher of AllApps starts it, on a port Thalia chooses.
        if (!remote && run.Where == "local" && OwnClients.Of(run.App, root) is { } own) { await StartOwnAsync(run, own); return; }
        // An App of the MPAI as a Service instance that needs its own window (ASM, CAS, CPS, TMA: the browser client has not the devices they use): the window is started on this machine and told where
        // the instance is. The Controller is the instance's: none is started here.
        if (!remote && run.Where == "mas" && OwnClients.Of(run.App, root) is { } ownMas && (ownMas.ControllerExe is not null || ownMas.MasArgs is not null)) { await StartOwnAsync(run, ownMas, masUrl.ToString()); return; }
        if (!File.Exists(hostDll)) throw new InvalidOperationException($"the App client is not built: {hostDll} (dotnet build {hostProject})");
        var hostFile = remote && File.Exists(hostExe) ? hostExe : "dotnet";
        var hostArgs = hostFile == "dotnet" ? $"\"{hostDll}\" " : "";
        if (run.App == "MCA") await EnsureOllamaAsync(run);

        Uri service;
        if (run.Where == "local")
        {
            if (!File.Exists(controllerDll)) throw new InvalidOperationException($"the local Controller is not built: {controllerDll}");
            var port = FreePort();
            run.Phase = "starting its local Controller (its Module and AIMs load: up to a minute or two)";
            // An App the User obtained is in the User's folder of Apps; the Controller is told where.
            var appsArg = installed.Get(run.App) is not null && AppCatalogue.Scan(appsRoot, new[] { run.App }).Apps.Count == 0 ? $" --apps \"{userApps}\"" : "";
            // The Store and the Controller's folder of its own: a Module that is not in the software is obtained from the Store, verified, and run from its package (ThaliaController).
            var storeArg = $" --store {storeUrl.GetLeftPart(UriPartial.Authority)} --aims \"{controllerAims}\"";
            var controller = run.Spawn("controller", "dotnet", $"\"{controllerDll}\" --listen http://127.0.0.1:{port} --app {run.App}{appsArg}{storeArg}", root, null);
            if (!await run.WaitForAsync(controller, "[MAS] Listening", TimeSpan.FromMinutes(5)))
                throw new InvalidOperationException("its local Controller did not come up: " + run.LastLine(controller));
            service = new Uri($"http://127.0.0.1:{port}/");
            run.Controller = service;
        }
        else
        {
            if (!await AnswersAsync(masUrl))
            {
                if (!masUrl.IsLoopback) throw new InvalidOperationException(remote
                    ? $"your Service at {masUrl.Host} does not answer: is Tailscale connected here, and is Thalia-Service running on your desktop?"
                    : $"the MPAI as a Service instance {masUrl} does not answer");
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
        // Remote: the client is published (no development certificate on this machine): http on localhost is a secure context for the microphone and the camera.
        var scheme = remote ? "http" : "https";
        var hostEnv = new Dictionary<string, string>();
        if (!remote) hostEnv["ASPNETCORE_ENVIRONMENT"] = "Development";
        if (!string.IsNullOrEmpty(masToken) && run.Where == "mas") hostEnv["MPAI_MAS_TOKEN"] = masToken;   // the client adds it to what it passes to the Service
        var host = run.Spawn("client", hostFile, $"{hostArgs}--Service {service} --Urls {scheme}://localhost:{hostPort}", hostProject, hostEnv);
        if (!await run.WaitForAsync(host, "Now listening on", TimeSpan.FromMinutes(2)))
            throw new InvalidOperationException("the App's client did not come up: " + run.LastLine(host));
        run.Url = $"{scheme}://localhost:{hostPort}/app/{Uri.EscapeDataString(run.App)}";
        run.Phase = "running";
    }
    catch (Exception ex)
    {
        run.Phase = "failed: " + ex.Message;
        run.Stop(null);
    }
}

// THE APP'S OWN CONTROLLER, THEN ITS OWN CLIENT; when the client ends (the User closed its window), the App ends and its Controller is stopped.
async Task StartOwnAsync(Run run, OwnClients.Own own, string? serviceUrl = null)
{
    if (own.LanguageModel && serviceUrl is null) await EnsureOllamaAsync(run);
    string controller = serviceUrl?.TrimEnd('/') ?? "";
    var clientPort = FreePort();
    string Fill(string t) => t.Replace("{controller}", controller).Replace("{root}", root).Replace("{data}", thaliaData).Replace("{port}", clientPort.ToString()).Replace("{page}", (clientPort + 1).ToString());
    // On the instance of MPAI as a Service the Module's storage is the instance's: the window is not given a storage of its own.
    var clientArgs = serviceUrl is null ? own.ClientArgs : System.Text.RegularExpressions.Regex.Replace(own.ClientArgs, @"\s*--storage\s+""[^""]*""", "");
    if (serviceUrl is not null && own.MasArgs is { } masArgs) clientArgs += " " + masArgs;   // an App whose Controller is inside its program (AUA): told to use the instance's
    if (own.ControllerExe is { } exe && serviceUrl is null)
    {
        if (!File.Exists(exe)) throw new InvalidOperationException($"its Controller is not built: {exe} (dotnet build {own.ControllerProject})");
        var port = FreePort();
        run.Phase = "starting its Controller (its Module and AIMs load: up to a minute or two)";
        var p = run.Spawn("controller", exe, $"--listen http://127.0.0.1:{port} {Fill(own.ControllerArgs)}".TrimEnd(), root, null);
        var until = DateTime.UtcNow.AddMinutes(5);
        while (!await PortOpenAsync(port))
        {
            if (p.HasExited) throw new InvalidOperationException("its Controller ended: " + run.LastLine(p));
            if (DateTime.UtcNow > until || run.Cancelled) throw new InvalidOperationException("its Controller did not come up");
            await Task.Delay(500);
        }
        controller = $"http://127.0.0.1:{port}";
        run.Controller = new Uri(controller + "/");
    }
    if (own.ClientBuilt is { } built && !File.Exists(built)) throw new InvalidOperationException($"the App is not built: {built}");
    run.Phase = "starting the App's own client";
    var env = own.Env?.ToDictionary(kv => kv.Key, kv => Fill(kv.Value)) ?? new Dictionary<string, string>();
    env["MPAI_IN_THALIA"] = "1";          // the App's client knows it is in Thalia (ASM puts its lady in Thalia's pane)
    var client = run.Spawn("client", own.ClientFile, Fill(clientArgs), root, env);
    if (own.Page is { } page)
    {
        var until = DateTime.UtcNow.AddMinutes(3);
        while (!await PortOpenAsync(clientPort))
        {
            if (client.HasExited) throw new InvalidOperationException("the App ended: " + run.LastLine(client));
            if (DateTime.UtcNow > until || run.Cancelled) throw new InvalidOperationException("the App did not come up");
            await Task.Delay(500);
        }
        run.Url = Fill(page);
    }
    run.Phase = "running";
    _ = Task.Run(async () => { await client.WaitForExitAsync(); if (!run.Cancelled) run.Stop("ended: its window was closed (exit code " + client.ExitCode + ")"); });
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

async Task<IResult> Control(string id, string verb)
{
    if (!running.TryGetValue($"local:{id}", out var run) || !run.Alive || run.Controller is null || run.Phase != "running")
        return Results.BadRequest(new { error = $"{id} is not running on this machine." });
    var module = AppCatalogue.Scan(appsRoot, new[] { id }).Apps.FirstOrDefault()?.Module ?? AppCatalogue.Scan(userApps, new[] { id }).Apps.FirstOrDefault()?.Module;
    if (module is null) return Results.BadRequest(new { error = $"{id} names no Module." });
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var made = JsonNode.Parse(await (await http.PostAsync(new Uri(run.Controller, "MPAI/AIFU/Controller"), new StringContent("{}", System.Text.Encoding.UTF8, "application/json"))).Content.ReadAsStringAsync())!;
        var cid = (string)made["id"]!;
        var started = await http.PostAsync(new Uri(run.Controller, $"MPAI/AIFU/{cid}/MODULE/Start"), new StringContent(new JsonObject { ["module"] = module }.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));
        var mid = (string)JsonNode.Parse(await started.Content.ReadAsStringAsync())!["id"]!;
        var r = await http.GetAsync(new Uri(run.Controller, $"MPAI/AIFU/{cid}/MODULE/{mid}/{verb}"));
        if (!r.IsSuccessStatusCode) return Results.Json(new { error = $"The Controller did not {verb.ToLowerInvariant()} the Module: {(await r.Content.ReadAsStringAsync()).Trim()}" }, statusCode: 502);
        run.Paused = verb == "Pause";
        return Results.Json(run.Json());
    }
    catch (Exception ex) { return Results.Json(new { error = $"The Controller of {id} does not answer: {ex.GetBaseException().Message}" }, statusCode: 502); }
}

// The Version an App states in its descriptor (app.json).
static string VersionOf(Mpai.Mas.Server.AppCatalogue.Entry e)
{
    try { return (string?)JsonNode.Parse(e.Descriptor)?["Version"] ?? ""; } catch { return ""; }
}

// WHAT THE APP ITSELF NEEDS (APP-2): what it will ask the User for (devices and inputs) and what it keeps, as its app.json states them.
(JsonNode? Asks, string? Keeps) AsksOf(AppCatalogue.Entry? e)
{
    if (e is null) return (null, null);
    try { var d = JsonNode.Parse(e.Descriptor); return (d?["Asks"]?.DeepClone(), (string?)d?["Keeps"]); } catch { return (null, null); }
}

// AN APP THAT IS NOT STANDALONE (2026-11-26): the "Warning" of its app.json (VMS, bundled with Avatar-Based Videoconference).
string? WarningOf(AppCatalogue.Entry? e)
{
    if (e is null) return null;
    try { return (string?)JsonNode.Parse(e.Descriptor)?["Warning"]; } catch { return null; }
}

JsonObject Item(string id, string name, string description, string where, string? module, string icon, AppCatalogue.Entry? descriptor = null)
{
    var needs = Needs(module);
    var (asks, keeps) = AsksOf(descriptor);
    return new JsonObject
    {
        ["id"] = id, ["name"] = name, ["description"] = description, ["where"] = where, ["module"] = module, ["icon"] = icon, ["asks"] = asks, ["keeps"] = keeps, ["warning"] = WarningOf(descriptor),
        ["aims"] = needs.Aims, ["licences"] = needs.Licences, ["resources"] = needs.Resources, ["accelerators"] = needs.Accelerators,
        ["canRun"] = where == "mas" ? true : needs.CanRun, ["why"] = needs.Why
    };
}

// THE L3 OF AN AIM: in the software (AIMs\AMDs), or obtained by this machine's Controller (its folder of its own), or - only asked, not obtained - in the MPAI Store.
(JsonObject? L3, string Where) L3Of(string aim)
{
    foreach (var (folder, where) in new[] { (amds, "software"), (controllerAims, "controller") })
    {
        var f = Path.Combine(folder, aim + ".json");
        if (File.Exists(f)) { try { return (JsonNode.Parse(File.ReadAllText(f))!.AsObject(), where); } catch { } }
    }
    if (remote || aim.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return (null, "none");
    lock (fromStore)
    {
        if (fromStore.TryGetValue(storeUrl + aim, out var known)) return (known, "store");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var text = http.GetStringAsync(new Uri(storeUrl, "MPAI/Store/L3/" + Uri.EscapeDataString(aim))).GetAwaiter().GetResult();
            var l3 = JsonNode.Parse(text)!.AsObject();
            fromStore[storeUrl + aim] = l3;
            return (l3, "store");
        }
        catch { return (null, "none"); }
    }
}

// WHAT THE MODULE NEEDS, from its L3 (AIM Instance Metadata): its AIMs, the AIM Licences, the computing resources, and whether this machine can run it.
Needs Needs(string? module)
{
    var n = new Needs();
    if (module is null) { n.Why = "no Module named"; return n; }
    if (L3Of(module).L3 is not { } l3) { n.CanRun = false; n.Why = $"its L3 {module}.json is neither on this machine nor in the Store"; return n; }
    try
    {
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
            if (L3Of(a).L3 is not { } sub) { n.CanRun = false; n.Why = $"the L3 of {a} is neither on this machine nor in the Store"; continue; }
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

// The folder of a Remote package: the first one above this program that holds remote.json and the Host folder.
static string RemotePackageRoot(string from)
{
    for (var d = new DirectoryInfo(from); d is not null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "remote.json")) && Directory.Exists(Path.Combine(d.FullName, "Host"))) return d.FullName;
    throw new InvalidOperationException("The Remote package is not complete (no remote.json and Host folder above " + from + ")");
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

static class Shared { public static Process? Mas, Ollama, Store; }

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
    public volatile bool Paused;
    public Uri? Controller;                     // the local Controller of this App: where Thalia, as a User Agent, asks it to pause and resume
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

    public JsonObject Json() => new() { ["key"] = Key, ["app"] = App, ["where"] = Where, ["phase"] = Phase, ["url"] = Url, ["controller"] = Controller?.ToString(), ["paused"] = Paused };

    // the processes this run started that still run (their windows are the App's own)
    public int[] Pids() { lock (processes) return processes.Where(x => { try { return !x.P.HasExited; } catch { return false; } }).Select(x => x.P.Id).ToArray(); }
}

static class NativeWindows
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    public static void MinimiseForeground() { var w = GetForegroundWindow(); if (w != IntPtr.Zero) ShowWindow(w, 6); }   // SW_MINIMIZE
}
