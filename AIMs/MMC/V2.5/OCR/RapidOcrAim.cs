using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Mpai.Core;

using RapidOcrNet;

// Disambiguate our data model TextLine from RapidOcrNet.TextLine.
using RtxTextLine = Mpai.Core.TextLine;

namespace Mpai.Aims.Ocr;

// RapidOcrNet implementation of MMC-OCR-V2.5.
// Uses the PaddleOCR PP-OCRv5 detection+recognition ONNX models (Apache-2.0),
// the same ONNX-Runtime family as the Whisper/BLIP/Piper AIMs.
// Image processing uses only SkiaSharp (no System.Drawing, no OpenCV).
//
// Models: the RapidOcrNet package ships the PP-OCRv5 latin models and copies
// them to models/v5/ next to the binary. With no config paths, InitModels()
// loads those bundled models. Explicit paths override them.
public sealed class RapidOcrAim : IOcrAim, IDisposable
{
    private readonly RapidOcr _ocr;

    public RapidOcrAim(RapidOcrConfiguration? config = null)
    {
        _ocr = new RapidOcr();

        if (config is not null && !string.IsNullOrWhiteSpace(config.DetModel))
        {
            _ocr.InitModels(
                config.DetModel,
                config.ClsModel,
                config.RecModel,
                config.KeysFile);
        }
        else
        {
            _ocr.InitModels();   // bundled PP-OCRv5 latin models
        }
    }

    public Task<RecognisedText> ProcessAsync(BasicVisualObject image)
    {
        // Prefer the file path; fall back to writing bytes to a temp file.
        string path;
        string? tempPath = null;
        if (!string.IsNullOrWhiteSpace(image.FileName) && File.Exists(image.FileName))
        {
            path = image.FileName;
        }
        else
        {
            tempPath = Path.Combine(Path.GetTempPath(),
                Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(tempPath, image.Data);
            path = tempPath;
        }

        try
        {
            var result = _ocr.Detect(path, RapidOcrOptions.Default);

            var lines = new List<RtxTextLine>();
            if (result?.TextBlocks is not null)
            {
                foreach (var block in result.TextBlocks)
                {
                    var xs = block.BoxPoints.Select(p => (int)p.X).ToArray();
                    var ys = block.BoxPoints.Select(p => (int)p.Y).ToArray();
                    int minX = xs.Min(), minY = ys.Min();
                    int maxX = xs.Max(), maxY = ys.Max();

                    double conf = block.CharScores is { Length: > 0 }
                        ? block.CharScores.Average()
                        : 0.0;

                    lines.Add(new RtxTextLine
                    {
                        Text        = BasicTextObject.FromText(block.Text ?? string.Empty),
                        Confidence  = conf,
                        BoundingBox = new BoundingBox
                        {
                            X = minX, Y = minY,
                            Width = maxX - minX, Height = maxY - minY
                        }
                    });
                }
            }

            // Reading order: top-to-bottom, then left-to-right.
            lines = lines
                .OrderBy(l => l.BoundingBox.Y)
                .ThenBy(l => l.BoundingBox.X)
                .ToList();

            return Task.FromResult(new RecognisedText
            {
                RecognisedTextID = Guid.NewGuid().ToString(),
                TextLines        = lines
            });
        }
        finally
        {
            if (tempPath is not null && File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    public void Dispose() => _ocr.Dispose();
}

// Optional model configuration. When DetModel is empty the AIM uses
// RapidOcrNet's bundled models.
public sealed class RapidOcrConfiguration
{
    public string DetModel { get; init; } = string.Empty;
    public string ClsModel { get; init; } = string.Empty;
    public string RecModel { get; init; } = string.Empty;
    public string KeysFile { get; init; } = string.Empty;
}
