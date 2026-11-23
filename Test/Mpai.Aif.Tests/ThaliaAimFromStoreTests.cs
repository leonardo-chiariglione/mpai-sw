using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;

using AIF.Controller;
using AIF.Store;
using Mpai.Aif.Api;
using Mpai.Thalia.Controller;

namespace Mpai.Aif.Tests;

// THE AIM OF AN IMPLEMENTER, FROM THE MPAI STORE, RUN IN A MODULE (N-45; Thalia). The package an Implementer publishes holds the Implementation the AIM Metadata names (BinaryName) and nothing
// of ours: no plug-in is required. A Controller with a folder of its own (the one ThaliaController keeps for the Store's AIMs) obtains the L3, unpacks the package, measures it and asks the
// Store whether that is what it approved (Same) before anything of it is loaded; then it finds how to build the AIM: the entry Module of the standard (ModuleEntry), or - for a package
// published as it is, with neither - glue of the Controller's own (PackageAdapters). An Implementation of a kind it cannot host is refused with the reason.
[Trait("Group", "Fast")]
[Trait("Blocks", "Yes")]
public class ThaliaAimFromStoreTests
{
    private const string Aim = "2AIH-ADT-V1.1-I01", Binary = "Mpai.Aih.Adt";

    // The package as the Implementer published it: the binary and its .deps.json, in one zip - as the zip of Implementer 2 in the Store of the author is made.
    private static string Package(string packages)
    {
        var build = Path.Combine(Repository.Root, "AIMs", "AIH", "V1.1", "ADT", "bin", "Debug", "net10.0");
        Skip.IfNot(File.Exists(Path.Combine(build, Binary + ".dll")), $"{Binary} is not built.");
        return StoreTests.Zip(packages, "AIH-ADT-V1.1-I01", (Binary + ".dll", File.ReadAllBytes(Path.Combine(build, Binary + ".dll"))), (Binary + ".deps.json", File.ReadAllBytes(Path.Combine(build, Binary + ".deps.json"))));
    }

    // The L3 of an Implementer: that of the reference AIM under the Implementer's name, with the package the Store is to approve.
    private static JsonNode L3(string implementerId, string uri, string implementation = "AIH-ADT-V1.1-I01", string binary = Binary, string? operatingSystem = null, string? architecture = null)
    {
        var l3 = JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Amds, "1AIH-ADT-V1.1-I01.json")))!;
        l3["Identifier"]!["ImplementerID"] = implementerId;
        l3["Identifier"]!["ImplementationID"] = implementation;
        l3["Identifier"]!["AIMInstID"] = implementerId + implementation;
        var impl = l3["Implementations"]![0]!;
        impl["ImplementationURI"] = uri; impl["BinaryName"] = binary;
        if (operatingSystem is not null) impl["OperatingSystem"] = operatingSystem;
        if (architecture is not null) impl["Architecture"] = architecture;
        return l3;
    }

    // A Controller as ThaliaController makes it for a Module of the Store: a folder of its own, the Store as its source and its verifier, the package provider first, with the Controller's own glue.
    private static ControllerApi Controller(string folder, HttpAimSource source, List<string> said)
    {
        Directory.CreateDirectory(folder);
        var repository = new AimRepository(folder);
        var api = new ControllerApi(folder, Path.Combine(Repository.Root, "AIMs", "aim-settings.json"),
            s => new CompositeProvider(new PackageAimProvider(s, Path.Combine(folder, "packages"), line => { lock (said) said.Add(line); }, source, PackageAdapters.All)));
        api.Repository = repository; api.Source = source;
        return api;
    }

    // THE TINY AIM WRITTEN OUTSIDE OUR CODE (Test\ForeignAim): built here, to a folder of this test's own, referencing none of our libraries.
    private static string BuildForeign(string into, string? shape = null)
    {
        var project = Path.Combine(Repository.Root, "Test", "ForeignAim", "Foreign.Aim.csproj");
        using var p = Process.Start(new ProcessStartInfo("dotnet", $"build \"{project}\" -o \"{into}\" -nologo -v q {(shape is null ? "" : "-p:Shape=" + shape)} -p:BaseIntermediateOutputPath={into.Replace('\\', '/')}-obj/")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true })!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, "the foreign AIM did not build: " + output);
        Assert.DoesNotContain(Directory.GetFiles(into, "*.dll").Select(Path.GetFileName), n => n!.StartsWith("AIF.") || n.StartsWith("Mpai."));      // it carries nothing of ours
        return into;
    }

    private static async Task<(StoreTests.RunningStore Store, StoreTests.Implementer Me)> Store()
    {
        var store = new StoreTests.RunningStore();
        return (store, await StoreTests.Implementer.Approved(store.Http));
    }

    [SkippableFact]
    public async Task APackagePublishedAsItIsRunsInAModuleThroughTheControllersOwnGlueAndAnswersAnAuditRequest()
    {
        var (store, me) = await Store();
        using var held = store;
        Assert.Equal("2", me.Id);                                                                   // the first Implementer after MPAI (1), as in the Store of the author
        var uri = Package(store.Packages);
        using (var zip = System.IO.Compression.ZipFile.OpenRead(new Uri(uri).LocalPath))
            Assert.Equal([Binary + ".deps.json", Binary + ".dll"], zip.Entries.Select(e => e.FullName).Order().ToArray());     // the dll and its deps.json: no plug-in, nothing of ours added
        var published = await StoreTests.Submit(store.Http, L3(me.Id, uri), me.Key);
        Assert.True(published.Status == HttpStatusCode.Created, $"{published.Status} {published.Body}\n{store.Log}");

        var said = new List<string>();
        using var source = new HttpAimSource(store.Url);
        var folder = Path.Combine(store.Root, "thalia-controller", "AIMs");
        using var api = Controller(folder, source, said);

        // THE CONTROLLER OBTAINS THE AIM when the Module starts - the L3 from the Store, the package from where the L3 says - and the Store says the package is the one it approved.
        var started = api.StartFlow(Aim, out var id);
        Assert.True(started == AifError.OK, $"{started}: {api.PrepareError} {api.StartRefusal} :: " + string.Join(" / ", said));
        Assert.True(api.Repository!.Holds(Aim), "the Controller holds what it obtained: " + string.Join(" / ", said));
        Assert.Contains(said, l => l.Contains("from the package " + Binary) && l.Contains("the Controller's own glue"));       // built from the package, by the Controller's glue
        var measured = ImplementationFingerprints.OfDirectory(Path.Combine(folder, "packages", "AIH-ADT-V1.1-I01"));
        Assert.Equal(VerifyResult.Same, source.Verify(Aim, Binary, measured));                      // what it unpacked and ran is what the Store approved

        // THE APP of the shelf, its Workflow run by the User Agent's interpreter: it offers the Audit Request and asks the Audit Response.
        var shown = new List<string>();
        var devices = new Mpai.Ua.DeviceRegistry().RegisterPresent("screen", data => { lock (shown) foreach (var v in data.Values) shown.Add(v); return Task.CompletedTask; });
        var workflow = new Mpai.Wdl.WorkflowReader().Read(File.ReadAllText(Path.Combine(Repository.Root, "Apps", "AIH", "ADT", "ADT.orch")));
        using var done = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var logged = new List<string>();
        var run = new Mpai.Ua.WorkflowInterpreter(api.Async(), devices, line => { lock (logged) logged.Add(line); }).RunAsync(workflow, done.Token);
        var answer = "";
        for (var i = 0; i < 100 && answer.Length == 0; i++) { await Task.Delay(200); lock (shown) answer = shown.FirstOrDefault(s => s.Contains("\"Response\"")) ?? ""; }
        done.Cancel(); try { await run; } catch (OperationCanceledException) { }
        Assert.True(answer.Length > 0, "no Audit Response: " + string.Join(" / ", logged) + " :: " + string.Join(" / ", said) + " :: " + string.Join(" / ", shown));
        var response = JsonNode.Parse(answer)!;
        Assert.Equal("AIH-ADT-V1.1", (string)response["Header"]!);
        Assert.Equal("end-user-1", (string)response["UserID"]!);
        Assert.Empty(response["Audit"]!["Response"]!["AIHDataIDs"]!.AsArray());                     // no AIH Data was given to Auditing: nothing unlicensed
        api.StopFlow(id);
    }

    // THE AIM WRITTEN OUTSIDE OUR CODE: a package with only the Implementation and its entry Module(); no plug-in, no glue of the Controller for it.
    [SkippableFact]
    public async Task AnAimWrittenOutsideOurCodeWithOnlyTheStandardEntryIsStartedAndGivenItsPorts()
    {
        var (store, me) = await Store();
        using var held = store;
        var built = BuildForeign(Path.Combine(store.Root, "foreign"));
        var uri = StoreTests.Zip(store.Packages, "AIH-ADT-V1.1-I02", ("Foreign.Aim.dll", File.ReadAllBytes(Path.Combine(built, "Foreign.Aim.dll"))), ("Foreign.Aim.deps.json", File.ReadAllBytes(Path.Combine(built, "Foreign.Aim.deps.json"))));
        var foreign = me.Id + "AIH-ADT-V1.1-I02";
        var published = await StoreTests.Submit(store.Http, L3(me.Id, uri, "AIH-ADT-V1.1-I02", "Foreign.Aim"), me.Key);
        Assert.True(published.Status == HttpStatusCode.Created, $"{published.Status} {published.Body}\n{store.Log}");

        var said = new List<string>();
        using var source = new HttpAimSource(store.Url);
        using var api = Controller(Path.Combine(store.Root, "controller-foreign", "AIMs"), source, said);
        var started = api.StartFlow(foreign, out var id);
        Assert.True(started == AifError.OK, $"{started}: {api.PrepareError} {api.StartRefusal} :: " + string.Join(" / ", said));
        Assert.Contains(said, l => l.Contains("its entry Foreign.Aim.Module"));
        var r = api.Advance(id, [new ControllerApi.Datum("AIH-ADT-V1.1", 1, "{\"hello\":1}")]);
        Assert.True(r.Ok, r.Error.ToString());
        var echo = JsonNode.Parse(r.ByType("AIH-ADT-V1.1") ?? "{}")!;
        Assert.Equal(1, (int)echo["echo"]!["hello"]!);                                              // the AIM was given its Port through the table of functions, and gave its output
        // An error code of the AIM, with the reason it gave (set_reason): nothing arrives at its Input Port, it returns 7 and says why.
        var wrong = api.Advance(id, [new ControllerApi.Datum("AIH-AHD-V1.1", 1, "{}")]);
        var why = string.Join(" | ", api.Status(id).Aims.Select(a => $"{a.Status} {a.Reason} {string.Join("; ", a.Reports)}")) + " " + wrong.Error;
        Assert.Contains("nothing at the Input Port AIH-ADT-V1.1#1", why);
        Assert.Contains("error 7", why);
        api.StopFlow(id);
    }

    [SkippableFact]
    public async Task AnEntryThatTakesNoTableStartsAndSaysWhyItCannotBeGivenData()
    {
        var (store, me) = await Store();
        using var held = store;
        var built = BuildForeign(Path.Combine(store.Root, "foreign-notable"), "NoTable");
        var uri = StoreTests.Zip(store.Packages, "AIH-ADT-V1.1-I03", ("Foreign.Aim.dll", File.ReadAllBytes(Path.Combine(built, "Foreign.Aim.dll"))), ("Foreign.Aim.deps.json", File.ReadAllBytes(Path.Combine(built, "Foreign.Aim.deps.json"))));
        Assert.Equal(HttpStatusCode.Created, (await StoreTests.Submit(store.Http, L3(me.Id, uri, "AIH-ADT-V1.1-I03", "Foreign.Aim"), me.Key)).Status);

        var said = new List<string>();
        using var source = new HttpAimSource(store.Url);
        using var api = Controller(Path.Combine(store.Root, "controller-notable", "AIMs"), source, said);
        Assert.Equal(AifError.OK, api.StartFlow(me.Id + "AIH-ADT-V1.1-I03", out var id));            // started: the entry of the standard is there
        var r = api.Advance(id, [new ControllerApi.Datum("AIH-ADT-V1.1", 1, "{}")]);
        // but it has no way to be given data in this environment, and the Module says so: no output, and the reason with the AIM's status
        var status = string.Join(" | ", api.Status(id).Aims.Select(a => $"{a.Status} {a.Reason} {string.Join("; ", a.Reports)}"));
        Assert.True(!r.Ok || r.ByType("AIH-ADT-V1.1") is null, "an AIM without a table answered: " + r.ByType("AIH-ADT-V1.1"));
        Assert.Contains("takes no table of functions", status + " " + r.Error);
        api.StopFlow(id);
    }

    // A KIND IT CANNOT HOST is refused with the reason: another Operating System or Architecture than the machine's, and a file that is not a .NET assembly.
    [SkippableFact]
    public async Task AnImplementationOfAKindTheControllerCannotHostIsRefusedWithTheReason()
    {
        var (store, me) = await Store();
        using var held = store;
        var built = BuildForeign(Path.Combine(store.Root, "foreign-kinds"));
        var good = StoreTests.Zip(store.Packages, "AIH-ADT-V1.1-I04", ("Foreign.Aim.dll", File.ReadAllBytes(Path.Combine(built, "Foreign.Aim.dll"))), ("Foreign.Aim.deps.json", File.ReadAllBytes(Path.Combine(built, "Foreign.Aim.deps.json"))));
        var native = StoreTests.Zip(store.Packages, "AIH-ADT-V1.1-I05", ("Foreign.Aim.dll", [0x4D, 0x5A, 0, 0, 1, 2, 3, 4]));          // not a .NET assembly
        var linux = L3(me.Id, good, "AIH-ADT-V1.1-I04", "Foreign.Aim", "Linux", "ARM64");
        Assert.Equal(HttpStatusCode.Created, (await StoreTests.Submit(store.Http, linux, me.Key)).Status);
        Assert.Equal(HttpStatusCode.Created, (await StoreTests.Submit(store.Http, L3(me.Id, native, "AIH-ADT-V1.1-I05", "Foreign.Aim"), me.Key)).Status);

        var said = new List<string>();
        using var source = new HttpAimSource(store.Url);
        using var api = Controller(Path.Combine(store.Root, "controller-kinds", "AIMs"), source, said);

        Assert.Equal(AifError.Failed, api.StartFlow(me.Id + "AIH-ADT-V1.1-I04", out _));
        Assert.Contains("Linux ARM64", api.StartRefusal);                                           // what the L3 names, and that it cannot be run here
        Assert.Contains("cannot be run here", api.StartRefusal);

        Assert.Equal(AifError.Failed, api.StartFlow(me.Id + "AIH-ADT-V1.1-I05", out _));
        Assert.Contains("not a .NET assembly", api.StartRefusal);
    }

    // THE PACKAGE AS IMPLEMENTER 2 PUBLISHED IT, in the Store of its author on this machine (port 5020), only asked: the Controller obtains the L3 and the package into a folder of its own, the Store
    // says Same, the Controller's glue builds the AIM and the Module answers an Audit Request. Skipped where that Store is not running or does not have the AIM.
    [SkippableFact]
    public void TheAimPublishedByImplementer2RunsFromTheStoreOfItsAuthor()
    {
        using var source = new HttpAimSource("http://127.0.0.1:5020");
        Skip.If(source.Get(Aim) is null, "the Store on 5020 is not running, or does not have " + Aim);
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "N-45-live-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var said = new List<string>();
            using var api = Controller(Path.Combine(folder, "AIMs"), source, said);
            var started = api.StartFlow(Aim, out var id);
            Assert.True(started == AifError.OK, $"{started}: {api.PrepareError} {api.StartRefusal} :: " + string.Join(" / ", said));
            Assert.Contains(said, l => l.Contains("from the package " + Binary));
            var request = JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Root, "Apps", "AIH", "ADT", "ADT.orch")).Split("= ", 2)[1].Split('\n')[0])!;       // the Audit Request of the App
            var r = api.Advance(id, [new ControllerApi.Datum("AIH-ADT-V1.1", 1, request.ToJsonString())]);
            Assert.True(r.Ok, r.Error.ToString());
            var response = JsonNode.Parse(r.ByType("AIH-ADT-V1.1") ?? "{}")!;
            Assert.Equal("audit-1", (string)response["AuditID"]!);
            Assert.Empty(response["Audit"]!["Response"]!["AIHDataIDs"]!.AsArray());
            Assert.Equal(VerifyResult.Same, source.Verify(Aim, Binary, ImplementationFingerprints.OfDirectory(Path.Combine(folder, "AIMs", "packages", "AIH-ADT-V1.1-I01"))));
            api.StopFlow(id);
        }
        finally { try { Directory.Delete(folder, true); } catch { } }
    }
    [SkippableFact]
    public async Task APackageThatIsNotWhatTheStoreApprovedIsNotRun()
    {
        var (store, me) = await Store();
        using var held = store;
        var uri = Package(store.Packages);
        Assert.Equal(HttpStatusCode.Created, (await StoreTests.Submit(store.Http, L3(me.Id, uri), me.Key)).Status);

        // The package changes where the Implementer keeps it, after the Store approved it (a byte added to the binary): the Controller does not obtain it, and the Module does not start.
        List<(string Name, byte[] Bytes)> files;
        using (var zip = System.IO.Compression.ZipFile.OpenRead(new Uri(uri).LocalPath))
            files = zip.Entries.Select(e => { using var s = e.Open(); using var m = new MemoryStream(); s.CopyTo(m); return (e.FullName, m.ToArray()); }).ToList();
        var at = files.FindIndex(f => f.Name == Binary + ".dll");
        files[at] = (files[at].Name, files[at].Bytes.Append((byte)0).ToArray());
        StoreTests.Zip(store.Packages, "AIH-ADT-V1.1-I01", files.Select(f => (f.Name, f.Bytes)).ToArray());

        var said = new List<string>();
        using var source = new HttpAimSource(store.Url);
        using var api = Controller(Path.Combine(store.Root, "thalia-controller-2", "AIMs"), source, said);
        Assert.Equal(AifError.NotTrusted, api.StartFlow(Aim, out _));
        Assert.Contains("NOT_TRUSTED", api.PrepareError);
        Assert.False(api.Repository!.Holds(Aim));
    }
}
