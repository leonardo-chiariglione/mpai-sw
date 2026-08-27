using System;
using System.IO;

using Mpai.Mmc.Sir;

namespace Mpai.Mmc.Sir.EmbedTest;

// Stage 1 verification: does the ECAPA embedder discriminate speakers?
//   spk1221_a, spk1221_b  = same speaker (1221)
//   spk1089_x             = different speaker (1089)
// Expect: cosine(1221_a, 1221_b) HIGH; cosine(1221_a, 1089_x) LOW.
public static class Program
{
    public static int Main(string[] args)
    {
        string models = args.Length >= 1 ? args[0] : @"D:\AI\Models";
        string modelPath = Path.Combine(models, "ecapa-tdnn.onnx");
        string a = Path.Combine(models, "spk1221_a.wav");
        string b = Path.Combine(models, "spk1221_b.wav");
        string x = Path.Combine(models, "spk1089_x.wav");

        foreach (var p in new[] { modelPath, a, b, x })
            if (!File.Exists(p)) { Console.WriteLine($"Missing: {p}"); return 1; }

        using var embedder = new SpeakerEmbedder(modelPath);

        Console.WriteLine("Embedding clips...");
        var ea = embedder.Embed(WavReader.ReadMono16k(a));
        var eb = embedder.Embed(WavReader.ReadMono16k(b));
        var ex = embedder.Embed(WavReader.ReadMono16k(x));
        Console.WriteLine($"  embedding dim: {ea.Length}");
        Console.WriteLine();

        double same = SpeakerEmbedder.Cosine(ea, eb);   // 1221 vs 1221
        double diff = SpeakerEmbedder.Cosine(ea, ex);    // 1221 vs 1089
        double diff2 = SpeakerEmbedder.Cosine(eb, ex);   // 1221 vs 1089 (other clip)

        Console.WriteLine("== Speaker discrimination ==");
        Console.WriteLine($"  same speaker  (1221_a vs 1221_b): cos = {same:F3}");
        Console.WriteLine($"  diff speaker  (1221_a vs 1089_x): cos = {diff:F3}");
        Console.WriteLine($"  diff speaker  (1221_b vs 1089_x): cos = {diff2:F3}");
        Console.WriteLine();

        double margin = same - Math.Max(diff, diff2);
        Console.WriteLine($"  margin (same - best diff): {margin:F3}");
        bool ok = same > diff && same > diff2 && margin > 0.10;
        Console.WriteLine(ok
            ? "PASS: same-speaker clearly more similar than different-speaker."
            : "WEAK/FAIL: separation poor - mel front-end params likely need tuning.");
        return ok ? 0 : 1;
    }
}
