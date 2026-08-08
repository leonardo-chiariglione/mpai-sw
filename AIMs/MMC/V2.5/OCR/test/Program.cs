using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Mpai.Core;
using Mpai.Aims.Ocr;

namespace Mpai.Mmc.Ocr.Test;

// Standalone test harness for MMC-OCR-V2.5.
// Loads an image from disk, runs the OCR AIM with models from D:\AI\Models\OCR,
// and prints the recognised lines with confidence and bounding box.
internal static class Program
{
    private const string ImagePath = @"C:\Users\leona\Downloads\ocr-test.png";
    private const string ModelDir  = @"D:\AI\Models\OCR";

    private static async Task Main()
    {
        if (!File.Exists(ImagePath))
        {
            Console.WriteLine($"Image not found: {ImagePath}");
            return;
        }

        Console.WriteLine($"Loading image: {ImagePath}");
        var bytes = File.ReadAllBytes(ImagePath);
        var image = BasicVisualObject.FromFile(ImagePath, bytes);

        // Point the AIM at the models in D:\AI\Models\OCR
        var settings = new Dictionary<string, string>
        {
            ["DetModel"] = Path.Combine(ModelDir, "ch_PP-OCRv5_mobile_det.onnx"),
            ["ClsModel"] = Path.Combine(ModelDir, "ch_ppocr_mobile_v2.0_cls_infer.onnx"),
            ["RecModel"] = Path.Combine(ModelDir, "latin_PP-OCRv5_rec_mobile_infer.onnx"),
            ["KeysFile"] = Path.Combine(ModelDir, "ppocrv5_latin_dict.txt"),
        };

        Console.WriteLine($"Creating OCR AIM (models from {ModelDir})...");
        IOcrAim ocr;
        try
        {
            ocr = OcrFactory.Create(settings);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to create OCR AIM: " + ex.Message);
            Console.WriteLine(ex);
            return;
        }

        Console.WriteLine("Running OCR...");
        RecognisedText result;
        try
        {
            result = await ocr.ProcessAsync(image);
        }
        catch (Exception ex)
        {
            Console.WriteLine("OCR failed: " + ex);
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"Recognised {result.TextLines.Count} lines:");
        Console.WriteLine(new string('-', 70));
        foreach (var line in result.TextLines)
        {
            var box = line.BoundingBox;
            Console.WriteLine(
                $"[{line.Confidence:0.00}] " +
                $"({box.X,4},{box.Y,4} {box.Width,4}x{box.Height,3})  " +
                line.Text.GetText());
        }
        Console.WriteLine(new string('-', 70));
        Console.WriteLine($"Header: {result.Header}");
    }
}
