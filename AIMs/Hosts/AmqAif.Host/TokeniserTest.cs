using System;
using System.IO;
using System.Linq;

using AIF.Store;

using Mmc.Ttt.Onnx;

namespace AmqAif.Host;

// Stage 1 of the real MMC-TTT engine: prove the tokeniser before downloading
// 1.9 GB of model weights.
//
// What is being checked, in order of how quietly it fails if wrong:
//
//   1. Every piece of the source text maps to an id. An unknown piece becomes
//      <unk> and is silently mistranslated; a count of zero is the only
//      acceptable answer for ordinary Latin text.
//   2. Encode then Decode returns the original sentence. If it does not, the
//      piece-to-id mapping or the U+2581 word-boundary handling is wrong.
//   3. The language tokens "__en__" and "__it__" resolve to ids. These force the
//      decoder's first token, and a wrong one produces fluent output in the
//      wrong language - the failure hardest to spot in a demo.
//   4. </s> is present: M2M-100 starts the decoder with it, not with <s>.
//
// Run with:  dotnet run -- --tokentest
internal static class TokeniserTest
{
    public static void Run()
    {
        var repoRoot = FindRepoRoot();
        if (repoRoot is null) { Console.WriteLine("Could not find AIMs\\AMDs."); return; }

        var settings = AimSettings.Load(Path.Combine(repoRoot, "AIMs", "aim-settings.json"))
                                  .For("MMC-TTT-V2.5");

        if (!settings.TryGetValue("TttSpmModel", out var spmPath) ||
            !settings.TryGetValue("TttVocab", out var vocabPath))
        {
            Console.WriteLine("MMC-TTT-V2.5 needs TttSpmModel and TttVocab in aim-settings.json.");
            return;
        }

        settings.TryGetValue("TttSpecialTokens", out var specialTokensPath);

        foreach (var path in new[] { spmPath, vocabPath })
        {
            if (!File.Exists(path)) { Console.WriteLine($"Missing: {path}"); return; }
        }

        Console.WriteLine();
        Console.WriteLine("M2M-100 tokeniser probe");
        Console.WriteLine($"  spm:   {spmPath}");
        Console.WriteLine($"  vocab: {vocabPath}");

        var tokeniser = M2M100Tokeniser.Load(spmPath, vocabPath, specialTokensPath);

        Console.WriteLine();
        Console.WriteLine($"  vocabulary entries: {tokeniser.VocabularyEntries:N0}");
        Console.WriteLine($"  languages:          {tokeniser.LanguageCount} " +
                          (tokeniser.LanguagesFromFile
                              ? "(order read from special_tokens_map.json)"
                              : "(order from the EMBEDDED fallback list)"));
        if (tokeniser.LanguageCount != 100)
        {
            Console.WriteLine("  ! expected 100 languages. A wrong count means a wrong order,");
            Console.WriteLine("    and a wrong order means fluent output in the wrong language.");
        }
        Console.WriteLine($"  <s>={tokeniser.BosId}  <pad>={tokeniser.PadId}  " +
                          $"</s>={tokeniser.EosId}  <unk>={tokeniser.UnknownId}");

        // ---- 3. language tokens ------------------------------------------
        Console.WriteLine();
        Console.WriteLine("  language tokens:");
        foreach (var language in new[] { "en", "it", "fr", "de", "zz" })
        {
            var id = tokeniser.LanguageTokenId(language);
            var shown = id.HasValue ? id.Value.ToString("N0") : "ABSENT";
            var note  = language == "zz" ? "   (expected ABSENT - a control)" : "";
            Console.WriteLine($"    __{language}__ -> {shown}{note}");
        }

        // ---- 1 and 2. round trip -----------------------------------------
        var samples = new[]
        {
            "The zebra is drinking at the river.",
            "What animal is shown in this picture, and what is it doing?",
            "Il carciofo e' un ortaggio."
        };

        foreach (var sample in samples)
        {
            Console.WriteLine();
            Console.WriteLine($"  text:     {sample}");

            var pieces  = tokeniser.Pieces(sample);
            var unknown = tokeniser.UnknownCount(sample);
            var ids     = tokeniser.Encode(sample, "en");
            var back    = tokeniser.Decode(ids);

            Console.WriteLine($"  pieces:   {string.Join(" ", pieces.Take(14))}" +
                              (pieces.Count > 14 ? " ..." : ""));
            Console.WriteLine($"  ids:      {string.Join(" ", ids.Take(14))}" +
                              (ids.Length > 14 ? $" ...   ({ids.Length} total)" : $"   ({ids.Length} total)"));
            Console.WriteLine($"  leading:  {ids[0]} should be __en__ = " +
                              $"{tokeniser.LanguageTokenId("en")}, trailing {ids[^1]} should be </s> = {tokeniser.EosId}");
            Console.WriteLine($"  unknown:  {unknown}" +
                              (unknown == 0 ? "" : "   <-- these would be mistranslated"));
            Console.WriteLine($"  decoded:  {back}");

            var exact = string.Equals(back, sample, StringComparison.Ordinal);
            Console.WriteLine($"  round trip: {(exact ? "EXACT" : "DIFFERS")}");
        }

        Console.WriteLine();
        Console.WriteLine("If the language tokens resolve, unknown counts are 0 and the round");
        Console.WriteLine("trips are exact, the tokeniser is sound and the decode loop is next.");
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