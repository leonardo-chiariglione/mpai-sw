using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

// THE STORE SIDE OF THALIA (MPAI-THL, M3323 6): finding Apps in the MPAI Store, obtaining them (APP-4), updating and removing them (APP-8).
//
// An App is a Workflow written against one Module, with the descriptor that lets a User find it, understand it and consent to it. Obtaining an App is NOT obtaining its Module: the Module and its AIMs
// are the Controller's to obtain, with their AIM Licences (APP-5); Thalia never obtains AIMs itself. What Thalia does here is show the User what the App needs - the AIMs of its Module, what each
// obtains and under which licence, the resources and whether this machine meets them - and keep the App only when the User accepts. Removing an App removes the App and nothing of its Module.

// What the User obtained: one small file, so that it stays whatever window shows Thalia.
sealed class InstalledApps(string file)
{
    private readonly object gate = new();
    private JsonObject Read() { try { return File.Exists(file) ? (JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? new()) : new(); } catch { return new(); } }
    public string[] Ids() { lock (gate) return Read().Select(p => p.Key).ToArray(); }
    public Obtained? Get(string id)
    {
        lock (gate)
            return Read()[id] is JsonObject o ? new Obtained(id, (string?)o["version"] ?? "", (string?)o["from"] ?? "", (string?)o["at"] ?? "") : null;
    }
    public void Set(string id, string version, string from, IEnumerable<string> accepted)
    {
        lock (gate)
        {
            var all = Read();
            all[id] = new JsonObject { ["version"] = version, ["from"] = from, ["at"] = DateTimeOffset.UtcNow.ToString("o"), ["accepted"] = new JsonArray(accepted.Select(a => (JsonNode)a).ToArray()) };
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, all.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    public void Remove(string id)
    {
        lock (gate) { var all = Read(); if (all.Remove(id)) File.WriteAllText(file, all.ToJsonString(new JsonSerializerOptions { WriteIndented = true })); }
    }
}

sealed record Obtained(string Id, string Version, string From, string At);

sealed class StoreContext
{
    public required Uri StoreUrl { get; set; }          // the User may change it (Settings)
    public required string Root { get; init; }          // the software
    public required string AppsRoot { get; init; }      // the Apps that come with it
    public required string UserApps { get; init; }      // the Apps the User obtained
    public required string Data { get; init; }          // Thalia's data of this User
    public required InstalledApps Installed { get; init; }
    public required string Amds { get; init; }
    public required Func<string?, Needs> NeedsOf { get; init; }
    public required Func<string, bool> IsRunning { get; init; }
    public required Func<string, string?> ModuleOfLocal { get; init; }
    public required Func<string, bool> IsBundled { get; init; }     // an App that is in Thalia's catalogue with the software
}

static class StoreRoutes
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly SemaphoreSlim Starting = new(1, 1);

    public static void Map(WebApplication app, StoreContext c)
    {
        // The Apps of the Store, as this User sees them: which are already here, which are to obtain, which have a newer version.
        app.MapGet("/api/store/apps", async (string? search) =>
        {
            var problem = await EnsureStoreAsync(c);
            if (problem is not null) return Results.Json(new JsonObject { ["problem"] = problem, ["apps"] = new JsonArray() });
            JsonArray list;
            try { list = JsonNode.Parse(await Http.GetStringAsync(new Uri(c.StoreUrl, "MPAI/Store/App" + (string.IsNullOrWhiteSpace(search) ? "" : "?search=" + Uri.EscapeDataString(search)))))!.AsArray(); }
            catch (Exception ex) { return Results.Json(new JsonObject { ["problem"] = "The Store does not answer: " + ex.GetBaseException().Message, ["apps"] = new JsonArray() }); }
            foreach (var a in list.OfType<JsonObject>())
            {
                var id = (string)a["id"]!; var storeVersion = (string?)a["version"] ?? "";
                var held = c.Installed.Get(id);
                var bundled = c.IsBundled(id) && held is null;
                a["state"] = held is not null ? (Newer(storeVersion, held.Version) ? "update" : "obtained") : bundled ? "bundled" : "available";
                a["installedVersion"] = held?.Version;
            }
            return Results.Json(new JsonObject { ["problem"] = null, ["apps"] = list, ["store"] = c.StoreUrl.ToString() });
        });

        // WHAT THE APP NEEDS, before the User accepts (APP-2, APP-3, APP-5): its descriptor, the AIMs of its Module and what each obtains under which licence, the resources, and whether this machine meets them.
        app.MapGet("/api/store/apps/{id}", async (string id) =>
        {
            var problem = await EnsureStoreAsync(c);
            if (problem is not null) return Results.Json(new { problem }, statusCode: 503);
            JsonObject detail;
            try { detail = JsonNode.Parse(await Http.GetStringAsync(new Uri(c.StoreUrl, $"MPAI/Store/App/{Uri.EscapeDataString(id)}")))!.AsObject(); }
            catch (Exception ex) { return Results.Json(new { problem = $"The Store has no App {id}: {ex.GetBaseException().Message}" }, statusCode: 404); }
            var module = (string?)detail["module"];
            var needs = c.NeedsOf(module);
            detail["needs"] = new JsonObject { ["aims"] = needs.Aims.DeepClone(), ["licences"] = needs.Licences.DeepClone(), ["resources"] = needs.Resources.DeepClone(), ["accelerators"] = needs.Accelerators.DeepClone(), ["canRun"] = needs.CanRun, ["why"] = needs.Why };
            detail["sources"] = Sources(c, module, needs);
            detail["held"] = c.Installed.Get(id) is { } h ? new JsonObject { ["version"] = h.Version, ["at"] = h.At } : null;
            return Results.Json(detail);
        });

        // OBTAIN (APP-4): the App's package - its Workflow, its descriptor, its icon - into this User's folder of Apps; the User has accepted what the App needs. The Module is then asked of the Controller (APP-5).
        app.MapPost("/api/store/obtain/{id}", async (string id, HttpRequest req) =>
        {
            JsonObject? body = null;
            try { body = await JsonNode.ParseAsync(req.Body) as JsonObject; } catch { }
            if ((bool?)body?["accepted"] != true) return Results.BadRequest(new { error = "The User has not accepted what the App needs." });
            var problem = await EnsureStoreAsync(c);
            if (problem is not null) return Results.Json(new { error = problem }, statusCode: 503);
            if (c.IsRunning(id)) return Results.BadRequest(new { error = $"{id} is running: stop it first." });
            var version = (string?)body?["version"];
            byte[] package;
            try { package = await Http.GetByteArrayAsync(new Uri(c.StoreUrl, $"MPAI/Store/App/{Uri.EscapeDataString(id)}/package" + (version is null ? "" : "?version=" + Uri.EscapeDataString(version)))); }
            catch (Exception ex) { return Results.Json(new { error = $"The Store did not give the App: {ex.GetBaseException().Message}" }, statusCode: 502); }
            string? installedVersion;
            try { installedVersion = Install(c, id, package); }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or InvalidOperationException) { return Results.Json(new { error = "The package is not a valid App: " + ex.Message }, statusCode: 502); }
            var accepted = body?["licences"]?.AsArray().Select(l => (string?)l ?? "").Where(l => l.Length > 0) ?? Enumerable.Empty<string>();
            c.Installed.Set(id, installedVersion!, c.StoreUrl.ToString(), accepted);
            var module = c.ModuleOfLocal(id);
            return Results.Json(new JsonObject { ["id"] = id, ["version"] = installedVersion, ["module"] = module, ["controller"] = ObtainModule(c, module) });
        });

        // REMOVE (APP-8): the App only. Its Module, and the AIMs the Controller holds, stay: another App may use them.
        app.MapPost("/api/remove/{id}", (string id) =>
        {
            if (c.Installed.Get(id) is null) return Results.BadRequest(new { error = $"{id} comes with the software: it is not one the User obtained." });
            if (c.IsRunning(id)) return Results.BadRequest(new { error = $"{id} is running: stop it first." });
            var folder = Path.Combine(c.UserApps, id);
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch (IOException ex) { return Results.BadRequest(new { error = "The App's folder could not be removed: " + ex.Message }); }
            c.Installed.Remove(id);
            return Results.Json(new { id, removed = true, note = "Its Module stays with the Controller: another App may use it." });
        });
    }

    // A package unpacked into the User's folder of Apps, replacing the version held. The version it states is returned.
    static string Install(StoreContext c, string id, byte[] package)
    {
        var target = Path.Combine(c.UserApps, id);
        var staging = target + ".new";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        try
        {
            using (var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read))
                foreach (var e in zip.Entries.Where(e => e.Name.Length > 0))
                {
                    var name = Path.GetFileName(e.FullName);                           // a flat package: no path survives, so nothing can leave the folder
                    e.ExtractToFile(Path.Combine(staging, name), overwrite: true);
                }
            var manifest = Path.Combine(staging, "app.json");
            if (!File.Exists(manifest)) throw new InvalidOperationException("no app.json");
            var d = JsonNode.Parse(File.ReadAllText(manifest))!.AsObject();
            var version = (string?)d["Version"] ?? throw new InvalidOperationException("app.json has no Version");
            if (!Directory.EnumerateFiles(staging, "*.orch").Any()) throw new InvalidOperationException("no Workflow Description in the package");
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(staging, target);
            return version;
        }
        finally { try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { } }
    }

    // THE CONTROLLER OBTAINS THE MODULE (APP-5): Thalia asks, the Controller holds. In this reference the Controller is the one of the machine, which holds the L3 of the Module and of each of its AIMs.
    static JsonObject ObtainModule(StoreContext c, string? module)
    {
        var o = new JsonObject { ["module"] = module };
        if (module is null) { o["state"] = "the App names no Module"; return o; }
        var needs = c.NeedsOf(module);
        var missing = needs.Aims.Select(a => a!.ToString()).Where(a => !File.Exists(Path.Combine(c.Amds, a + ".json"))).ToList();
        if (!File.Exists(Path.Combine(c.Amds, module + ".json"))) missing.Insert(0, module);
        o["aims"] = needs.Aims.Count; o["missing"] = new JsonArray(missing.Select(m => (JsonNode)m).ToArray());
        o["state"] = missing.Count == 0 ? "held: the Controller of this machine has the Module and its AIMs" : "not held: " + string.Join(", ", missing);
        return o;
    }

    // What each AIM of the Module obtains, and under which licence: the Access Sources of the L3s, so that the User sees them before accepting.
    static JsonArray Sources(StoreContext c, string? module, Needs needs)
    {
        var list = new JsonArray();
        if (module is null) return list;
        foreach (var a in needs.Aims.Select(x => x!.ToString()).Append(module).Distinct())
        {
            var f = Path.Combine(c.Amds, a + ".json");
            if (!File.Exists(f)) continue;
            try
            {
                var l3 = JsonNode.Parse(File.ReadAllText(f))!.AsObject();
                foreach (var s in l3["AccessSources"]?.AsArray().OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                {
                    var uri = (string?)s["Uri"] ?? "";
                    list.Add(new JsonObject
                    {
                        ["aim"] = a, ["name"] = (string?)s["Name"], ["description"] = (string?)s["Description"],
                        ["from"] = Uri.TryCreate(uri, UriKind.Absolute, out var u) ? u.Host : uri,
                        ["licence"] = (string?)s["Terms"]?["Licence"], ["terms"] = (string?)s["Terms"]?["Uri"], ["personalData"] = (bool?)s["Terms"]?["PersonalData"] ?? false
                    });
                }
            }
            catch { }
        }
        return list;
    }

    static bool Newer(string a, string b)
    {
        static int[] N(string s) => s.Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        var x = N(a); var y = N(b);
        for (var i = 0; i < Math.Max(x.Length, y.Length); i++) { var l = i < x.Length ? x[i] : 0; var r = i < y.Length ? y[i] : 0; if (l != r) return l > r; }
        return false;
    }

    // THE STORE, started when it is needed if it is on this machine and not running (as the MPAI as a Service instance is): its own process and folder of this User, with the Apps that come with the software on its shelf.
    public static async Task<string?> EnsureStoreAsync(StoreContext c)
    {
        if (await AnswersAsync(c.StoreUrl)) return null;
        if (!c.StoreUrl.IsLoopback) return $"The MPAI Store at {c.StoreUrl.Host} does not answer.";
        await Starting.WaitAsync();
        try
        {
            if (await AnswersAsync(c.StoreUrl)) return null;
            var dll = new[] { Path.Combine(c.Root, "Thalia", "Store", "bin", "StoreService.dll"), Path.Combine(c.Root, "Store", "Service", "bin", "Debug", "net10.0", "StoreService.dll") }.FirstOrDefault(File.Exists);
            if (dll is null) return "The MPAI Store is not built on this machine (dotnet build Store\\Service\\StoreService.csproj -o Thalia\\Store\\bin).";
            var data = Path.Combine(c.Data, "Store"); Directory.CreateDirectory(data);
            var info = new ProcessStartInfo("dotnet", $"\"{dll}\" --Urls {c.StoreUrl.GetLeftPart(UriPartial.Authority)} --Root \"{data}\" --Schemas \"{Path.Combine(c.Root, "schemas")}\"") { WorkingDirectory = c.Root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            Shared.Store = Process.Start(info);
            Shared.Store!.OutputDataReceived += (_, _) => { }; Shared.Store.ErrorDataReceived += (_, _) => { };
            Shared.Store.BeginOutputReadLine(); Shared.Store.BeginErrorReadLine();
            for (var i = 0; i < 60 && !await AnswersAsync(c.StoreUrl); i++) await Task.Delay(1000);
            if (!await AnswersAsync(c.StoreUrl)) return "The MPAI Store did not come up.";
            await SeedAsync(c, data);
            return null;
        }
        finally { Starting.Release(); }
    }

    static async Task<bool> AnswersAsync(Uri store)
    {
        try { using var r = await Http.GetAsync(new Uri(store, "MPAI/Store/App")); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    // A Store with no App is given the Apps that come with the software (once), as its operator would publish them.
    static async Task SeedAsync(StoreContext c, string data)
    {
        try
        {
            if (JsonNode.Parse(await Http.GetStringAsync(new Uri(c.StoreUrl, "MPAI/Store/App")))!.AsArray().Count > 0) return;
            var keyFile = Path.Combine(data, "admin-key.txt");
            if (!File.Exists(keyFile)) return;
            var key = (await File.ReadAllTextAsync(keyFile)).Trim();
            // The Apps Thalia can run on this machine today: those whose Modules the local Controller of Thalia builds (the Apps of MPAI-MMC and MPAI-OSD that run in a browser client). The others have clients of their own.
            string[] runnable = ["AMC", "MCA", "MTS", "NPR", "AMQ", "ACR", "MAC"];
            foreach (var folder in Directory.EnumerateDirectories(c.AppsRoot).SelectMany(Directory.EnumerateDirectories).Where(f => runnable.Contains(Path.GetFileName(f))))
            {
                if (!File.Exists(Path.Combine(folder, "app.json")) || !Directory.EnumerateFiles(folder, "*.orch").Any()) continue;
                using var ms = new MemoryStream();
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
                    foreach (var f in Directory.EnumerateFiles(folder).Where(f => Path.GetFileName(f) == "app.json" || new[] { ".orch", ".svg", ".png", ".jpg" }.Contains(Path.GetExtension(f).ToLowerInvariant())))
                        zip.CreateEntryFromFile(f, Path.GetFileName(f));
                ms.Position = 0;
                using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(c.StoreUrl, "MPAI/Store/App/" + Path.GetFileName(folder))) { Content = new StreamContent(ms) };
                req.Content.Headers.ContentType = new("application/zip");
                req.Headers.Add("MPAI-Admin", key);
                using var _ = await Http.SendAsync(req);
            }
        }
        catch { }
    }
}
