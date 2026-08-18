using System;
using System.IO;
using System.Text.Json;

namespace TstUi;

// Where the AIF is.
//
// An empty MasServerUrl means LOCAL: the application starts its own Controller
// and runs every AIM in this process, which is what it has done so far. A URL
// means REMOTE: the AIWs run on an MPAI-MAS server and this becomes a Remote
// Client Application, holding only the microphone, the loudspeaker and the
// window.
//
// Same switch, same file name pattern and same meaning as UaUi's ua-config.json,
// so the two applications are configured the same way.
public sealed class TstConfig
{
    public string MasServerUrl { get; set; } = string.Empty;

    // Offered in the language boxes when running remotely. Locally the list is
    // built from the configured voices, but a Remote Client Application cannot
    // see the server's settings - and should not: what languages a server can
    // speak is the server's business, and asking would be a new API call.
    public string[] Languages { get; set; } = { "en", "it", "fr", "de", "zh", "ja", "es", "pt" };

    // Set from Program.Main, so a launcher can say --mas http://host:5005/
    // without a file at all.
    public static string[] CommandLine { get; set; } = Array.Empty<string>();

    public static TstConfig Load()
    {
        // 1. the command line wins, because it is the most specific thing anyone
        //    can say and needs no file to exist.
        var named = Array.IndexOf(CommandLine, "--mas");
        if (named >= 0 && named + 1 < CommandLine.Length)
        {
            return new TstConfig { MasServerUrl = CommandLine[named + 1] };
        }

        if (Array.IndexOf(CommandLine, "--local") >= 0)
        {
            return new TstConfig();
        }

        // 2. then a config named after THIS executable, then the shared one.
        //    Two executables published side by side - a standalone copy and a
        //    client - would otherwise read the same file and one of them would
        //    be wrong.
        var executable = Path.GetFileNameWithoutExtension(
            System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "TstUi");

        var names = new[] { executable + "-config.json", "tst-config.json" };

        foreach (var directory in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        foreach (var name in names)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;

            try
            {
                var config = JsonSerializer.Deserialize<TstConfig>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (config is not null) return config;
            }
            catch (Exception failure)
            {
                Console.WriteLine($"[UA] {path} could not be read: {failure.Message}");
            }
        }

        return new TstConfig();   // local
    }
}