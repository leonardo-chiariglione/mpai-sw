using System;
using System.IO;
using System.Linq;

using Mpai.Paf.Ebd;

// Standalone proof that BlazePoseEstimator gives sane 3D body landmarks - the same
// de-risking step SCRFD/YOLOX got before being wrapped as AIMs. Runs on an image
// (ideally already a person crop; leonardo.jpg works) and prints presence + the 33
// world-space 3D keypoints.
//
//   dotnet run --project <this> [imagePath]
internal static class Program
{
    private static void Main(string[] args)
    {
        string modelPath = @"D:\AI\Models\pose_landmarks_detector_full.onnx";
        string imagePath = args.Length > 0 ? args[0] : @"D:\AI\TestData\Images\leonardo.jpg";

        if (!File.Exists(modelPath)) { Console.WriteLine($"model not found: {modelPath}"); return; }
        if (!File.Exists(imagePath)) { Console.WriteLine($"image not found: {imagePath}"); return; }

        Console.WriteLine($"Loading BlazePose from {modelPath}");
        using var estimator = new BlazePoseEstimator(modelPath);

        Console.WriteLine($"Estimating 3D body pose in {imagePath}");
        var result = estimator.Estimate(File.ReadAllBytes(imagePath));

        Console.WriteLine();
        Console.WriteLine($"presence = {result.Presence:F3}");
        Console.WriteLine($"=> {result.Keypoints.Count} body keypoints (world metres, hip-centred):");
        foreach (var k in result.Keypoints)
            Console.WriteLine($"   {k.Name,-18} ({k.X,7:F3},{k.Y,7:F3},{k.Z,7:F3})  vis={k.Visibility:F2}");
    }
}
