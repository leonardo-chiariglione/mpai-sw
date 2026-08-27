using System;
using System.IO;

using Mpai.Mmc.Sir;

namespace Mpai.Mmc.Sir.RecogTest;

// Stage 2 verification against the shared OSD-IID output.
//   Enrol 1221 (clip a) and 1089 (clip x).
//   Identify held-out clip b (truly 1221) -> primary candidate "1221" at
//     ["sound","speech","speaker"].
//   1089 vs a 1221-only DB -> not recognised, so primary is "speech" at the
//     coarser ["sound","speech"] layer (NOT an empty list).
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
        var db = new SpeakerDatabase(threshold: 0.45f);
        db.Enrol("1221", embedder.Embed(WavReader.ReadMono16k(a)));
        db.Enrol("1089", embedder.Embed(WavReader.ReadMono16k(x)));
        Console.WriteLine($"Enrolled {db.Count} speakers: 1221, 1089");
        Console.WriteLine();

        var sir = new SpeakerIdentityRecognitionAim(embedder, db);

        var idB = sir.Identify(WavReader.ReadMono16k(b));
        var pB = idB.InstanceIdentifierData[0];
        Console.WriteLine("== Recognition ==");
        Console.WriteLine($"  clip b (truly 1221): label='{pB.InstanceLabel}' conf={pB.LabelConfidenceLevel:F3} layer=[{string.Join(",", pB.Taxonomy.TaxonomyLevelIDs)}]");

        // 1089 against a 1221-only DB -> coarser "speech" layer.
        var db1221 = new SpeakerDatabase(threshold: 0.45f);
        db1221.Enrol("1221", embedder.Embed(WavReader.ReadMono16k(a)));
        using var sir1221 = new SpeakerIdentityRecognitionAim(embedder, db1221);
        var idU = sir1221.Identify(WavReader.ReadMono16k(x));
        var pU = idU.InstanceIdentifierData[0];
        Console.WriteLine($"  clip x (1089) vs 1221-only DB: label='{pU.InstanceLabel}' layer=[{string.Join(",", pU.Taxonomy.TaxonomyLevelIDs)}]");
        Console.WriteLine();

        bool ok = pB.InstanceLabel == "1221"
               && pB.Taxonomy.TaxonomyLevelIDs.Count == 3
               && pU.InstanceLabel == "speech"
               && pU.Taxonomy.TaxonomyLevelIDs.Count == 2;
        Console.WriteLine(ok
            ? "PASS: known speaker identified at speaker layer; stranger falls back to speech layer (schema-valid, not empty)."
            : "FAIL: recognition/layering not as expected.");
        return ok ? 0 : 1;
    }
}
