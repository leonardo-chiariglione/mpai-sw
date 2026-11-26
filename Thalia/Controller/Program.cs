using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.Store;

using Mpai.Aif.Api;
using Mpai.Aif.PortData;
using Mpai.Core;
using Mpai.Mas.Server;
using Mpai.Mas.Service;
using Mpai.Providers;

namespace Mpai.Thalia.Controller;

// ThaliaController - THE LOCAL CONTROLLER OF THALIA, one instance for each running local App.
//
//   ThaliaController --listen http://127.0.0.1:<port> --app <App ID> [--apps <folder of the Apps>] [--store <MPAI Store URL> --aims <folder of this Controller's AIMs>]
//
// It offers the App it runs (its App Description: name, description, icon, Workflow) and starts the App's Module at once, so that the App's first
// request finds it ready. Then it answers the Controller-User Agent API like any Controller: the App's User Agent starts, drives and stops the Module.
// It prints "[MAS] Listening on <url>" when it is ready (Mpai.Mas.Server). Loopback only: no token and no certificate are configured.
//
// AIMs OF THE STORE (N-45). The AIMs of the first Apps are compiled in. A Module that is not among them - the App's Module is an AIM of an Implementer that published it in the
// MPAI Store - is obtained by this Controller from the Store (--store) into the folder of its own (--aims: the L3s it obtained, the packages and the records of what the Store
// approved), and each AIM is built from its package (PackageAimProvider): the Controller measures the package as it unpacked it and asks the Store whether that is the one it
// approved (Same), before anything of it is loaded; a package the Store does not approve is not run (NOT_TRUSTED). The L3s of the software (AIMs\AMDs) are not changed.
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string? Value(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var url  = Value("--listen") ?? "http://127.0.0.1:5200";
        var app  = Value("--app");
        var apps = Value("--apps") ?? Path.Combine(MpaiPaths.Root, "Apps");
        Console.WriteLine($"=== Thalia: the local Controller{(app is null ? "" : " of " + app)} ===");
        if (!url.StartsWith("http://127.0.0.1", StringComparison.Ordinal) && !url.StartsWith("http://localhost", StringComparison.Ordinal))
        { Console.WriteLine("FATAL: this Controller listens on the loopback only (no token, no certificate is configured)."); return 1; }

        var amds = MpaiPaths.Amds; var settings = MpaiPaths.Settings;
        var storeUrl = Value("--store"); var aimsFolder = Value("--aims");
        if (!Directory.Exists(amds)) { Console.WriteLine($"FATAL: the AMD directory does not exist: {amds}"); return 1; }
        PortDataSchema.Sink = (dataType, direction, complaint) => Console.WriteLine($"[SCHEMA] {dataType} {direction} {complaint}");
        var store = new AmdStore(amds);
        store.Scan();

        // THE APP IT RUNS, as a MPAI as a Service instance offers its Apps: the same App Description, so that Thalia lists and runs both alike.
        var catalogue = AppCatalogue.Scan(apps, app is null ? Array.Empty<string>() : new[] { app });
        var entry = app is null ? null : catalogue.Apps.FirstOrDefault(a => a.Id == app);
        if (app is not null && entry is null) { Console.WriteLine($"FATAL: no App {app} under {apps} (a folder {app} with app.json and its Workflow)."); return 1; }

        // A Module the software does not carry is the Store's: this Controller keeps what it obtains in a folder of its own.
        AimRepository? repository = null; HttpAimSource? source = null;
        if (storeUrl is not null && aimsFolder is not null && entry?.Module is { Length: > 0 } wanted && store.FindByAimName(wanted) is null)
        {
            Directory.CreateDirectory(aimsFolder);
            repository = new AimRepository(aimsFolder);
            source = new HttpAimSource(storeUrl);
            amds = aimsFolder;
            Console.WriteLine($"  Store:       {storeUrl} (the Module {wanted} is not in the software: obtained from it into {aimsFolder})");
        }

        using var north = new ControllerApi(amds, settings,
            s =>
            {
                var compiled = new IAimProvider[] { new AmcProvider(s), new McaProvider(s), new MtsProvider(s), new NprProvider(s), new AmqProvider(s), new AcrProvider(s), new MacProvider(s),
                                                  new Mpai.Cae.Usc.UscProvider(s), new Mpai.Cui.CuiProvider(), new MqaProvider(s) };   // CAE-USC and CUI-CPP, as the MPAI as a Service instance (N-49); MMC-MQA (N-50, after the others)
                // The package provider is asked first: it builds what the Store approved from its package, and nothing else.
                return repository is null ? new CompositeProvider(compiled)
                    : new CompositeProvider(compiled.Prepend(new PackageAimProvider(s, Path.Combine(aimsFolder!, "packages"), Console.WriteLine, source, PackageAdapters.All)).ToArray());
            });
        if (repository is not null) { north.Repository = repository; north.Source = source; }
        // The runner reads the Ports of the Module from the L3s: those of the folder this Controller keeps, where the Store's AIM was obtained (read again once the Module has started).
        var runnerStore = repository is null ? store : new AmdStore(amds);
        runnerStore.Scan();
        var runner = new ControllerApiRunner(north, runnerStore);

        if (entry?.Module is { Length: > 0 } module)
        {
            Console.WriteLine($"  Module:      {module}");
            var failure = runner.Start(module);
            if (failure is not null) { Console.WriteLine($"FATAL: the Module {module} did not start: {failure}"); return 2; }
            runnerStore.Scan();
        }

        var server = new MasServer(runner, PortDataCodecs.Default(), url) { Catalogue = catalogue, ExclusiveControl = true };   // one App, one User: Pause and Resume act on the Module
        Console.WriteLine($"  Listening:   {url}");
        Console.WriteLine($"  AMDs:        {amds}");
        await server.RunAsync();
        return 0;
    }
}
