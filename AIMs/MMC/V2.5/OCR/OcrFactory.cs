using System.Collections.Generic;

namespace Mpai.Aims.Ocr;

// Builds the RapidOcrNet-backed OCR AIM from deployment settings.
//
// Settings (all optional): DetModel, ClsModel, RecModel, KeysFile.
// If DetModel is absent the AIM uses RapidOcrNet's bundled models.
public static class OcrFactory
{
    public static IOcrAim Create(
        IReadOnlyDictionary<string, string> settings)
    {
        var det = Get(settings, "DetModel");
        if (string.IsNullOrWhiteSpace(det))
            return new RapidOcrAim();   // bundled models

        return new RapidOcrAim(new RapidOcrConfiguration
        {
            DetModel = det,
            ClsModel = Get(settings, "ClsModel"),
            RecModel = Get(settings, "RecModel"),
            KeysFile = Get(settings, "KeysFile")
        });
    }

    private static string Get(
        IReadOnlyDictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var v) ? v : string.Empty;
}
