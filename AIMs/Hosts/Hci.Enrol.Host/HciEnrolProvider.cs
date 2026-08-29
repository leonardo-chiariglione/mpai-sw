using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Paf.Fir;   // ArcFaceRecogniser, FaceCrop (shared primitives)
using Mpai.Mmc.Sir;   // SpeakerEmbedder, WavReader (shared primitives)
using Mpai.Paf.Efd;   // EfdAimProcessor
using Mpai.Mmc.Esd;   // EsdAimProcessor
using Mpai.Osd.VisualScene;

namespace Hci.Enrol.Host;

// Composition root for the HCI enrolment app. Constructs the two description
// AIMs - Entity Face Description (PAF-EFD) and Entity Speech Description
// (MMC-ESD) - as self-contained IAimProcessors, each reading its own port names
// from its instance JSON via the AmdStore.
//
// EFD and ESD run the same feature extractors the recognition AIMs use - one
// ArcFace recogniser + SCRFD detector for faces, one ECAPA embedder for speech -
// so enrolment and recognition embed identically and their descriptors are
// comparable. This provider owns those single instances and injects them, so the
// models load once. Model paths come from settings (with defaults).
//
// Modelled on HciIdentityProvider; the description AIMs are the enrolment
// counterpart of that host's recognition AIMs.
public sealed class HciEnrolProvider : IAimProvider, IDisposable
{
    private readonly AmdStore _store;

    private ArcFaceRecogniser? _arcFace;
    private SpeakerEmbedder?   _ecapa;
    private ScrfdFaceDetector? _scrfd;

    public HciEnrolProvider(AmdStore store) => _store = store;

    public IAimProcessor Create(
        string aimName,
        IReadOnlyDictionary<string, string> settings)
        => aimName switch
        {
            "PAF-EFD-V1.6" =>
                new EfdAimProcessor(
                    aimName,
                    Scrfd(settings),
                    ArcFace(settings),
                    AimPortReader.Load(_store, aimName)),

            "MMC-ESD-V2.5" =>
                new EsdAimProcessor(
                    aimName,
                    Ecapa(settings),
                    AimPortReader.Load(_store, aimName)),

            _ => throw new NotSupportedException(
                     $"HciEnrolProvider does not provide '{aimName}'.")
        };

    // ---- shared singletons (built once, from settings) --------------------

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
