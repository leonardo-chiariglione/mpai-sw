using System;
using System.Diagnostics;
using System.IO;

using AIF.Store;

using Mpai.Aims.Ttt;
using Mpai.Core;

namespace AmqAif.Host;

// Stage 2b: translate a few sentences directly through MMC-TTT, outside the AIF.
//
// Worth doing before --tsttest because a wrong decode loop and a wrong topology
// look alike from the outside. Here there is no Controller, no ASR and no TTS -
// only text in and text out - so anything wrong is the engine's.
//
// The tell-tales of the classic faults:
//   same token repeating       the past_key_values / use_cache_branch handoff
//   fluent but wrong language  the target language token id
//   plausible words, no sense  the piece-to-id mapping
//   empty output               </s> chosen immediately, usually a bad first step
//
// Run with:  dotnet run -- --translatetest
internal static class TranslateTest
{
    public static void Run()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null) { Console.WriteLine("Could not find AIMs\\AMDs."); return; }

        var settings = AimSettings.Load(Path.Combine(repoRoot, "AIMs", "aim-settings.json"))
                                  .For("MMC-TTT-V2.5");

        Console.WriteLine();
        Console.WriteLine("MMC-TTT-V2.5 direct translation");

        var started = Stopwatch.StartNew();
        var ttt     = TttFactory.Create(settings);
        Console.WriteLine($"  engine: {ttt.GetType().Name}  (loaded in {started.ElapsedMilliseconds:N0} ms)");

        var cases = new[]
        {
            ("The zebra is drinking at the river.",                        "en", "it"),
            ("What animal is shown in this picture, and what is it doing?", "en", "it"),
            ("Il carciofo e' un ortaggio.",                                "it", "en"),
            ("Good morning. Please take a seat.",                          "en", "fr")
        };

        foreach (var (text, from, to) in cases)
        {
            Console.WriteLine();
            Console.WriteLine($"  {from} -> {to}");
            Console.WriteLine($"    in:  {text}");

            var timer = Stopwatch.StartNew();
            try
            {
                var result = ttt.ProcessAsync(
                    BasicTextObject.FromText(text),
                    BasicSelectorObject.Languages(from, to)).GetAwaiter().GetResult();

                Console.WriteLine($"    out: {result.GetText()}");
                Console.WriteLine($"    language stamped on the Qualifier: " +
                                  $"{result.TextQualifier?.Attributes?.Language?.LanguageCode ?? "(none)"}");
                Console.WriteLine($"    {timer.ElapsedMilliseconds:N0} ms");
            }
            catch (Exception failure)
            {
                Console.WriteLine($"    FAILED: {failure.GetType().Name}: {failure.Message}");
            }
        }

        if (ttt is IDisposable disposable) disposable.Dispose();

        Console.WriteLine();
        Console.WriteLine("The Qualifier language matters as much as the text: MMC-TTS picks its");
        Console.WriteLine("voice from it, so a right translation with a missing stamp still speaks");
        Console.WriteLine("Italian in an English voice.");
        Console.WriteLine();
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