using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mpai.StoreService;

// THE APPS OF THE STORE. An App is at another level than the Module (M3323, 6): it is a Workflow written against one Module, with the descriptor that lets a User find it, understand it and
// consent to it (app.json), its icon and, if it brings one, its client. Several Apps may be written against the same Module, so obtaining an App is not obtaining its Module: the Module and its
// AIMs are the Controller's to obtain (APP-5), with their AIM Licences. The Store keeps each version of an App as the package it was published as, and never overwrites one (as for an L3).
//
//   <Root>/apps/<id>/<version>/package.zip    the App: app.json, its Workflow (*.orch), its icon, the rest of its folder
//
// The identifier of an App is the name of its folder. A package is refused when it has no app.json with a Name, a Version and a Module, or no Workflow Description.
public sealed class AppShelf
{
    public sealed record Entry(string Id, string Name, string Description, string Version, string? Module, string? Standard, string? UseCase, string[] Keywords, long Size, string Descriptor);

    private readonly string folder;
    public AppShelf(string root) { folder = Path.Combine(root, "apps"); Directory.CreateDirectory(folder); }

    public int Count => Directory.EnumerateDirectories(folder).Count();

    // The latest version of each App, by name or by what it says it does.
    public IReadOnlyList<Entry> Latest(string? search)
    {
        var list = new List<Entry>();
        foreach (var dir in Directory.EnumerateDirectories(folder))
            if (Versions(Path.GetFileName(dir)).LastOrDefault() is { } v && Get(Path.GetFileName(dir), v) is { } e) list.Add(e);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var words = search.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            list = list.Where(e => words.All(w => $"{e.Id} {e.Name} {e.Description} {e.Module} {e.Standard} {string.Join(' ', e.Keywords)}".ToLowerInvariant().Contains(w))).ToList();
        }
        return list.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<string> Versions(string id)
    {
        var dir = Path.Combine(folder, Safe(id));
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateDirectories(dir).Select(Path.GetFileName).Select(s => s!).OrderBy(VersionKey).ThenBy(s => s, StringComparer.Ordinal).ToList();
    }

    public Entry? Get(string id, string? version = null)
    {
        version ??= Versions(id).LastOrDefault();
        if (version is null) return null;
        var package = PackageFile(id, version);
        if (!File.Exists(package)) return null;
        using var zip = ZipFile.OpenRead(package);
        var manifest = zip.GetEntry("app.json");
        if (manifest is null) return null;
        using var reader = new StreamReader(manifest.Open());
        var text = reader.ReadToEnd();
        var d = JsonNode.Parse(text)!.AsObject();
        return new Entry(id, (string?)d["Name"] ?? id, (string?)d["Description"] ?? "", version, (string?)d["Module"], (string?)d["Standard"], (string?)d["UseCase"],
                         d["Keywords"]?.AsArray().Select(k => (string?)k ?? "").Where(k => k.Length > 0).ToArray() ?? [], new FileInfo(package).Length, text);
    }

    public byte[]? Package(string id, string? version = null)
    {
        version ??= Versions(id).LastOrDefault();
        return version is not null && File.Exists(PackageFile(id, version)) ? File.ReadAllBytes(PackageFile(id, version)) : null;
    }

    // Publishes a package as the version its app.json states. Null when published, else why not.
    public string? Publish(string id, byte[] package, out string version)
    {
        version = "";
        if (id.Length == 0 || id != Safe(id)) return "The App's identifier is the name of its folder: letters, digits, - and _.";
        try
        {
            using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
            if (zip.GetEntry("app.json") is not { } manifest) return "The package has no app.json.";
            JsonObject d;
            using (var reader = new StreamReader(manifest.Open())) d = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
            if (string.IsNullOrWhiteSpace((string?)d["Name"])) return "app.json has no Name.";
            if (string.IsNullOrWhiteSpace((string?)d["Version"])) return "app.json has no Version.";
            if (string.IsNullOrWhiteSpace((string?)d["Module"])) return "app.json names no Module: an App is a Workflow written against one.";
            if (!zip.Entries.Any(e => e.FullName.EndsWith(".orch", StringComparison.OrdinalIgnoreCase))) return "The package has no Workflow Description (*.orch).";
            version = (string)d["Version"]!;
            if (version != Safe(version)) return "The Version is made of letters, digits, . - and _.";
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or InvalidOperationException) { return "The package is not a valid App package: " + ex.Message; }
        var dir = Path.Combine(folder, id, version);
        if (File.Exists(Path.Combine(dir, "package.zip"))) return $"{id} {version} is already in the Store: a version is never overwritten.";
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "package.zip"), package);
        return null;
    }

    private string PackageFile(string id, string version) => Path.Combine(folder, Safe(id), Safe(version), "package.zip");

    private static string Safe(string s) => new string(s.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());

    // 1.0 < 1.1 < 1.10 < 2.0: by the numbers of the version.
    public static string VersionKey(string? v) => string.Join('.', (v ?? "").Split('.').Select(p => int.TryParse(p, out var n) ? n.ToString("D6") : p));
}
