using System;
using System.Linq;
using Microsoft.ML.OnnxRuntime;

// Throwaway diagnostic: dump the wav2vec2 dimensional-emotion ONNX input/output
// signature, so the real estimator is built against the true names/shapes - the
// same role the HSEmotion / BlazePose / YOLOX probes played.
//
//   dotnet run --project W2v2Probe -- [modelPath]
internal static class Program
{
    private static void Main(string[] args)
    {
        string modelPath = args.Length > 0 ? args[0] : @"D:\AI\Models\w2v2-emotion\model.onnx";
        Console.WriteLine($"Loading {modelPath}");
        using var session = new InferenceSession(modelPath);

        Console.WriteLine();
        Console.WriteLine("=== INPUTS ===");
        foreach (var kv in session.InputMetadata)
            Console.WriteLine($"  {kv.Key}: type={kv.Value.ElementType}, dims=[{string.Join(",", kv.Value.Dimensions)}]");

        Console.WriteLine();
        Console.WriteLine("=== OUTPUTS ===");
        foreach (var kv in session.OutputMetadata)
            Console.WriteLine($"  {kv.Key}: type={kv.Value.ElementType}, dims=[{string.Join(",", kv.Value.Dimensions)}]");

        Console.WriteLine();
        Console.WriteLine("Expected (audeering w2v2-L-robust-12): input 'signal' [1,-1] raw mono 16kHz float;");
        Console.WriteLine("outputs 'hidden_states' [1,1024] and 'logits' [1,3] = arousal, dominance, valence (~0..1).");
    }
}
