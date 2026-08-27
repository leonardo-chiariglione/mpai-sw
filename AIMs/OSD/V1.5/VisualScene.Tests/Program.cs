using System;
using System.IO;
using System.Linq;

using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Osd.VisualScene;

namespace Mpai.Osd.VisualScene.Test;

// Minimal harness to run the SCRFD detector + visual pipeline on a real image.
//   arg 0 : image path (required)      e.g. "C:\...\R.Reagan.jpg"
//   arg 1 : model path (optional)      default D:\AI\Models\scrfd_10g_bnkps.onnx
//
// Prints the model's real input/output metadata FIRST (the key diagnostic for
// tuning the decode), then the detections.
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: ScrfdTest <imagePath> [modelPath]");
            return 1;
        }

        string imagePath = args[0];
        string modelPath = args.Length >= 2
            ? args[1]
            : @"D:\AI\Models\scrfd_10g_bnkps.onnx";

        if (!File.Exists(imagePath))
        {
            Console.WriteLine($"Image not found: {imagePath}");
            return 1;
        }
        if (!File.Exists(modelPath))
        {
            Console.WriteLine($"Model not found: {modelPath}");
            Console.WriteLine("Stage scrfd_10g_bnkps.onnx (Apache-2.0, fal/AuraFace-v1) under D:\\AI\\Models\\.");
            return 1;
        }

        Console.WriteLine($"Image: {imagePath}");
        Console.WriteLine($"Model: {modelPath}");
        Console.WriteLine();

        // ---- 1. Dump the model's real I/O metadata (diagnostic) -------------
        // If detections are wrong, THIS tells us how the outputs are actually
        // laid out (names encode the stride), so the decode can be fixed exactly.
        using (var session = new Microsoft.ML.OnnxRuntime.InferenceSession(modelPath))
        {
            Console.WriteLine("== Model inputs ==");
            foreach (var kv in session.InputMetadata)
                Console.WriteLine($"  {kv.Key}: [{string.Join(",", kv.Value.Dimensions)}]  {kv.Value.ElementType}");

            Console.WriteLine("== Model outputs ==");
            foreach (var kv in session.OutputMetadata)
                Console.WriteLine($"  {kv.Key}: [{string.Join(",", kv.Value.Dimensions)}]  {kv.Value.ElementType}");
            Console.WriteLine();
        }

        // ---- 2. Run detector + pipeline -------------------------------------
        var bytes = File.ReadAllBytes(imagePath);
        var visual = BasicVisualObject.FromFile(imagePath, bytes);

        using var detector = new ScrfdFaceDetector(modelPath);

        // Raw detections (box + score + landmarks).
        var faces = detector.Detect(bytes);
        Console.WriteLine($"== Detections: {faces.Count} face(s) ==");
        int i = 0;
        foreach (var f in faces)
        {
            Console.WriteLine(
                $"  [{i++}] score={f.Score:F3}  " +
                $"box=({f.X1:F0},{f.Y1:F0})-({f.X2:F0},{f.Y2:F0})  " +
                $"size={f.Width:F0}x{f.Height:F0}");
            for (int k = 0; k < f.Landmarks.Length; k++)
                Console.WriteLine($"        kp{k}=({f.Landmarks[k].X:F0},{f.Landmarks[k].Y:F0})");
        }
        Console.WriteLine();

        // ---- 3. Run the full pipeline -> BasicVisualSceneDescriptors --------
        var pipeline = new AvsVisualPipeline(detector);
        BasicVisualSceneDescriptors scene = pipeline.Process(visual);
        Console.WriteLine($"== Scene: {scene.VisualObjectCount} entr(y/ies), header={scene.Header} ==");
        int e = 0;
        foreach (var entry in scene.BasicVisualSceneDescriptorsEntries)
        {
            var sph = entry.PointOfView.SpherPosition;
            string bearing = sph is null ? "(none)" : $"az={sph[1]:F1} deg, el={sph[2]:F1} deg";
            Console.WriteLine($"  entry[{e++}] bearing: {bearing}");
        }

        return 0;
    }
}
