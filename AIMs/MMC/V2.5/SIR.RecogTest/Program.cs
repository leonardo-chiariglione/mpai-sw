using System;
using System.IO;

using Mpai.Mmc.Sir;

namespace Mpai.Mmc.Sir.RecogTest;

// Stage 2 verification: real recognition against an enrolled database.
//   Enrol speaker "1221" from clip a, and "1089" from clip x.
//   Identify clip b (1221, held out) -> should be "1221".
//   Raise the threshold and re-identify 1089 against a 1221-only DB -> unknown.
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

        // Enrol 1221 (clip a) and 1089 (clip x).
        db.Enrol("1221", embedder.Embed(WavReader.ReadMono16k(a)));
        db.Enrol("1089", embedder.Embed(WavReader.ReadMono16k(x)));
        Console.WriteLine($"Enrolled {db.Count} speakers: 1221, 1089");
        Console.WriteLine();

        var sir = new SpeakerIdentityRecognitionAim(embedder, db);

        // Identify held-out clip b (truly speaker 1221).
        var idB = sir.Identify(WavReader.ReadMono16k(b));
        string labelB = idB.InstanceIdentifierData.Count > 0 ? idB.InstanceIdentifierData[0].InstanceLabel : "(unknown)";
        double confB = idB.InstanceIdentifierData.Count > 0 ? idB.InstanceIdentifierData[0].LabelConfidenceLevel : 0;
        Console.WriteLine("== Recognition ==");
        Console.WriteLine($"  clip b (truly 1221) identified as: {labelB}  (conf {confB:F3})");

        // Unknown rejection: a database with ONLY 1221, asked to identify 1089.
        var db1221 = new SpeakerDatabase(threshold: 0.45f);
        db1221.Enrol("1221", embedder.Embed(WavReader.ReadMono16k(a)));
        using var sir1221 = new SpeakerIdentityRecognitionAim(embedder, db1221);
        var idUnknown = sir1221.Identify(WavReader.ReadMono16k(x));  // 1089 vs 1221-only DB
        bool rejected = idUnknown.InstanceIdentifierData.Count == 0;
        Console.WriteLine($"  clip x (1089) vs 1221-only DB: {(rejected ? "correctly UNKNOWN" : "WRONGLY matched " + idUnknown.InstanceIdentifierData[0].InstanceLabel)}");
        Console.WriteLine();

        bool ok = labelB == "1221" && rejected;
        Console.WriteLine(ok
            ? "PASS: held-out clip identified to the right speaker; stranger rejected as unknown."
            : "FAIL: recognition or rejection not as expected.");
        return ok ? 0 : 1;
    }
}
