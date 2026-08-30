using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Mmc.Nlu;   // NluAimProcessor
using Mpai.Mmc.Esi;   // EsiAimProcessor
using Mpai.Mmc.Efi;   // EfiAimProcessor
using Mpai.Mmc.Psm;   // PsmAimProcessor
using Mpai.Mmc.Sir;          // WavReader path (static)
using Mpai.Osd.VisualScene;  // ScrfdFaceDetector

namespace Pse.Host;

// Composition root for the PSE test host: provides the four Personal-Status AIMs -
// Natural Language Understanding (Text PS), Entity Speech Interpretation (Speech PS),
// Entity Face Interpretation (Face PS), and Personal Status Multiplexing (assembles
// the Entity PS). First-pass engines (Phase A); the interfaces do not change when the
// engines are deepened.
public sealed class PseHostProvider : IAimProvider, IDisposable
{
    private readonly AmdStore _store;
    private ScrfdFaceDetector? _scrfd;
    private HSEmotionEstimator? _hse;
    private Wav2Vec2EmotionEstimator? _w2v2;

    public PseHostProvider(AmdStore store) => _store = store;

    private ScrfdFaceDetector Scrfd() => _scrfd ??= new ScrfdFaceDetector(@"D:\AI\Models\scrfd_10g_bnkps.onnx");
    private HSEmotionEstimator Hse()   => _hse   ??= new HSEmotionEstimator(@"D:\AI\Models\hsemotion_enet_b0_8_va_mtl.onnx");
    private Wav2Vec2EmotionEstimator W2v2() => _w2v2 ??= new Wav2Vec2EmotionEstimator(@"D:\AI\Models\w2v2-emotion\model.onnx");

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings)
        => aimName switch
        {
            "MMC-NLU-V2.5" => new NluAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            "MMC-ESI-V2.5" => new EsiAimProcessor(aimName, W2v2(), AimPortReader.Load(_store, aimName)),
            "MMC-EFI-V2.5" => new EfiAimProcessor(aimName, Scrfd(), Hse(), AimPortReader.Load(_store, aimName)),
            "MMC-PSM-V2.5" => new PsmAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            _ => throw new NotSupportedException($"PseHostProvider does not provide '{aimName}'.")
        };

    public void Dispose() { _scrfd?.Dispose(); _hse?.Dispose(); _w2v2?.Dispose(); }
}
