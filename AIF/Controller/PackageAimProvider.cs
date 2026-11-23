using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.RegularExpressions;

using AIF.SharedStorage;
using AIF.Store;

namespace AIF.Controller;

// AIMs FROM PACKAGES - MPAI-MAS actions 10 to 13. An AIM's L3 names, in each of
// its Implementations entries, where its package is (ImplementationURI), what it
// is called (BinaryName) and what it runs on (Architecture, OperatingSystem).
// This provider takes the entry for the machine it is on, fetches the package into
// the SCI's cache, loads it in a load context of its own, finds the IAimPlugin it
// carries, and asks it to build the AIM.
//
// The framework's own types are NOT loaded from the package: a package carrying
// its own copy of AIF.Controller would produce an IAimProcessor the Controller
// could not recognise as one. They are shared with the program, and only what the
// package adds is loaded from it.
//
// It builds what it can. Asked for an AIM whose package is missing, or built for
// another machine, it says it cannot (CanCreate), and a provider beside it -
// today, one compiled into the Service - builds that AIM instead.
// WHAT THE CONTROLLER FINDS IN THE PACKAGE (an AIM of the Store runs from its package, and nothing of ours has to be in it). After the fingerprint is the Store's (Same), the
// Implementation BinaryName.dll is loaded and the Controller looks, in this order, for: (1) an IAimPlugin the package happens to carry - used, no longer required; (2) the entry of
// the standard, a public static method Module (ModuleEntry: this environment's binding of it); (3) glue of the Controller's own for that kind of AIM (an IPackageAdapter the
// Controller was given), which builds the AIM from what the assembly has - so that a package published as it is keeps running without its Implementer adding anything to it.
// An Implementation of a kind this Controller cannot host - another Architecture or Operating System than the machine's, or not a .NET assembly - is refused, with the reason,
// as FAILED: the Module does not start, and says why. No native or Python host exists here.
public sealed class PackageAimProvider : IAimProvider
{
    private readonly AmdStore store;
    private readonly string cache;
    private readonly Action<string> say;
    private readonly Dictionary<string, AimBuilder?> byAim = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<IPackageAdapter> adapters;
    private readonly Dictionary<string, string> notHosted = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Assembly> byPackage = new(StringComparer.OrdinalIgnoreCase);
    private readonly IImplementationVerifier? approvals;
    private readonly Dictionary<string, string> binaryOf = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> refused = new(StringComparer.OrdinalIgnoreCase);

    // approvals: the Store, when there is one to ask (MPAI_AIFS_VerifyImplementation). The Controller measures the binary of each package it has
    // fetched and the Store says whether that is the fingerprint it approved; a package it does not approve is not loaded and the AIM is refused -
    // not built by another provider in its place. Without it, nothing is verified.
    public PackageAimProvider(AmdStore store, string cacheFolder, Action<string>? say = null, IImplementationVerifier? approvals = null, IEnumerable<IPackageAdapter>? adapters = null)
    {
        this.adapters = (adapters ?? []).ToList();
        this.approvals = approvals;
        this.store = store;
        cache = cacheFolder;
        this.say = say ?? (_ => { });
        Directory.CreateDirectory(cache);
    }

    public bool CanCreate(string aimName) => PluginFor(aimName) is not null || refused.ContainsKey(aimName) || notHosted.ContainsKey(aimName);

    // The binary this provider fetched for the AIM, for the Controller to measure.
    public string? ImplementationOf(string aimName) => PluginFor(aimName) is not null && binaryOf.TryGetValue(aimName, out var path) ? path : null;

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings, ISharedStorage? storage) =>
        Create(aimName, settings, storage, null, null, null);

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings, ISharedStorage? storage,
        ISharedStorage? privateStorage, IRuledStorage? moduleStorage, IAccessReader? access)
    {
        var found = PluginFor(aimName);
        if (refused.TryGetValue(aimName, out var why)) throw new TrustRefusedException(why);
        if (notHosted.TryGetValue(aimName, out var cannot)) throw new ImplementationNotHostedException(cannot);
        if (found is not { } build)
            throw new InvalidOperationException($"No package provides {aimName}.");

        var processor = build(AimPortReader.Load(store, aimName), settings, access);

        // AN IMPLEMENTATION IS NAMED BY ITS INSTANCE. IAimPlugin.Create is not told
        // which Instance it is building, so a plug-in names its processor with the
        // standard AIM name (MMC-EDP-V2.5); the Controller runs Instances
        // (1MMC-EDP-V2.5-I01) and would not find it. Until the plug-in contract
        // carries the Instance identifier, the provider supplies it.
        return string.Equals(processor.InstanceId, aimName, StringComparison.Ordinal) ? processor
            : processor is IAimRunner runner ? new RunnerAsInstance(aimName, processor, runner)                 // an AIM that executes continuously stays one
            : new AsInstance(aimName, processor);
    }

    private class AsInstance(string instanceId, IAimProcessor inner) : IAimProcessor
    {
        public string InstanceId => instanceId;
        public System.Threading.Tasks.Task<Message> ProcessAsync(Message message) => inner.ProcessAsync(message);
    }

    private sealed class RunnerAsInstance(string instanceId, IAimProcessor inner, IAimRunner runner) : AsInstance(instanceId, inner), IAimRunner
    {
        public System.Threading.Tasks.Task RunAsync(IAimPorts ports, AimContext context) => runner.RunAsync(ports, context);
    }

    // 1MMC-TIQ-V2.5-I01 is an implementation of MMC-TIQ-V2.5, which is what a
    // plug-in declares.
    private static string StandardName(string id) =>
        Regex.Replace(Regex.Replace(id, @"^[0-9]+", ""), @"-I[0-9]+$", "");

    private AimBuilder? PluginFor(string aimName)
    {
        if (byAim.TryGetValue(aimName, out var known)) return known;
        AimBuilder? builder = null;
        try { builder = Load(aimName); }
        catch (Exception ex) { say($"  package for {aimName}: {ex.Message}"); }
        byAim[aimName] = builder;
        return builder;
    }

    private AimBuilder? Load(string aimName)
    {
        var identifier = store.FindByAimName(aimName);
        if (identifier is null) { say($"  package for {aimName}: it has no L3 here."); return null; }

        var l3 = store.GetAMD(identifier).RootElement;
        if (!l3.TryGetProperty("Implementations", out var impls) || impls.ValueKind != JsonValueKind.Array)
        {
            say($"  package for {aimName}: its L3 names no Implementations."); return null;
        }

        var elsewhere = new List<string>();
        foreach (var impl in impls.EnumerateArray())
        {
            string S(string n) => impl.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
            if (!ForThisMachine(S("Architecture"), S("OperatingSystem"))) { elsewhere.Add($"{S("OperatingSystem")} {S("Architecture")}".Trim()); continue; }
            var uri = S("ImplementationURI");
            var binary = S("BinaryName");
            if (uri.Length == 0 || binary.Length == 0) continue;

            var folder = Fetch(uri, identifier.ImplementationID);
            if (folder is null) continue;

            var assemblyPath = Path.Combine(folder, binary + ".dll");
            if (!File.Exists(assemblyPath))
            {
                // Not a .NET assembly this Controller names: say what it is, if the package has it in another form.
                var other = new[] { ".so", ".dylib", ".exe", ".py", "" }.Select(e => Path.Combine(folder, binary + e)).FirstOrDefault(File.Exists);
                if (other is not null) return NotHosted(aimName, $"the Implementation {Path.GetFileName(other)} of the package is not a .NET assembly (native code, or a script): this Controller hosts .NET builds only");
                say($"  package for {aimName}: {binary}.dll is not in {folder}."); continue;
            }

            // THE STORE SAYS WHETHER THIS IS THE BINARY IT APPROVED, before anything of the package is loaded.
            if (approvals is not null)
            {
                var fingerprint = ImplementationFingerprints.OfImplementation(assemblyPath);                 // the package: the binary and what it loads
                var answer = approvals.Verify(aimName, binary, fingerprint);
                if (answer != VerifyResult.Same)
                {
                    var why = answer == VerifyResult.Different
                        ? $"{aimName} not started: {binary}.dll of the package at {uri} is not the binary the Store approved (SHA-256 {fingerprint[..16]}...)."
                        : $"{aimName} not started: the Store approved no fingerprint of {binary}.dll, or could not be asked.";
                    say("  " + why);
                    refused[aimName] = why;
                    return null;
                }
            }
            binaryOf[aimName] = assemblyPath;
            if (!IsManaged(assemblyPath)) return NotHosted(aimName, $"{binary}.dll of the package is native code, not a .NET assembly: this Controller hosts .NET builds only");

            if (!byPackage.TryGetValue(assemblyPath, out var assembly))
                byPackage[assemblyPath] = assembly = new PackageContext(assemblyPath).LoadFromAssemblyPath(assemblyPath);

            var standard = StandardName(aimName);
            // (1) a plug-in the package carries: used if it is there, not required
            foreach (var type in ModuleEntry.Types(assembly))
            {
                if (type.IsAbstract || !typeof(IAimPlugin).IsAssignableFrom(type)) continue;
                if (Activator.CreateInstance(type) is not IAimPlugin candidate) continue;
                if (!string.Equals(candidate.AimName, standard, StringComparison.OrdinalIgnoreCase)) continue;
                say($"  {aimName,-22} from the package {binary} at {folder} (its plug-in)");
                return (ports, settings, access) => candidate.Create(ports, settings, access);
            }
            // (2) the entry of the standard
            if (ModuleEntry.Find(assembly) is { } entry)
            {
                say($"  {aimName,-22} from the package {binary} at {folder} (its entry {entry.DeclaringType?.FullName}.{ModuleEntry.Name})");
                return (ports, settings, _) => ModuleEntry.Start(aimName, entry, ports, settings);
            }
            // (3) the Controller's own glue for this kind of AIM
            if (adapters.FirstOrDefault(a => string.Equals(a.AimName, standard, StringComparison.OrdinalIgnoreCase)) is { } adapter)
            {
                say($"  {aimName,-22} from the package {binary} at {folder} (built by the Controller's own glue for {standard})");
                return (ports, settings, _) => adapter.Create(assembly, ports, settings) ?? throw new InvalidOperationException($"{aimName}: the Controller's glue for {standard} found nothing of it in {binary}.dll.");
            }
            say($"  package for {aimName}: {binary}.dll has no entry {ModuleEntry.Name}(), no plug-in for {standard}, and the Controller has no glue for it.");
        }
        if (elsewhere.Count > 0 && !binaryOf.ContainsKey(aimName))
            return NotHosted(aimName, $"its L3 names Implementations for {string.Join("; ", elsewhere)} only, and this machine is {RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture}: it cannot be run here");
        return null;
    }

    // An Implementation of a kind this Controller cannot host: the AIM is refused, with the reason (the Module does not start).
    private AimBuilder? NotHosted(string aimName, string why)
    {
        var text = $"{aimName} not started: {why}.";
        say("  " + text);
        notHosted[aimName] = text;
        return null;
    }

    private static bool IsManaged(string file)
    {
        try { using var stream = File.OpenRead(file); using var pe = new System.Reflection.PortableExecutable.PEReader(stream); return pe.HasMetadata; }
        catch (BadImageFormatException) { return false; }
    }

    private static bool ForThisMachine(string architecture, string operatingSystem) => ExecutionEnvironment.IsHere(architecture, operatingSystem);

    // THE PACKAGE, IN THE SCI'S CACHE. A file: URI is copied from where the
    // Implementer put it; an http(s) URI is downloaded, and a .zip unpacked.
    // Fetched once: a package already in the cache is used as it is.
    private string? Fetch(string uri, string implementationId)
    {
        var into = Path.Combine(cache, implementationId);
        if (Directory.Exists(into) && Directory.EnumerateFiles(into, "*.dll").Any()) return into;

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) { say($"  package: '{uri}' is not an absolute URI."); return null; }
        try
        {
            if (parsed.IsFile)
            {
                var from = parsed.LocalPath;
                if (Directory.Exists(from))
                {
                    Directory.CreateDirectory(into);
                    foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
                    {
                        var to = Path.Combine(into, Path.GetRelativePath(from, file));
                        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                        File.Copy(file, to, overwrite: true);
                    }
                    return into;
                }
                if (File.Exists(from) && from.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ZipFile.ExtractToDirectory(from, into, overwriteFiles: true);
                    return into;
                }
                say($"  package: nothing at {uri}.");
                return null;
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var bytes = http.GetByteArrayAsync(parsed).GetAwaiter().GetResult();
            Directory.CreateDirectory(into);
            var temp = Path.Combine(into, "package.zip");
            File.WriteAllBytes(temp, bytes);
            ZipFile.ExtractToDirectory(temp, into, overwriteFiles: true);
            File.Delete(temp);
            return into;
        }
        catch (Exception ex) { say($"  package at {uri}: {ex.Message}"); return null; }
    }

    // A package's own load context: what the package adds comes from the package,
    // and everything the program already has is shared with it.
    private sealed class PackageContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver resolver;
        public PackageContext(string assemblyPath) : base(isCollectible: false) => resolver = new AssemblyDependencyResolver(assemblyPath);

        // WHAT RUNS IS WHAT WAS MEASURED (MPAI-GME Operation, 5; Zero Trust and Profiles 5): an assembly the package has is loaded from the package - the fingerprint the Store
        // approved covers it - except MPAI-AIF itself (AIF.*), which the Controller gives every package, so that the types the Controller and the AIM share are one.
        // Only what the package does not have (the .NET runtime) comes from the program.
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name is { } n && !n.StartsWith(ImplementationFingerprints.FrameworkPrefix, StringComparison.Ordinal) && resolver.ResolveAssemblyToPath(name) is { } own)
                return LoadFromAssemblyPath(own);
            try
            {
                var shared = Default.LoadFromAssemblyName(name);
                if (shared is not null) return shared;
            }
            catch (FileNotFoundException) { /* the program does not have it: the package must */ }
            var path = resolver.ResolveAssemblyToPath(name);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string name)
        {
            var path = resolver.ResolveUnmanagedDllToPath(name);
            return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}

// How the Controller builds an AIM from the Implementation it loaded, once the Store has said it is the one it approved.
public delegate IAimProcessor AimBuilder(AimPortReader ports, IReadOnlyDictionary<string, string> settings, IAccessReader? access);

// THE CONTROLLER'S OWN GLUE for a kind of AIM: it builds the AIM from what the assembly of the package has, so that a package that carries neither a plug-in nor the entry of the
// standard still runs. The glue is the Controller's, not the package's: the package stays what its Implementer published.
public interface IPackageAdapter
{
    // The standard AIM name it is glue for, e.g. "AIH-ADT-V1.1".
    string AimName { get; }

    // The AIM, or null where the assembly does not have what the glue needs.
    IAimProcessor? Create(Assembly implementation, AimPortReader ports, IReadOnlyDictionary<string, string> settings);
}

// An Implementation of a kind this Controller cannot host (another Architecture or Operating System than the machine's, native code, a script): the Module is not started, and the reason is given.
public sealed class ImplementationNotHostedException(string message) : InvalidOperationException(message);
