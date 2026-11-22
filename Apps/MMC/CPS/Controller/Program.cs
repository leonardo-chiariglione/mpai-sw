using System;
using System.IO;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.Store;

using Mpai.Aif.Api;
using Mpai.Aif.PortData;
using Mpai.Core;
using Mpai.Mas.Server;
using Mpai.Mas.Service;

namespace Mpai.Mmc.Cps.Controller;

// CpsController - THE CONTROLLER of Conversation with Personal Status, a program of its own.
//
//   CpsController [--listen http://127.0.0.1:5059]
//
// The App is a User Agent: it reads its workflow, and the workflow asks this Controller, through the Controller API, to start its Module (the L3); this Controller starts the Module and its AIMs as
// the L3 says. The Controller API is carried over the routes of Mpai.Mas.Server, on the loopback. Nothing here is the person's: the App only supplies the devices.
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        string? Value(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var url = Value("--listen") ?? "http://127.0.0.1:5059";
        Console.WriteLine("=== MPAI-MMC Conversation with Personal Status: the Controller ===");
        if (!url.StartsWith("http://127.0.0.1", StringComparison.Ordinal) && !url.StartsWith("http://localhost", StringComparison.Ordinal))
        { Console.WriteLine("FATAL: this Controller listens on the loopback only (no token, no certificate is configured)."); return 1; }

        var amds = MpaiPaths.Amds; var settings = MpaiPaths.Settings;
        if (!Directory.Exists(amds)) { Console.WriteLine($"FATAL: the AMD directory does not exist: {amds}"); return 1; }
        PortDataSchema.Sink = (dataType, direction, complaint) => Console.WriteLine($"[SCHEMA] {dataType} {direction} {complaint}");
        var store = new AmdStore(amds);
        store.Scan();
        using var north = new ControllerApi(amds, settings, s => new CpsProvider(s));
        var runner = new ControllerApiRunner(north, store);
        var server = new MasServer(runner, PortDataCodecs.Default(), url);
        Console.WriteLine($"  Listening:   {url}");
        Console.WriteLine($"  AMDs:        {amds}");
        Console.WriteLine("The Controller is ready; the App starts its Module.");
        await server.RunAsync();
        return 0;
    }
}
