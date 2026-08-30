using System;
using System.IO;

using Mpai.Mmc.Sir;   // WavReader
using Mpai.Mmc.Esi;   // Wav2Vec2EmotionEstimator, SpeechAffect

// Standalone proof that wav2vec2 reads dimensional speech affect from a WAV.
//   dotnet run --project <this> [wavPath]
internal static class Program
{
    private static void Main(string[] args)
    {
        string model = @"D:\AI\Models\w2v2-emotion\model.onnx";
        string wav   = args.Length > 0 ? args[0] : @"D:\AI\TestData\Audio\leonardo.wav";
        if (!File.Exists(wav)) { Console.WriteLine($"wav not found: {wav}"); return; }

        Console.WriteLine($"Loading wav2vec2 from {model}");
        using var est = new Wav2Vec2EmotionEstimator(model);

        var samples = WavReader.ReadMono16k(File.ReadAllBytes(wav));
        Console.WriteLine($"samples: {samples.Length} ({samples.Length / 16000.0:F1}s)");

        SpeechAffect a = est.Estimate(samples);
        Console.WriteLine();
        Console.WriteLine($"Arousal:   {a.Arousal:F3}");
        Console.WriteLine($"Dominance: {a.Dominance:F3}");
        Console.WriteLine($"Valence:   {a.Valence:F3}");
    }
}
