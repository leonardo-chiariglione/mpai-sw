using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Paf.Fir;
using Mpai.Mmc.Sir;
using Mpai.Hci.Idr;
using Mpai.Osd.VisualScene;

namespace Hci.Access.Host;

// Composition root for the HCI identity AIMs (the "check authorised users" app
// and the enrolment app). Constructs FIR, SIR and IDR as self-contained
// IAimProcessors, each reading its own port names from its instance JSON via the
// AmdStore.
//
// The three identity AIMs SHARE state: one SubjectGallery (the common name DB
// that FIR, SIR and IDR all search), one ArcFace recogniser, one ECAPA embedder
// and one SCRFD detector. This provider owns those single instances and injects
// them, so an enrolment is visible to every consumer and the models are loaded
// once. Model paths and the gallery path come from settings (with defaults).
public sealed class HciIdentityProvider : IAimProvider, IDisposable
{
    private readonly AmdStore _store;

    // Shared, lazily-built singletons.
    private SubjectGallery?    _gallery;
    private ArcFaceRecogniser? _arcFace;
    private SpeakerEmbedder?   _ecapa;
    private ScrfdFaceDetector? _scrfd;

    // Settings captured from the first Create call (all AIMs share one config).
    private IReadOnlyDictionary<string, string>? _settings;

    public HciIdentityProvider(AmdStore store) => _store = store;

    public IAimProcessor Create(
        string aimName,
        IReadOnlyDictionary<string, string> settings)
    {
        _settings ??= settings;

        return aimName switch
        {
            "PAF-FIR-V1.6" =>
                new FirAimProcessor(
                    aimName,
                    Scrfd(settings),
                    ArcFace(settings),
                    Gallery(settings),
                    AimPortReader.Load(_store, aimName)),

            "MMC-SIR-V2.5" =>
                new SirAimProcessor(
                    aimName,
                    Ecapa(settings),
                    Gallery(settings),
                    AimPortReader.Load(_store, aimName)),

            "HCI-IDR-V1.0" =>
                new IdrAimProcessor(
                    aimName,
                    AimPortReader.Load(_store, aimName)),

            _ => throw new NotSupportedException(
                     $"HciIdentityProvider does not provide '{aimName}'.")
        };
    }

    // ---- shared singletons (built once, from settings) --------------------

    private SubjectGallery Gallery(IReadOnlyDictionary<string, string> s) =>
        _gallery ??= SubjectGallery.Load(Setting(s, "GalleryPath", @"D:\AI\TestData\gallery.json"));

    private ArcFaceRecogniser ArcFace(IReadOnlyDictionary<string, string> s) =>
        _arcFace ??= new ArcFaceRecogniser(Setting(s, "ArcFaceModel", @"D:\AI\Models\glintr100.onnx"));

    private SpeakerEmbedder Ecapa(IReadOnlyDictionary<string, string> s) =>
        _ecapa ??= new SpeakerEmbedder(Setting(s, "EcapaModel", @"D:\AI\Models\ecapa-tdnn.onnx"));

    private ScrfdFaceDetector Scrfd(IReadOnlyDictionary<string, string> s) =>
        _scrfd ??= new ScrfdFaceDetector(Setting(s, "ScrfdModel", @"D:\AI\Models\scrfd_10g_bnkps.onnx"));

    // ---- settings helpers -------------------------------------------------

    private static string Setting(IReadOnlyDictionary<string, string> s, string key, string fallback) =>
        s.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    public void Dispose()
    {
        _arcFace?.Dispose();
        _ecapa?.Dispose();
        _scrfd?.Dispose();
    }
}
