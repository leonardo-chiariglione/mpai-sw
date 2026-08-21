using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
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
// This replaces TSTNetworked.bat. The .bat worked, but a folder meant for a
// demonstration should show only what a person double-clicks, and a .bat sitting
// beside the executables it launches invites the question of which to run.
//
// THE SERVER KEEPS ITS OWN WINDOW. That is deliberate for a demonstration: the
// server loading its models is the slowest part of the whole system, and a
// visible console is the difference between "it is working" and "nothing is
// happening". It also shows the AIF trace as the AIMs run, which is most of what
// there is to see.
//
// It WAITS FOR THE SERVER RATHER THAN COUNTING TO TEN. The .bat slept ten
// seconds, which was too long on a warm machine and too short on a cold one with
// full-precision models to load. This polls until the server answers.
internal static class Program
{
    private const string ServerUrl = "http://localhost:5005/";
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

        Console.WriteLine("Starting the MPAI-MAS server...");

        using var serverProcess = Process.Start(new ProcessStartInfo(server)
        {
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

        if (!await WaitForServer(serverProcess))
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

    // Poll rather than sleep. A cold machine loading full-precision models takes
    // far longer than ten seconds; a warm one is ready almost at once.
    private static async Task<bool> WaitForServer(Process serverProcess)
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
                using var response = await http.GetAsync(ServerUrl);
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
}