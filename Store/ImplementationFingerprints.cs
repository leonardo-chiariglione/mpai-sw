using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIF.Store;

// THE FINGERPRINTS OF APPROVED IMPLEMENTATIONS (Phase 13, M3223 3.2; OI-9). When
// the Store approves an L3 it records, for each Implementation the L3 names by its
// BinaryName, the SHA-256 of that binary as the Implementer submitted it. A
// Controller that verifies what it runs compares the binary it is about to run with
// this record: the Store is the guarantor that an Implementation is what its
// Implementer built (M3205 3.7).
//
// Kept beside the L3s, in approved/<AIMName>.json - the Store's record, not the
// Implementer's Metadata.
// WHAT THE STORE SAYS OF A FINGERPRINT IT IS ASKED ABOUT (MPAI_AIFS_VerifyImplementation). The real fingerprint of an approved Implementation is the Store's alone: a Controller measures
// what it has obtained, asks, and is told. It is never given the fingerprint to compare for itself.
public enum VerifyResult { Same, Different, NotRecorded }

public interface IImplementationVerifier
{
    VerifyResult Verify(string aimName, string binaryName, string fingerprint);

    // For the version of the AIM Instance Metadata the Controller holds (the version the Store published it as): what the Store approved for that version. A verifier that keeps no versions answers as for the latest.
    VerifyResult Verify(string aimName, string binaryName, string fingerprint, string? version) => Verify(aimName, binaryName, fingerprint);
}

public sealed class ImplementationFingerprints : IImplementationVerifier
{
    public const string Folder = "approved";
    private readonly string folder;

    public ImplementationFingerprints(string storeFolder) => folder = Path.Combine(storeFolder, Folder);

    // The fingerprint of a file - of a model, of a data file: SHA-256, hexadecimal, upper case.
    public static string Of(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    // THE FINGERPRINT OF AN IMPLEMENTATION IS THAT OF ITS PACKAGE (MPAI-GME V2.1 Operation, 5, as defined on 2026-11-14). The package is every file at the location the
    // AIM Instance Metadata states - the files of an archive, the files of a folder, or the one file - whether or not the Implementation loads it. For each file:
    // its path (relative to the root of the package, "/" between folders, no leading "/" or "./", UTF-8) and the SHA-256 of its content (64 lower-case hex digits);
    // one line "path SP hash LF" per file; the lines in ascending order of the bytes of their paths; the fingerprint is the SHA-256 of the lines joined, lower-case.
    // Calculated the same way by the Store (from the package as the Implementer keeps it) and by the Controller, or what executes an AIM apart from it (from the
    // package as it obtained it, before anything of it is loaded).

    // The package of an Implementation whose binary is on disk: the folder that holds the binary, every file in it at any depth - an obtained package is unpacked into
    // a folder of its own; an AIM built into a program has the program's folder as its package.
    public static string OfImplementation(string binaryFile) => OfDirectory(Path.GetDirectoryName(Path.GetFullPath(binaryFile))!);

    // A package that is a folder: every file in it, at any depth.
    public static string OfDirectory(string folder)
    {
        var root = Path.GetFullPath(folder);
        return OfFiles(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                                .Select(f => (PackagePath(Path.GetRelativePath(root, f)), HashOf(f))));
    }

    // A package that is one file: its file name is its path.
    public static string OfSingleFile(string file) => OfFiles([(Path.GetFileName(file), HashOf(file))]);

    // A package that is a .zip: every file of the archive (a directory is not a file); null if the binary binaryName (BinaryName.dll, or BinaryName) is not in it.
    public static string? OfPackage(Stream zip, string binaryName)
    {
        using var archive = new System.IO.Compression.ZipArchive(zip, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
        var files = new List<(string Path, string Sha256)>();
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
            using var stream = entry.Open();
            files.Add((PackagePath(entry.FullName), Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()));
        }
        var names = files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        return names.Contains(binaryName + ".dll") || names.Contains(binaryName) || names.Any(n => n.EndsWith("/" + binaryName + ".dll", StringComparison.Ordinal)) ? OfFiles(files) : null;
    }

    // The path of a file in a package as the method writes it: "/" between folders, no leading "/" or "./".
    public static string PackagePath(string path)
    {
        var p = path.Replace('\\', '/');
        while (p.StartsWith("./", StringComparison.Ordinal) || p.StartsWith('/')) p = p.StartsWith("./", StringComparison.Ordinal) ? p[2..] : p[1..];
        return p;
    }

    // The SHA-256 of a file on disk, lower-case hex, kept while the file is unchanged (a package is measured at every Start).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Path, long Length, DateTime Written), string> known = new();
    private static string HashOf(string file)
    {
        var info = new FileInfo(file);
        return known.GetOrAdd((info.FullName, info.Length, info.LastWriteTimeUtc), _ => { using var s = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant(); });
    }

    // The ascending order of the bytes of the paths (UTF-8).
    private sealed class Utf8ByteOrder : IComparer<string>
    {
        public static readonly Utf8ByteOrder Instance = new();
        public int Compare(string? x, string? y) => System.Text.Encoding.UTF8.GetBytes(x ?? "").AsSpan().SequenceCompareTo(System.Text.Encoding.UTF8.GetBytes(y ?? ""));
    }

    // FOR BUILDING A PACKAGE, NOT FOR ITS FINGERPRINT: the files of a folder a .NET binary loads - the binary and the assemblies it refers to, at any depth, that the
    // folder has - without the assemblies of MPAI-AIF itself (AIF.*), which the Controller gives every package. (read: the bytes of a file, or null.)
    public const string FrameworkPrefix = "AIF.";
    public static IReadOnlyList<(string Path, string Sha256)> PackageFiles(string binary, Func<string, byte[]?> read, Func<string, byte[], (string, string[])?>? known = null)
    {
        var found = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var todo = new Stack<string>([binary]);
        while (todo.TryPop(out var name))
        {
            if (found.ContainsKey(name) || read(name) is not { } bytes) continue;
            var (sha, references) = known?.Invoke(name, bytes) ?? (Convert.ToHexString(SHA256.HashData(bytes)), References(bytes).ToArray());
            found[name] = sha;
            foreach (var reference in references)
                if (!reference.StartsWith(FrameworkPrefix, StringComparison.Ordinal)) todo.Push(reference + ".dll");
        }
        return found.Select(f => (f.Key, f.Value)).ToList();
    }

    private static IEnumerable<string> References(byte[] assembly)
    {
        try
        {
            using var pe = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(assembly));
            if (!pe.HasMetadata) return [];
            var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
            return metadata.AssemblyReferences.Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name)).ToList();
        }
        catch (BadImageFormatException) { return []; }                           // not a .NET assembly: a package of one file
    }

    // The fingerprint of a list of files (path, SHA-256): the lines "path SP hash LF" in ascending byte order of the paths, hashed; lower-case hex (MPAI-GME Operation, 5).
    public static string OfFiles(IEnumerable<(string Path, string Sha256)> files) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Concat(
            files.Select(f => (Path: PackagePath(f.Path), Sha: f.Sha256.ToLowerInvariant())).OrderBy(f => f.Path, Utf8ByteOrder.Instance).Select(f => f.Path + " " + f.Sha + "\n"))))).ToLowerInvariant();

    // Records the fingerprint of the binary that implements an AIM, as approved.
    public void Approve(string aimName, string binaryName, string fingerprint, DateTimeOffset at, string? version = null)
    {
        Directory.CreateDirectory(folder);
        var path = PathOf(aimName);
        var record = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))!.AsObject()
                                       : new JsonObject { ["AIMName"] = aimName, ["Implementations"] = new JsonObject() };
        var approved = new JsonObject { ["Algorithm"] = "SHA-256", ["Fingerprint"] = fingerprint, ["Approved"] = at.ToString("O") };
        record["Implementations"]![binaryName] = approved.DeepClone();              // the latest
        if (version is not null)
        {
            // Every version keeps what was approved for it: a new version does not take away the approval of the one a Controller still holds (STR-3; Verification, 2).
            var versions = record["Versions"] as JsonObject ?? new JsonObject();
            var of = versions[version] as JsonObject ?? new JsonObject();
            of[binaryName] = approved.DeepClone();
            versions[version] = of;
            record["Versions"] = versions;
        }
        File.WriteAllText(path, record.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    // The Store's answer to a fingerprint calculated elsewhere: the one it recorded for that binary of that AIM, or not.
    public VerifyResult Verify(string aimName, string binaryName, string fingerprint) =>
        Approved(aimName, binaryName) is not { } recorded ? VerifyResult.NotRecorded
        : string.Equals(recorded, fingerprint, StringComparison.OrdinalIgnoreCase) ? VerifyResult.Same : VerifyResult.Different;

    public VerifyResult Verify(string aimName, string binaryName, string fingerprint, string? version)
    {
        var path = PathOf(aimName);
        // A record made before versions were kept has none: it answers as for the latest.
        if (version is null || !File.Exists(path) || JsonNode.Parse(File.ReadAllText(path))?["Versions"] is not JsonObject versions) return Verify(aimName, binaryName, fingerprint);
        return (string?)versions[version]?[binaryName]?["Fingerprint"] is not { } recorded ? VerifyResult.NotRecorded
             : string.Equals(recorded, fingerprint, StringComparison.OrdinalIgnoreCase) ? VerifyResult.Same : VerifyResult.Different;
    }

    // The binaries of an AIM the Store has a fingerprint of (their names; the fingerprints stay with the Store).
    public IReadOnlyList<string> Binaries(string aimName)
    {
        var path = PathOf(aimName);
        return File.Exists(path) && JsonNode.Parse(File.ReadAllText(path))?["Implementations"] is JsonObject list ? list.Select(p => p.Key).ToList() : [];
    }

    // The approved fingerprint of the binary an AIM's L3 names; null where the Store
    // approved none. Only the Store reads it: a Controller asks Verify.
    public string? Approved(string aimName, string binaryName)
    {
        var path = PathOf(aimName);
        if (!File.Exists(path)) return null;
        return (string?)JsonNode.Parse(File.ReadAllText(path))?["Implementations"]?[binaryName]?["Fingerprint"];
    }

    private string PathOf(string aimName) => Path.Combine(folder, aimName + ".json");
}
