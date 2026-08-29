using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Paf.Fir;   // ArcFaceRecogniser, FaceCrop (shared primitives)
using Mpai.Mmc.Sir;   // SpeakerEmbedder, WavReader (shared primitives)
using Mpai.Paf.Efd;   // EfdAimProcessor
using Mpai.Mmc.Esd;   // EsdAimProcessor
using Mpai.Hci.Idr;   // IdrAimProcessor
using Mpai.Osd.VisualScene;

namespace Hci.AccessControl.Host;

// Composition root for the "check authorised users" app (path A): describe with
// EFD/ESD, then reconcile with IDR. Constructs the two description AIMs and the
// reconciliation AIM, each self-contained, reading its own port names from its
// instance JSON via the AmdStore. Shares one ArcFace recogniser + SCRFD detector
// and one ECAPA embedder across the description AIMs, so models load once.
public sealed class HciAccessControlProvider : IAimProvider, IDisposable
{
    private readonly AmdStore _store;

    private ArcFaceRecogniser? _arcFace;
    private SpeakerEmbedder?   _ecapa;
    private ScrfdFaceDetector? _scrfd;

    public HciAccessControlProvider(AmdStore store) => _store = store;

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings)
        => aimName switch
        {
            "PAF-EFD-V1.6" =>
                new EfdAimProcessor(aimName, Scrfd(settings), ArcFace(settings), AimPortReader.Load(_store, aimName)),

            "MMC-ESD-V2.5" =>
                new EsdAimProcessor(aimName, Ecapa(settings), AimPortReader.Load(_store, aimName)),

            "HCI-IDR-V1.0" =>
                new IdrAimProcessor(aimName, AimPortReader.Load(_store, aimName)),

            _ => throw new NotSupportedException($"HciAccessControlProvider does not provide '{aimName}'.")
        };

    private ArcFaceRecogniser ArcFace(IReadOnlyDictionary<string, string> s) =>
        _arcFace ??= new ArcFaceRecogniser(Setting(s, "ArcFaceModel", @"D:\AI\Models\glintr100.onnx"));

    private SpeakerEmbedder Ecapa(IReadOnlyDictionary<string, string> s) =>
        _ecapa ??= new SpeakerEmbedder(Setting(s, "EcapaModel", @"D:\AI\Models\ecapa-tdnn.onnx"));

    private ScrfdFaceDetector Scrfd(IReadOnlyDictionary<string, string> s) =>
        _scrfd ??= new ScrfdFaceDetector(Setting(s, "ScrfdModel", @"D:\AI\Models\scrfd_10g_bnkps.onnx"));

    private static string Setting(IReadOnlyDictionary<string, string> s, string key, string fallback) =>
        s.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    public void Dispose()
    {
        _arcFace?.Dispose();
        _ecapa?.Dispose();
        _scrfd?.Dispose();
    }
}
