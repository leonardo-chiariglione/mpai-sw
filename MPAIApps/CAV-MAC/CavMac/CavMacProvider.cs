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

using Mpai.Aims.Tts;     // TtsAimProcessor, TtsFactory (text -> speech)
using Mpai.Aims.Speech;  // SodAimProcessor, ISpeechDeliveryAim, WinmmSpeechDelivery (speech -> loudspeaker)

namespace CavMac;

// Composition root for CAV-MAC V2.0 (Multimodal Access Control). Builds the two
// description AIMs (PAF-EFD, MMC-ESD), the reconciliation AIM (HCI-IDR), and the
// spoken-prompt chain used to speak the instructions: MMC-TTS (text -> speech)
// followed by MMC-SOD (speech -> loudspeaker). Shares one ArcFace recogniser +
// SCRFD detector and one ECAPA embedder across the description AIMs, so models
// load once. Modelled on HciAccessControlProvider (identity AIMs) + TstProvider
// (TTS / delivery / devices).
internal sealed class CavMacProvider : IAimProvider, IDisposable
{
    private readonly AmdStore _store;

    private ArcFaceRecogniser? _arcFace;
    private SpeakerEmbedder?   _ecapa;
    private ScrfdFaceDetector? _scrfd;

    public CavMacProvider(AmdStore store) => _store = store;

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings)
        => aimName switch
        {
            "PAF-EFD-V1.6" =>
                new EfdAimProcessor(aimName, Scrfd(settings), ArcFace(settings), AimPortReader.Load(_store, aimName)),

            "MMC-ESD-V2.5" =>
                new EsdAimProcessor(aimName, Ecapa(settings), AimPortReader.Load(_store, aimName)),

            "HCI-IDR-V1.0" =>
                new IdrAimProcessor(aimName, AimPortReader.Load(_store, aimName)),

            "MMC-TTS-V2.5" =>
                new TtsAimProcessor(aimName, TtsFactory.Create(settings), AimPortReader.Load(_store, aimName)),

            "MMC-SOD-V2.5" =>
                new SodAimProcessor(aimName, Loudspeaker(), AimPortReader.Load(_store, aimName)),

            _ => throw new NotSupportedException($"CavMacProvider does not provide '{aimName}'.")
        };

    private ArcFaceRecogniser ArcFace(IReadOnlyDictionary<string, string> s) =>
        _arcFace ??= new ArcFaceRecogniser(Setting(s, "ArcFaceModel", @"D:\AI\Models\glintr100.onnx"));

    private SpeakerEmbedder Ecapa(IReadOnlyDictionary<string, string> s) =>
        _ecapa ??= new SpeakerEmbedder(Setting(s, "EcapaModel", @"D:\AI\Models\ecapa-tdnn.onnx"));

    private ScrfdFaceDetector Scrfd(IReadOnlyDictionary<string, string> s) =>
        _scrfd ??= new ScrfdFaceDetector(Setting(s, "ScrfdModel", @"D:\AI\Models\scrfd_10g_bnkps.onnx"));

    // Speech Object Delivery has its own device, independent of Audio Object
    // Delivery. On Windows this is the winmm-backed speech delivery.
    private static ISpeechDeliveryAim Loudspeaker()
    {
#if WINDOWS_DEVICES
        return new WinmmSpeechDelivery();
#else
        throw new PlatformNotSupportedException("No non-Windows speech delivery device is configured.");
#endif
    }

    private static string Setting(IReadOnlyDictionary<string, string> s, string key, string fallback) =>
        s.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    public void Dispose()
    {
        _arcFace?.Dispose();
        _ecapa?.Dispose();
        _scrfd?.Dispose();
    }
}
