using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;

namespace Tst.Linux.Host;

// MMC-TST-V2.5 on Linux, without a window.
//
// The Avalonia application runs here too - its project targets plain net10.0 off
// Windows - so this exists for the same reason Amq.Linux.Host does: a headless
// box, a container, an ssh session, and a way to prove the pipeline works before
// blaming the user interface.
//
//   dotnet run                    speak, press ENTER to stop
//   dotnet run -- --text "..."    translate a sentence and exit
//   dotnet run -- --headless      read the configured WAV instead of the mic
internal static class Program
{
    private const string PromptAiw = "UAG-SPK-V1.0";
    private const string TstAiw    = "MMC-TST-V2.5";

    private static async Task<int> Main(string[] args)
    {
        var headless = Array.Exists(args, a => a == "--headless");
        var text     = Value(args, "--text");
        var source   = Value(args, "--from") ?? "en";
        var target   = Value(args, "--into") ?? "it";

        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            Console.Error.WriteLine("Could not find AIMs/AMDs above this executable.");
            return 1;
        }

        var store = new AmdStore(Path.Combine(repoRoot, "AIMs", "AMDs"));
        store.Scan();

        var settings = AimSettings.Load(Path.Combine(repoRoot, "AIMs", "aim-settings.json"));
        var provider = new TstLinuxProvider(store, headless);
        var ua       = new UserAgent(store);

        Console.WriteLine($"MMC-TST-V2.5 on Linux: {source} -> {target}");
        Console.WriteLine("  loading models...");

        ua.MPAI_AIFU_Controller_Initialize();

        if (ua.MPAI_AIFU_AIW_Start(TstAiw, provider, settings, out var aiwId) != AifError.OK)
        {
            Console.Error.WriteLine($"Could not start {TstAiw}.");
            return 1;
        }

        try
        {
            var boundary = new Dictionary<string, string>
            {
                ["LanguageSelector"] = MpaiJson.ToJson(
                    BasicSelectorObject.Languages(source, target))
            };

            if (text is not null)
            {
                boundary["InputText"] = MpaiJson.ToJson(BasicTextObject.FromText(text));
            }
            else
            {
                // An empty Speech Object asks MMC-SOA to acquire; its Qualifier
                // carries the source language for MMC-ASR.
                boundary["InputSpeech"] = MpaiJson.ToJson(
                    BasicSpeechObject.FromData(
                        Array.Empty<byte>(),
                        new SpeechQualifier
                        {
                            SpeechQualifierID = Guid.NewGuid().ToString(),
                            Attributes = new SpeechAttributes
                            {
                                Metadata = new SpeechMetadata
                                {
                                    Language = new Language
                                    {
                                        LanguageCode   = source,
                                        LanguageFormat = LanguageFormat.Iso639_1
                                    }
                                }
                            }
                        }));
            }

            var running = Task.Run(() => ua.RunAsync(aiwId, boundary));

            // Press-to-stop, through Pause and Resume, exactly as the window
            // does it - which is only possible on Linux now that arecord can be
            // interrupted rather than timed.
            if (text is null && !headless)
            {
                Console.WriteLine("  speak after the beep, then press ENTER.");
                while (!running.IsCompleted)
                {
                    if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Enter)
                    {
                        ua.MPAI_AIFU_AIW_Pause(aiwId);
                        ua.MPAI_AIFU_AIW_Resume(aiwId);
                        break;
                    }
                    Thread.Sleep(25);
                }
            }

            var (error, outcome) = await running;

            if (error != AifError.OK || outcome?.Completed is null)
            {
                Console.Error.WriteLine($"Run failed: {error}");
                return 1;
            }

            if (outcome.Completed.IsError)
            {
                Console.Error.WriteLine($"{outcome.Completed.FailedAim}: {outcome.Completed.Payload}");
                return 1;
            }

            if (outcome.Completed.Ports.TryGetValue("OutputText", out var textJson))
            {
                Console.WriteLine($"  {target}: {MpaiJson.FromJson<BasicTextObject>(textJson).GetText()}");
            }
            else
            {
                Console.WriteLine("  nothing came back.");
                return 1;
            }

            return 0;
        }
        finally
        {
            ua.MPAI_AIFU_AIW_Stop(aiwId);
        }
    }

    private static string? Value(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string? FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "AIMs", "AMDs")))
                return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }
}