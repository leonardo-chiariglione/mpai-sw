using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Osd.VisualScene;   // YoloxObjectDetector
using Mpai.Osd.Vii;           // ViiAimProcessor

namespace Vii.Host;

// Composition root for the OSD-VII test host. Constructs the Visual Instance
// Identification AIM, self-contained, reading its own port names from its instance
// JSON via the AmdStore. Holds one YOLOX detector so the model loads once.
public sealed class ViiHostProvider : IAimProvider, IDisposable
{
    private readonly AmdStore _store;
    private YoloxObjectDetector? _yolox;

    public ViiHostProvider(AmdStore store) => _store = store;

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings)
        => aimName switch
        {
            "OSD-VII-V1.5" =>
                new ViiAimProcessor(aimName, Yolox(settings), AimPortReader.Load(_store, aimName)),

            _ => throw new NotSupportedException($"ViiHostProvider does not provide '{aimName}'.")
        };

    private YoloxObjectDetector Yolox(IReadOnlyDictionary<string, string> s) =>
        _yolox ??= new YoloxObjectDetector(Setting(s, "YoloxModel", @"D:\AI\Models\yolox_s.onnx"));

    private static string Setting(IReadOnlyDictionary<string, string> s, string key, string fallback) =>
        s.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    public void Dispose() => _yolox?.Dispose();
}
