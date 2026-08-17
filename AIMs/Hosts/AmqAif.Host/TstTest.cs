using System;
using System.Collections.Generic;
using System.IO;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;

namespace AmqAif.Host;

// MMC-TST-V2.5 driven entirely through the User Agent.
//
// This is the TST execution model: the UA kicks off and leaves. It writes the
// boundary ports once, the Controller runs SOA -> ASR -> TTT -> TTS -> SOD per
// the Topology, and control returns with the outputs. No choreography class, no
// AIM named by the UA, no suspension in the ordinary case.
//
// Acquisition and delivery are INSIDE the composite, so the UA touches no audio
// device. A Speech Object WITH data on Input Speech is passed straight through
// by MMC-SOA; an EMPTY one means acquire from the microphone; omitting the port
// leaves the whole speech branch idle.
//
// Three passes, each testing something the AMQ path never exercised:
//
//   1. Speech only  - ASR runs, TTT takes Recognised Text (OSD-TXO PortNumber 2)
//   2. Text only    - ASR is SKIPPED (InputSpeech IsOptional, nothing supplied)
//                     and TTT takes Input Text (PortNumber 1)
//   3. Both         - the Text/Speech Selector decides which one is translated
//
// Run with:  dotnet run -- --tsttest
internal static class TstTest
{
    private const string OutputLanguage = "it";

    public static void Run()
    {
        AimLog.ToConsole();

        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            Console.WriteLine("Could not locate the repository root (looking for AIMs\\AMDs).");
            return;
        }

        var amdRepository = Path.Combine(repoRoot, "AIMs", "AMDs");
        var settingsFile  = Path.Combine(repoRoot, "AIMs", "aim-settings.json");

        var store = new AmdStore(amdRepository);
        store.Scan();
        var settings = AimSettings.Load(settingsFile);

        var speechFile = SpeechFile(settings);

        Console.WriteLine();
        Console.WriteLine("MMC-TST-V2.5 via User Agent - kick off, run, return");
        Console.WriteLine($"  AMDs:     {amdRepository}");
        Console.WriteLine($"  settings: {settingsFile}");
        Console.WriteLine($"  speech:   {speechFile ?? "(none found - pass 1 and 3 will be skipped)"}");

        // Pass 1: speech only.
        if (speechFile is not null)
        {
            var speech = BasicSpeechObject.FromData(File.ReadAllBytes(speechFile));
            RunOnce(
                store, settings,
                "1. speech supplied (expect: SOA passes it through, ASR runs, TTT uses Recognised Text)",
                new Dictionary<string, string>
                {
                    ["InputSpeech"]          = MpaiJson.ToJson(speech),
                    ["LanguageSelector"] = MpaiJson.ToJson(
                                                   BasicSelectorObject.Languages(null, OutputLanguage))
                });
        }

        // Pass 2: text only. ASR has nothing and must be skipped, not waited for.
        RunOnce(
            store, settings,
            "2. text only (expect: SOA and ASR SKIPPED, TTT uses Input Text)",
            new Dictionary<string, string>
            {
                ["InputText"]            = MpaiJson.ToJson(
                                               BasicTextObject.FromText("The zebra is drinking at the river.")),
                ["LanguageSelector"] = MpaiJson.ToJson(
                                               BasicSelectorObject.Languages("en", OutputLanguage))
            });

        // Pass 3: both, with the selector naming Input Text.
        if (speechFile is not null)
        {
            var speech = BasicSpeechObject.FromData(File.ReadAllBytes(speechFile));
            RunOnce(
                store, settings,
                "3. both, Media Selector = InputText (expect: the typed text is translated)",
                new Dictionary<string, string>
                {
                    ["InputSpeech"]          = MpaiJson.ToJson(speech),
                    ["InputText"]            = MpaiJson.ToJson(
                                                   BasicTextObject.FromText("A written question wins.")),
                    ["LanguageSelector"] = MpaiJson.ToJson(
                                                   BasicSelectorObject.Languages("en", OutputLanguage)),
                    ["MediaSelector"]   = MpaiJson.ToJson(
                                                   BasicSelectorObject.Source(TextSource.InputText))
                });
        }

        Console.WriteLine();
        Console.WriteLine("TST passes complete.");
    }

    private static void RunOnce(
        AmdStore store,
        AimSettings settings,
        string label,
        Dictionary<string, string> boundaryPorts)
    {
        Console.WriteLine();
        Console.WriteLine("--------------------------------------------------------");
        Console.WriteLine($"[TST] {label}");
        Console.WriteLine($"[UA]  boundary ports written: {string.Join(", ", boundaryPorts.Keys)}");

        var ua       = new UserAgent(store);
        var provider = new AmqAifProvider(store, headless: true);

        ua.MPAI_AIFU_Controller_Initialize();

        var err = ua.MPAI_AIFU_AIW_Start("MMC-TST-V2.5", provider, settings, out var aiwId);
        if (err != AifError.OK)
        {
            Console.WriteLine($"[UA]  AIW_Start failed: {err}");
            return;
        }

        var (runErr, outcome) =
            ua.RunAsync(aiwId, boundaryPorts).GetAwaiter().GetResult();

        if (runErr != AifError.OK || outcome is null)
        {
            Console.WriteLine($"[UA]  Run failed: {runErr}");
            ua.MPAI_AIFU_AIW_Stop(aiwId);
            return;
        }

        if (outcome.Suspended)
        {
            // In this workflow a suspension is a fault, not a step: nobody is
            // going to supply anything more.
            Console.WriteLine($"[TST] UNEXPECTED SUSPENSION on '{outcome.WaitingPort}'.");
            ua.MPAI_AIFU_AIW_Stop(aiwId);
            return;
        }

        var completed = outcome.Completed;
        if (completed is null)
        {
            Console.WriteLine("[TST] completed with no message.");
            ua.MPAI_AIFU_AIW_Stop(aiwId);
            return;
        }

        if (completed.IsError)
        {
            Console.WriteLine($"[TST] ERROR from {completed.FailedAim}: {completed.Payload}");
            ua.MPAI_AIFU_AIW_Stop(aiwId);
            return;
        }

        Console.WriteLine($"[TST] output ports: {string.Join(", ", completed.Ports.Keys)}");

        if (completed.Ports.TryGetValue("OutputText", out var textJson))
        {
            var text = MpaiJson.FromJson<BasicTextObject>(textJson);
            Console.WriteLine($"[TST] OutputText:      {text.GetText()}");
        }
        else
        {
            Console.WriteLine("[TST] OutputText:      (absent)");
        }

        if (completed.Ports.TryGetValue("OutputSpeech", out var speechJson))
        {
            var speech = MpaiJson.FromJson<BasicSpeechObject>(speechJson);
            Console.WriteLine($"[TST] OutputSpeech:    {speech.Data.Length:N0} bytes");
        }
        else
        {
            Console.WriteLine("[TST] OutputSpeech:    (absent)");
        }

        ua.MPAI_AIFU_AIW_Stop(aiwId);
    }

    // The spoken input: whatever the settings already point ASR or SOA at.
    private static string? SpeechFile(AimSettings settings)
    {
        foreach (var aim in new[] { "MMC-SOA-V2.5", "CAE-AOA-V1.0" })
        {
            if (settings.For(aim).TryGetValue("QuestionAudio", out var path) &&
                File.Exists(path))
            {
                return path;
            }
        }
        return null;
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