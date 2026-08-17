using System;
using System.Collections.Generic;
using System.IO;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;

namespace AmqAif.Host;

// MMC-TST-V2.5 spoken into a microphone and heard from a loudspeaker.
//
// The User Agent touches no audio device. Acquisition and delivery are SubAIMs
// of the composite - MMC-SOA at the front, MMC-SOD at the back - so all the UA
// does is write the boundary ports, run once, and read what comes back. The
// Controller does the rest, exactly as in --tsttest.
//
// The only difference from --tsttest is the provider: headless false, so
// MMC-SOA gets the microphone instead of a WAV file and MMC-SOD the loudspeaker
// instead of an output folder.
//
// An EMPTY Speech Object on Input Speech is what asks MMC-SOA to acquire: it
// passes through a Speech Object that carries data, and omitting the port
// entirely leaves the speech branch idle. So "empty" is the trigger, and it
// needs no extra port and no change to the figure.
//
// Recording length comes from MMC-SOA-V2.5's DurationSeconds. Press-to-stop
// would be better, but the AIF Basic API has no way for the User Agent to signal
// a running AIM - only Pause and Stop, which end the run. That gap is why the
// AMQ user interface reached into AimHost for IStartStopAcquisition, which is
// one of the zero-trust holes.
//
// Run with:  dotnet run -- --tstlive
internal static class TstLiveTest
{
    public static void Run()
    {
        AimLog.ToConsole();

        var repoRoot = FindRepoRoot();
        if (repoRoot is null) { Console.WriteLine("Could not find AIMs\\AMDs."); return; }

        var store = new AmdStore(Path.Combine(repoRoot, "AIMs", "AMDs"));
        store.Scan();

        var settingsPath = Path.Combine(repoRoot, "AIMs", "aim-settings.json");
        var settings     = AimSettings.Load(settingsPath);

        var seconds = settings.For("MMC-SOA-V2.5").TryGetValue("DurationSeconds", out var declared)
                      ? declared
                      : "5";

        var sourceLanguage = Ask("Speak which language", "en");
        var targetLanguage = Ask("Translate into",       "it");

        Console.WriteLine();
        Console.WriteLine($"MMC-TST-V2.5 live: {sourceLanguage} -> {targetLanguage}");
        Console.WriteLine($"  MMC-SOA records for {seconds}s from ENTER; MMC-SOD plays the result.");
        Console.WriteLine("  ENTER to speak, 'q' then ENTER to quit.");

        while (true)
        {
            Console.WriteLine();
            Console.Write("> ");
            if (string.Equals(Console.ReadLine()?.Trim(), "q", StringComparison.OrdinalIgnoreCase))
                break;

            Console.WriteLine($"  speak now ({seconds}s)...");

            var ua = new UserAgent(store);

            // headless: false - the real microphone and loudspeaker.
            var provider = new AmqAifProvider(store, headless: false);

            ua.MPAI_AIFU_Controller_Initialize();

            var started = ua.MPAI_AIFU_AIW_Start("MMC-TST-V2.5", provider, settings, out var aiwId);
            if (started != AifError.OK)
            {
                Console.WriteLine($"  AIW_Start failed: {started}");
                continue;
            }

            try
            {
                // Empty Speech Object = "acquire". The Language Selector carries
                // both codes; MMC-ASR takes the source language from the Speech
                // Qualifier, which MMC-SOA stamps on what it captures.
                var boundary = new Dictionary<string, string>
                {
                    ["InputSpeech"]      = MpaiJson.ToJson(BasicSpeechObject.FromData(Array.Empty<byte>())),
                    ["LanguageSelector"] = MpaiJson.ToJson(
                        BasicSelectorObject.Languages(sourceLanguage, targetLanguage))
                };

                var (error, outcome) = ua.RunAsync(aiwId, boundary).GetAwaiter().GetResult();

                if (error != AifError.OK || outcome is null)
                {
                    Console.WriteLine($"  run failed: {error}");
                    continue;
                }

                if (outcome.Suspended)
                {
                    Console.WriteLine($"  unexpected suspension on '{outcome.WaitingPort}'.");
                    continue;
                }

                var completed = outcome.Completed;
                if (completed is null)
                {
                    Console.WriteLine("  completed with no message.");
                    continue;
                }

                if (completed.IsError)
                {
                    Console.WriteLine($"  error from {completed.FailedAim}: {completed.Payload}");
                    continue;
                }

                if (completed.Ports.TryGetValue("OutputText", out var textJson))
                {
                    var text = MpaiJson.FromJson<BasicTextObject>(textJson);
                    Console.WriteLine($"  {targetLanguage}: {text.GetText()}");
                }
                else
                {
                    Console.WriteLine("  no OutputText produced.");
                }

                if (completed.Ports.TryGetValue("OutputSpeech", out var speechJson))
                {
                    var speech = MpaiJson.FromJson<BasicSpeechObject>(speechJson);
                    Console.WriteLine($"  played {speech.Data.Length:N0} bytes");
                }
            }
            finally
            {
                ua.MPAI_AIFU_AIW_Stop(aiwId);
            }
        }

        Console.WriteLine();
        Console.WriteLine("Done.");
    }

    private static string Ask(string prompt, string fallback)
    {
        Console.Write($"{prompt} [{fallback}]: ");
        var answer = Console.ReadLine()?.Trim();
        return string.IsNullOrWhiteSpace(answer) ? fallback : answer;
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