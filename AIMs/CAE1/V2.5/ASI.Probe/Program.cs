using System;
using System.IO;
using System.Linq;

using Microsoft.ML.OnnxRuntime;

namespace Mpai.Cae.Asi.Probe;

// Loads the YAMNet ONNX model and prints its input/output signature, to confirm
// how ASI must feed audio and read class scores.
//   Expected (per model card): input raw mono 16 kHz waveform f32 [-1] (mel
//   frontend internal -> NO C# DSP needed), output_0 f32 [-1, 521] per-frame
//   AudioSet class scores (Speech=0), plus embeddings/log-mel outputs (unused).
//   arg 0: model path (default D:\AI\Models\yamnet.onnx)
public static class Program
{
    public static int Main(string[] args)
    {
        string modelPath = args.Length >= 1 ? args[0] : @"D:\AI\Models\yamnet.onnx";
        if (!File.Exists(modelPath)) { Console.WriteLine($"Model not found: {modelPath}"); return 1; }

        Console.WriteLine($"Model: {modelPath}");
        Console.WriteLine($"Size:  {new FileInfo(modelPath).Length / 1024.0 / 1024.0:F1} MB");
        Console.WriteLine();

        using var session = new InferenceSession(modelPath);

        Console.WriteLine("== Inputs ==");
        foreach (var kv in session.InputMetadata)
            Console.WriteLine($"  {kv.Key}: [{string.Join(",", kv.Value.Dimensions)}]  {kv.Value.ElementType}");

        Console.WriteLine("== Outputs ==");
        foreach (var kv in session.OutputMetadata)
            Console.WriteLine($"  {kv.Key}: [{string.Join(",", kv.Value.Dimensions)}]  {kv.Value.ElementType}");

        Console.WriteLine();
        Console.WriteLine("Interpretation:");
        Console.WriteLine("  input [-1] or [1,-1] f32 -> raw 16kHz waveform, feed samples directly (no DSP).");
        Console.WriteLine("  output [-1,521]          -> per-frame class scores; mean over frames, argmax = top class.");
        return 0;
    }
}
