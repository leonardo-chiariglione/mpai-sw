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
//   ThaliaController --listen http://127.0.0.1:<port> --app <App ID> [--apps <folder of the Apps>]
//
// It offers the App it runs (its App Description: name, description, icon, Workflow) and starts the App's Module at once, so that the App's first
// request finds it ready. Then it answers the Controller-User Agent API like any Controller: the App's User Agent starts, drives and stops the Module.
// It prints "[MAS] Listening on <url>" when it is ready (Mpai.Mas.Server). Loopback only: no token and no certificate are configured.
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
        if (!Directory.Exists(amds)) { Console.WriteLine($"FATAL: the AMD directory does not exist: {amds}"); return 1; }
        PortDataSchema.Sink = (dataType, direction, complaint) => Console.WriteLine($"[SCHEMA] {dataType} {direction} {complaint}");
        var store = new AmdStore(amds);
        store.Scan();

        // THE APP IT RUNS, as a MPAI as a Service instance offers its Apps: the same App Description, so that Thalia lists and runs both alike.
        var catalogue = AppCatalogue.Scan(apps, app is null ? Array.Empty<string>() : new[] { app });
        var entry = app is null ? null : catalogue.Apps.FirstOrDefault(a => a.Id == app);
        if (app is not null && entry is null) { Console.WriteLine($"FATAL: no App {app} under {apps} (a folder {app} with app.json and its Workflow)."); return 1; }

        using var north = new ControllerApi(amds, settings,
            s => new CompositeProvider(new AmcProvider(s), new McaProvider(s), new MtsProvider(s), new NprProvider(s), new AmqProvider(s),
                new AcrProvider(s), new MacProvider(s)));
        var runner = new ControllerApiRunner(north, store);

        if (entry?.Module is { Length: > 0 } module)
        {
            Console.WriteLine($"  Module:      {module}");
            var failure = runner.Start(module);
            if (failure is not null) { Console.WriteLine($"FATAL: the Module {module} did not start: {failure}"); return 2; }
        }

        var server = new MasServer(runner, PortDataCodecs.Default(), url) { Catalogue = catalogue, ExclusiveControl = true };   // one App, one User: Pause and Resume act on the Module
        Console.WriteLine($"  Listening:   {url}");
        Console.WriteLine($"  AMDs:        {amds}");
        await server.RunAsync();
        return 0;
    }
}
