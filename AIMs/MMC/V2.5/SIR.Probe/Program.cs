using System;
using System.IO;
using System.Linq;

using Microsoft.ML.OnnxRuntime;

namespace Mpai.Mmc.Sir.Probe;

// Loads the ECAPA ONNX model and prints its input/output tensor signature.
// This decides how SIR must preprocess audio:
//   - if the input is a long 1-D / [1, N] waveform -> the model featurises
//     internally; C# just feeds raw samples (easy, like FIR).
//   - if the input is [1, T, 80] or [1, 80, T] -> the model expects log-Mel
//     features; C# must compute the mel-spectrogram (real DSP).
//   arg 0: model path (default D:\AI\Models\ecapa-tdnn.onnx)
public static class Program
{
    public static int Main(string[] args)
    {
        string modelPath = args.Length >= 1 ? args[0] : @"D:\AI\Models\ecapa-tdnn.onnx";
        if (!File.Exists(modelPath))
        {
            Console.WriteLine($"Model not found: {modelPath}");
            Console.WriteLine("Download AXERA-TECH/3D-Speaker/ecapa-tdnn.onnx (MIT) to D:\\AI\\Models\\.");
            return 1;
        }

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
        Console.WriteLine("  input like [1, N] or [N] (large/dynamic) -> raw waveform (easy).");
        Console.WriteLine("  input like [1, T, 80] or [1, 80, T]      -> log-Mel features (need DSP).");
        Console.WriteLine("  output [1, D]                            -> D-dim speaker embedding (cosine compare).");
        return 0;
    }
}
