using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mpai.Networked;

// <App>Networked.exe - starts the MPAI-MAS server, waits for it, then the client.
//
// ONE PROJECT, PUBLISHED TWICE. It works out which application it belongs to
// from its OWN file name: TSTNetworked.exe looks for bin\TSTServer.exe and
// bin\TSTClient.exe, AMQNetworked.exe for bin\AMQServer.exe and
// bin\AMQClient.exe. Nothing to keep in step between the two applications, and
// a third would need no new code.
//
// THE SERVER KEEPS ITS OWN WINDOW. That is deliberate for a demonstration: the
// server loading its models is the slowest part of the whole system, and a
// visible console is the difference between "it is working" and "nothing is
// happening". It also shows the AIF trace as the AIMs run.
//
// It WAITS FOR THE SERVER RATHER THAN COUNTING TO TEN. This polls until the
// server answers.
//
// IT PASSES THE SERVER ITS PATHS. The server used to be started with no
// arguments, so it fell back to its built-in D:\AI defaults and never read any
// configuration. That worked only because the defaults happened to be right on
// one machine. The paths and the URL now come from the client's config, so one
// file governs both processes and a different root moves both.
internal static class Program
{
    private const string DefaultServerUrl = "http://localhost:5005/";
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(3);

    private static async Task<int> Main()
    {
        // "TSTNetworked" -> "TST", "AMQNetworked" -> "AMQ".
        var myName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "Networked");
        var app    = myName.EndsWith("Networked", StringComparison.OrdinalIgnoreCase)
            ? myName[..^"Networked".Length]
            : myName;

        Console.Title = $"MPAI {app} - networked";

        var here   = AppContext.BaseDirectory;
        var server = Path.Combine(here, "bin", $"{app}Server.exe");
        var client = Path.Combine(here, "bin", $"{app}Client.exe");

        if (!File.Exists(server) || !File.Exists(client))
        {
            Console.WriteLine("Cannot find the server and the client.");
            Console.WriteLine($"  expected: {server}");
            Console.WriteLine($"            {client}");
            Console.WriteLine();
            Console.WriteLine($"Run the Build-{app} script and choose the networked option.");
            Console.WriteLine("Press any key to close.");
            Console.ReadKey(true);
            return 1;
        }

        // The client's config, because the two processes share one root and the
        // server has no config of its own. If the server ever needs paths that
        // differ from the client's, it wants a config file of its own rather
        // than a second copy of these values here.
        var configPath = Path.Combine(here, "bin", $"{app}Client-config.json");
        var config     = LauncherConfig.Load(configPath);

        var serverUrl = string.IsNullOrWhiteSpace(config.MasServerUrl)
            ? DefaultServerUrl
            : config.MasServerUrl;

        Console.WriteLine($"Configuration: {(config.FromFile ? configPath : "built-in defaults (config not read)")}");
        Console.WriteLine($"  AMDs:     {config.AmdRepository}");
        Console.WriteLine($"  Settings: {config.SettingsFile}");
        Console.WriteLine($"  Output:   {config.OutputFolder}");
        Console.WriteLine($"  Server:   {serverUrl}");
        Console.WriteLine();

        // Sci.Host [amdRepo] [settingsFile] [outputFolder] [listenUrl]
        var arguments = string.Join(" ", new[]
        {
            config.AmdRepository,
            config.SettingsFile,
            config.OutputFolder,
            serverUrl
        }.Select(Quote));

        Console.WriteLine("Starting the MPAI-MAS server...");

        using var serverProcess = Process.Start(new ProcessStartInfo(server)
        {
            Arguments        = arguments,
            UseShellExecute  = true,          // its own window, for the demonstration
            WorkingDirectory = Path.GetDirectoryName(server)!
        });

        if (serverProcess is null)
        {
            Console.WriteLine("The server did not start.");
            Console.ReadKey(true);
            return 1;
        }

        Console.WriteLine("Waiting for it to load its models. This is the slow part.");

        if (!await WaitForServer(serverProcess, serverUrl))
        {
            Console.WriteLine();
            Console.WriteLine("The server did not answer. Its window will say why.");
            Console.WriteLine("Press any key to close.");
            Console.ReadKey(true);
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("Server ready. Starting the client.");

        using var clientProcess = Process.Start(new ProcessStartInfo(client)
        {
            UseShellExecute  = true,
            WorkingDirectory = Path.GetDirectoryName(client)!
        });

        if (clientProcess is null)
        {
            Console.WriteLine("The client did not start.");
            StopServer(serverProcess);
            Console.ReadKey(true);
            return 1;
        }

        Console.WriteLine("Close the client window to stop the server and finish.");

        await clientProcess.WaitForExitAsync();

        // The server holds several gigabytes of models. Leaving it running after
        // the client has gone is the kind of thing nobody notices until the
        // machine is short of memory.
        StopServer(serverProcess);

        Console.WriteLine("Server stopped.");
        return 0;
    }

    private static string Quote(string value) =>
        "\"" + value.Trim('"') + "\"";

    // Poll rather than sleep. A cold machine loading full-precision models takes
    // far longer than ten seconds; a warm one is ready almost at once.
    private static async Task<bool> WaitForServer(Process serverProcess, string serverUrl)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        var deadline = DateTime.UtcNow + Patience;

        while (DateTime.UtcNow < deadline)
        {
            if (serverProcess.HasExited)
            {
                Console.WriteLine();
                Console.WriteLine("The server stopped before it was ready.");
                return false;
            }

            try
            {
                using var response = await http.GetAsync(serverUrl);
                return true;              // anything at all means it is listening
            }
            catch
            {
                Console.Write(".");
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        return false;
    }

    private static void StopServer(Process serverProcess)
    {
        try
        {
            if (!serverProcess.HasExited)
                serverProcess.Kill(entireProcessTree: true);
        }
        catch
        {
            // It may have gone on its own; nothing to do either way.
        }
    }

    // The three paths and the URL the server needs, read from the same file the
    // client reads. %MpaiRoot% is expanded exactly as UaConfig expands it, so
    // the two agree on what a config file means.
    private sealed class LauncherConfig
    {
        public string MpaiRoot      { get; init; } = @"D:\AI";
        public string AmdRepository { get; init; } = @"D:\AI\AIMs\AMDs";
        public string SettingsFile  { get; init; } = @"D:\AI\AIMs\aim-settings.json";
        public string OutputFolder  { get; init; } = @"D:\AI\MPAIApps\AMQ\Output";
        public string MasServerUrl  { get; init; } = "";
        public bool   FromFile      { get; init; }

        private sealed class Raw
        {
            public string? MpaiRoot      { get; set; }
            public string? AmdRepository { get; set; }
            public string? SettingsFile  { get; set; }
            public string? OutputFolder  { get; set; }
            public string? MasServerUrl  { get; set; }
        }

        public static LauncherConfig Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return new LauncherConfig();

                var raw = JsonSerializer.Deserialize<Raw>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (raw is null)
                    return new LauncherConfig();

                var root = string.IsNullOrWhiteSpace(raw.MpaiRoot) ? @"D:\AI" : raw.MpaiRoot;

                string Expand(string? value, string fallback) =>
                    string.IsNullOrWhiteSpace(value)
                        ? fallback
                        : value.Replace("%MpaiRoot%", root);

                return new LauncherConfig
                {
                    MpaiRoot      = root,
                    AmdRepository = Expand(raw.AmdRepository, Path.Combine(root, "AIMs", "AMDs")),
                    SettingsFile  = Expand(raw.SettingsFile,  Path.Combine(root, "AIMs", "aim-settings.json")),
                    OutputFolder  = Expand(raw.OutputFolder,  Path.Combine(root, "MPAIApps", "AMQ", "Output")),
                    MasServerUrl  = raw.MasServerUrl ?? "",
                    FromFile      = true
                };
            }
            catch
            {
                // A malformed config should not stop a demonstration; the window
                // says which values were used.
                return new LauncherConfig();
            }
        }
    }
}