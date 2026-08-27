using System;
using System.IO;
using System.Linq;

using Mpai.Core;
using Mpai.Osd.VisualScene;
using Mpai.Paf.Fir;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Mpai.Paf.Fir.Test;

// Real recognition test across DIFFERENT images.
//   arg 0 : enrol image  (e.g. Reagan #1)   -> enrolled as "Reagan"
//   arg 1..N : probe images (labelled on the command line as name=path)
//             each is identified against the DB; prints similarity + verdict.
//   optional trailing: --scrfd <path>  --arcface <path>  --threshold <float>
//
// Example:
//   FirRecogTest reagan1.jpg reagan2.jpg other.jpg
//   -> enrols reagan1 as "Reagan", identifies reagan2 and other against it.
//
// A GOOD result: reagan2 matches "Reagan" with HIGH similarity (same person,
// different photo, typically ~0.4-0.8); other does NOT match (low similarity,
// below threshold). That is genuine recognition, unlike self-vs-self (=1.000).
public static class Program
{
    public static int Main(string[] args)
    {
        string scrfdPath   = @"D:\AI\Models\scrfd_10g_bnkps.onnx";
        string arcfacePath = @"D:\AI\Models\glintr100.onnx";
        float threshold    = 0.35f;

        // Split flags from positional image args.
        var images = new System.Collections.Generic.List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scrfd":     scrfdPath   = args[++i]; break;
                case "--arcface":   arcfacePath = args[++i]; break;
                case "--threshold": threshold   = float.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture); break;
                default: images.Add(args[i]); break;
            }
        }

        if (images.Count < 2)
        {
            Console.WriteLine("Usage: FirRecogTest <enrolImage> <probeImage1> [probeImage2 ...] " +
                              "[--scrfd p] [--arcface p] [--threshold f]");
            Console.WriteLine("  First image is enrolled; the rest are identified against it.");
            return 1;
        }

        foreach (var p in images.Append(scrfdPath).Append(arcfacePath))
            if (!File.Exists(p)) { Console.WriteLine($"Not found: {p}"); return 1; }

        Console.WriteLine($"Threshold: {threshold:F2}");
        Console.WriteLine($"Enrol:     {images[0]}");
        Console.WriteLine();

        using var detector   = new ScrfdFaceDetector(scrfdPath);
        using var recogniser = new ArcFaceRecogniser(arcfacePath);
        var db = new FaceDatabase(threshold);

        // Embed the largest face in an image (the subject).
        float[]? EmbedMainFace(string path)
        {
            var bytes = File.ReadAllBytes(path);
            using var img = Image.Load<Rgb24>(bytes);
            var faces = detector.Detect(bytes);
            if (faces.Count == 0) { Console.WriteLine($"  ! no face in {Path.GetFileName(path)}"); return null; }
            // Largest box = the main subject.
            var f = faces.OrderByDescending(d => d.Width * d.Height).First();
            using var crop = FaceCrop.Crop(img, f.X1, f.Y1, f.X2, f.Y2);
            return recogniser.Embed(crop);
        }

        // 1. Enrol image 0 as "Reagan".
        var enrolEmb = EmbedMainFace(images[0]);
        if (enrolEmb is null) { Console.WriteLine("Cannot enrol - no face."); return 1; }
        db.Enrol("Reagan", enrolEmb);
        Console.WriteLine("Enrolled image[0] as 'Reagan'.");
        Console.WriteLine();

        // 2. Identify each probe.
        Console.WriteLine("== Recognition ==");
        for (int i = 1; i < images.Count; i++)
        {
            var emb = EmbedMainFace(images[i]);
            string name = Path.GetFileName(images[i]);
            if (emb is null) continue;

            // Raw similarity to the enrolled Reagan, plus the DB verdict.
            float sim = ArcFaceRecogniser.CosineSimilarity(emb, enrolEmb);
            var match = db.Identify(emb);
            string verdict = match is null
                ? $"NO MATCH (below {threshold:F2})"
                : $"MATCH -> {match.PersonId}";
            Console.WriteLine($"  {name,-28} sim={sim:F3}  {verdict}");
        }

        Console.WriteLine();
        Console.WriteLine("Good result: the 2nd Reagan photo -> MATCH (high sim); the other person -> NO MATCH (low sim).");
        return 0;
    }
}
