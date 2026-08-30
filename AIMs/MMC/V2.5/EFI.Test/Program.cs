using System;
using System.IO;
using System.Linq;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using Mpai.Osd.VisualScene;   // ScrfdFaceDetector
using Mpai.Paf.Fir;           // FaceCrop
using Mpai.Mmc.Efi;           // HSEmotionEstimator, FaceAffect

// Standalone proof that HSEmotion reads facial affect - SCRFD detect+crop the face,
// then HSEmotion. Prints the top emotion, its confidence, valence, arousal, and the
// full probability map. Same de-risking the detectors got before wrapping as AIMs.
//
//   dotnet run --project <this> [imagePath]
internal static class Program
{
    private static void Main(string[] args)
    {
        string scrfd = @"D:\AI\Models\scrfd_10g_bnkps.onnx";
        string model = @"D:\AI\Models\hsemotion_enet_b0_8_va_mtl.onnx";
        string image = args.Length > 0 ? args[0] : @"D:\AI\TestData\Images\leonardo.jpg";
        if (!File.Exists(image)) { Console.WriteLine($"image not found: {image}"); return; }

        using var detector = new ScrfdFaceDetector(scrfd);
        using var hse = new HSEmotionEstimator(model);

        var bytes = File.ReadAllBytes(image);
        using var img = Image.Load<Rgb24>(bytes);
        var faces = detector.Detect(bytes).OrderByDescending(f => f.Width * f.Height).ToList();
        Console.WriteLine($"faces detected: {faces.Count}");

        using var crop = faces.Count > 0
            ? FaceCrop.Crop(img, faces[0].X1, faces[0].Y1, faces[0].X2, faces[0].Y2)
            : img.Clone();

        FaceAffect a = hse.Estimate(crop);
        Console.WriteLine();
        Console.WriteLine($"Top emotion: {a.Emotion} (confidence {a.Confidence:F3})");
        Console.WriteLine($"Valence: {a.Valence:F3}   Arousal: {a.Arousal:F3}");
        Console.WriteLine("All probabilities:");
        foreach (var kv in a.Probabilities.OrderByDescending(k => k.Value))
            Console.WriteLine($"   {kv.Key,-10} {kv.Value:F3}");
    }
}
