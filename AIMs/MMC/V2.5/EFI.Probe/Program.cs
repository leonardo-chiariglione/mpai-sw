using System;
using System.Linq;
using Microsoft.ML.OnnxRuntime;

// Throwaway diagnostic: dump the HSEmotion ONNX input/output signature so the real
// estimator is built against the true shapes/names - the same role the BlazePose
// and YOLOX probes played.
//
//   dotnet run --project HseProbe -- [modelPath]
internal static class Program
{
    private static void Main(string[] args)
    {
        string modelPath = args.Length > 0 ? args[0] : @"D:\AI\Models\hsemotion_enet_b0_8_va_mtl.onnx";
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
        Console.WriteLine("Expected (HSEmotion enet_b0_8_va_mtl): input ~[1,3,224,224] NCHW (or [1,224,224,3]);");
        Console.WriteLine("output(s): emotion logits [1,8] (Anger,Contempt,Disgust,Fear,Happiness,Neutral,Sadness,Surprise),");
        Console.WriteLine("plus valence and arousal (multi-task heads) - possibly one combined [1,10] tensor or separate.");
    }
}
