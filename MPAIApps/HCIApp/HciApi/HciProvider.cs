using System;
using System.Collections.Generic;
using AIF.Controller;
using AIF.Store;
using Mpai.Mmc.Edp;   // EdpAimProcessor, OllamaClient
using Mpai.Paf.Psd;   // PsdAimProcessor
using Mpai.Paf.Gfd;   // GfdAimProcessor
using Mpai.Aims.Tts;  // TtsAimProcessor, TtsFactory
using Mpai.Aims.Asr;  // AsrAimProcessor, AsrFactory
using Mpai.Aims.Ttt;  // TttAimProcessor, TttFactory
using Mpai.Mmc.Nlu;   // NluAimProcessor
using Mpai.Mmc.Esi;   // EsiAimProcessor (+ Wav2Vec2EmotionEstimator)
using Mpai.Mmc.Efi;   // EfiAimProcessor (+ HSEmotionEstimator)
using Mpai.Mmc.Psm;   // PsmAimProcessor
using Mpai.Osd.Bas;
using Mpai.Osd.Bvs;
using Mpai.Osd.Bls;

using Mpai.Osd.Ava;
using Mpai.Cve.Vsi;
using Mpai.Cae.Asi;
using Mpai.Osd.VisualScene;
using Mpai.Cae.Qcv;
using Mpai.Cae.Aii;
namespace Mpai.Hci.Api;
// Composition root for the HCI middleware Modules the API facade runs. It supplies
// the LEAF AIM implementations the Controller instantiates; composites (RSR, PSE)
// need no case here - the Controller builds them from their L3 and serves their leaves.
public sealed class HciProvider : IAimProvider, IDisposable
{
    private readonly AmdStore _store;
    private Wav2Vec2EmotionEstimator? _w2v2;
    private Wav2Vec2EmotionEstimator W2v2() =>
        _w2v2 ??= new Wav2Vec2EmotionEstimator(@"D:\AI\Models\w2v2-emotion\model.onnx");
    private HSEmotionEstimator? _hse;
    private HSEmotionEstimator Hse() => _hse ??= new HSEmotionEstimator(@"D:\\AI\\Models\\hsemotion_enet_b0_8_va_mtl.onnx");
    private OllamaClient?     _llm;
    public HciProvider(AmdStore store) => _store = store;
        private ScrfdFaceDetector? _vsiScrfd;
    private ScrfdFaceDetector VsiScrfd() => _vsiScrfd ??= new ScrfdFaceDetector(@"D:\AI\Models\scrfd_10g_bnkps.onnx");

        private SoundClassifier? _aiiYamnet;
    private SoundClassifier AiiYamnet() => _aiiYamnet ??= new SoundClassifier(@"D:\AI\Models\yamnet.onnx", @"D:\AI\Models\yamnet_class_map.csv");

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings)
        => aimName switch
        {
            "MMC-ASR-V2.5" => new AsrAimProcessor(aimName, AsrFactory.Create(settings), AimPortReader.Load(_store, aimName)),
            "MMC-TTT-V2.5" => new TttAimProcessor(aimName, TttFactory.Create(settings), AimPortReader.Load(_store, aimName)),
            "MMC-EDP-V2.5" => new EdpAimProcessor(aimName, Llm(settings), AimPortReader.Load(_store, aimName)),
            "PAF-PSD-V1.6" => new PsdAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            "MMC-TTS-V2.5" => new TtsAimProcessor(aimName, TtsFactory.Create(settings), AimPortReader.Load(_store, aimName)),
            "PAF-GFD-V1.6" => new GfdAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            // Personal-Status pipeline (MMC-MPD, speech-only): understand + perceive feeling.
            "MMC-NLU-V2.5" => new NluAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            "MMC-ESI-V2.5" => new EsiAimProcessor(aimName, W2v2(), AimPortReader.Load(_store, aimName)),
            "MMC-EFI-V2.5" => new EfiAimProcessor(aimName, Hse(), AimPortReader.Load(_store, aimName)),
            "MMC-PSM-V2.5" => new PsmAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            "OSD-BAS-V1.5" => new BasAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            "OSD-BVS-V1.5" => new BvsAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            "OSD-BLS-V1.5" => new BlsAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            "OSD-AVA-V1.5" => new OsdAvaAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            "CAE-ASI-V2.5" => new CaeAsiAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            "CVE-VSI-V1.0" => new CveVsiAimProcessor(aimName, VsiScrfd(), AimPortReader.Load(_store, aimName)),
            "CAE-QCV-V1.0" => new QcvAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            "CAE-AII-V2.5" => new CaeAiiAimProcessor(aimName, AiiYamnet(), AimPortReader.Load(_store, aimName)),
            _ => throw new NotSupportedException($"HciProvider does not provide '{aimName}'.")
        };
    private OllamaClient Llm(IReadOnlyDictionary<string, string> settings)
    {
        if (_llm is not null) return _llm;
        string model = settings.TryGetValue("OllamaModel", out var m) && !string.IsNullOrWhiteSpace(m) ? m : "llama3.1";
        return _llm = new OllamaClient(model);
    }
    public void Dispose() { _aiiYamnet?.Dispose(); _llm?.Dispose(); _w2v2?.Dispose(); _hse?.Dispose(); }
}