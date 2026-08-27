using System;
using System.IO;
using System.Linq;

using Mpai.Core;
using Mpai.Osd.VisualScene;
using Mpai.Paf.Fir;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Mpai.Paf.Fir.Test;

// End-to-end FIR sanity check on a real image:
//   arg 0 : image path (required)
//   arg 1 : SCRFD model path (optional, default D:\AI\Models\scrfd_10g_bnkps.onnx)
//   arg 2 : ArcFace model path (optional, default D:\AI\Models\glintr100.onnx)
//
// Flow: detect faces (SCRFD) -> crop each -> embed (ArcFace) -> enrol face 0
// as "TestPerson" -> identify every face against the DB. Face 0 should match
// itself with similarity ~1.0, proving detect+crop+embed+match all work.
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: FirTest <imagePath> [scrfdModel] [arcfaceModel]");
            return 1;
        }

        string imagePath  = args[0];
        string scrfdPath  = args.Length >= 2 ? args[1] : @"D:\AI\Models\scrfd_10g_bnkps.onnx";
        string arcfacePath = args.Length >= 3 ? args[2] : @"D:\AI\Models\glintr100.onnx";

        foreach (var (label, path) in new[] { ("Image", imagePath), ("SCRFD", scrfdPath), ("ArcFace", arcfacePath) })
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"{label} not found: {path}");
                if (label == "ArcFace")
                    Console.WriteLine("Stage glintr100.onnx (Apache-2.0, fal/AuraFace-v1) under D:\\AI\\Models\\.");
                return 1;
            }
        }

        Console.WriteLine($"Image:   {imagePath}");
        Console.WriteLine($"SCRFD:   {scrfdPath}");
        Console.WriteLine($"ArcFace: {arcfacePath}");
        Console.WriteLine();

        // Dump the ArcFace model I/O (diagnostic, like the SCRFD test).
        using (var s = new Microsoft.ML.OnnxRuntime.InferenceSession(arcfacePath))
        {
            Console.WriteLine("== ArcFace model inputs ==");
            foreach (var kv in s.InputMetadata)
                Console.WriteLine($"  {kv.Key}: [{string.Join(",", kv.Value.Dimensions)}]");
            Console.WriteLine("== ArcFace model outputs ==");
            foreach (var kv in s.OutputMetadata)
                Console.WriteLine($"  {kv.Key}: [{string.Join(",", kv.Value.Dimensions)}]");
            Console.WriteLine();
        }

        var bytes = File.ReadAllBytes(imagePath);
        using var image = Image.Load<Rgb24>(bytes);

        // 1. Detect.
        using var detector = new ScrfdFaceDetector(scrfdPath);
        var faces = detector.Detect(bytes);
        Console.WriteLine($"Detected {faces.Count} face(s).");
        if (faces.Count == 0)
        {
            Console.WriteLine("No faces - cannot test recognition. (Check the SCRFD decode first.)");
            return 0;
        }

        // 2. Crop + 3. Embed each face.
        using var recogniser = new ArcFaceRecogniser(arcfacePath);
        var db = new FaceDatabase(threshold: 0.35f);
        var embeddings = new float[faces.Count][];

        for (int i = 0; i < faces.Count; i++)
        {
            var f = faces[i];
            using var crop = FaceCrop.Crop(image, f.X1, f.Y1, f.X2, f.Y2);
            embeddings[i] = recogniser.Embed(crop);
            Console.WriteLine($"  face[{i}] embedded: {embeddings[i].Length}-d, " +
                              $"first values [{embeddings[i][0]:F3}, {embeddings[i][1]:F3}, ...]");
        }
        Console.WriteLine();

        // 4. Enrol face 0.
        db.Enrol("TestPerson", embeddings[0]);
        Console.WriteLine("Enrolled face[0] as 'TestPerson'.");

        // 5. Identify every face. face[0] must match itself ~1.0.
        for (int i = 0; i < faces.Count; i++)
        {
            var m = db.Identify(embeddings[i]);
            string result = m is null ? "no match" : $"{m.PersonId} (sim={m.Similarity:F3})";
            Console.WriteLine($"  identify face[{i}] -> {result}");
        }
        Console.WriteLine();
        Console.WriteLine("Expected: face[0] -> TestPerson (sim ~1.000). If so, detect+crop+embed+match all work.");
        return 0;
    }
}
