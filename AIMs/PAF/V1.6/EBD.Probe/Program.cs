using System;
using System.Linq;
using Microsoft.ML.OnnxRuntime;

// Throwaway diagnostic: dump the BlazePose landmark ONNX input/output signature so
// the real estimator is built against the true shapes/names - exactly the role the
// YOLOX probe played. Prints each input/output name, element type, and dimensions.
//
//   dotnet run --project BlazePoseProbe -- [modelPath]
internal static class Program
{
    private static void Main(string[] args)
    {
        string modelPath = args.Length > 0 ? args[0] : @"D:\AI\Models\blazepose_landmarks_full.onnx";
        Console.WriteLine($"Loading {modelPath}");
        using var session = new InferenceSession(modelPath);

        Console.WriteLine();
        Console.WriteLine("=== INPUTS ===");
        foreach (var kv in session.InputMetadata)
        {
            var m = kv.Value;
            Console.WriteLine($"  {kv.Key}: type={m.ElementType}, dims=[{string.Join(",", m.Dimensions)}]");
        }

        Console.WriteLine();
        Console.WriteLine("=== OUTPUTS ===");
        foreach (var kv in session.OutputMetadata)
        {
            var m = kv.Value;
            Console.WriteLine($"  {kv.Key}: type={m.ElementType}, dims=[{string.Join(",", m.Dimensions)}]");
        }

        Console.WriteLine();
        Console.WriteLine("Expected (BlazePose GHUM full): input ~[1,256,256,3] or [1,3,256,256];");
        Console.WriteLine("outputs include a (1,195) landmark tensor = 33 keypoints x (x,y,z,visibility,presence),");
        Console.WriteLine("plus a (1,1) presence flag, and possibly a segmentation/world-landmarks output.");
    }
}
