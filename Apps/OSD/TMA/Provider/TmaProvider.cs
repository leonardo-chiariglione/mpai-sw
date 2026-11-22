using System;
using System.Collections.Generic;
using System.IO;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Osd.Tma;

namespace Mpai.Osd.Tma.Provider;

// THE AIMs OF TELEVISION MEDIA ANALYSIS (1OSD-TMA-V1.5-I01), built for the Controller as the Module needs them: the AIMs of Mpai.Osd.Tma (visual change detection, full to basic object, speech
// scene description, scene multiplexing, event description, identity reconciliation) and the AIMs of the repository that recognise and transcribe (audio segmentation, face and speaker
// recognition, speech recognition, visual scene description).
//
// The people TMA can name are those of a gallery: the folder MPAI_TMA_GALLERY, where Access Control Registration registered them; with none, nobody is registered and TMA says who speaks
// when, not who the people are.
public sealed class TmaProvider : IAimProvider, IDisposable
{
    private static readonly HashSet<string> Aims =
    [
        "1CVE-VCD-V1.0-I01", "1CAE-AUS-V1.0-I01", "1OSD-FBO-V1.5-I01", "1PAF-FIR-V1.6-I02", "1MMC-SIR-V2.5-I02", "1MMC-ASR-V2.5-I02",
        "1OSD-IDR-V1.5-I01", "1OSD-BSS-V1.5-I01", "1OSD-BVS-V1.5-I01", "1OSD-MSM-V1.5-I01", "1OSD-MED-V1.5-I01"
    ];

    private readonly AmdStore store;
    private readonly SubjectGallery gallery;
    private Mpai.Mmc.Sir.SpeakerEmbedder? ecapa;
    private Mpai.Paf.Fir.ArcFaceRecogniser? arcFace;
    private Mpai.Osd.VisualScene.ScrfdFaceDetector? scrfd;

    public TmaProvider(AmdStore store)
    {
        this.store = store;
        var folder = Environment.GetEnvironmentVariable("MPAI_TMA_GALLERY");
        gallery = !string.IsNullOrEmpty(folder) && Directory.Exists(folder)
            ? SubjectGallery.Load(new AIF.SharedStorage.FileSharedStorage(folder, "tma", "tma"))
            : new SubjectGallery();
    }

    public bool CanCreate(string aimName) => Aims.Contains(aimName);

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings, AIF.SharedStorage.ISharedStorage? storage)
    {
        var ports = AimPortReader.Load(store, aimName);
        return aimName switch
        {
            "1CVE-VCD-V1.0-I01" => new VisualChangeDetection(aimName, settings),
            "1OSD-FBO-V1.5-I01" => new FullToBasicObject(aimName),
            "1OSD-BSS-V1.5-I01" => new BasicSpeechSceneDescription(aimName),
            "1OSD-MSM-V1.5-I01" => new MultimodalSceneMultiplexing(aimName),
            "1OSD-MED-V1.5-I01" => new MultimodalEventDescription(aimName),
            "1CAE-AUS-V1.0-I01" => new Mpai.Cae.Aus.AusAimProcessor(aimName, Ecapa(settings), ports),
            "1PAF-FIR-V1.6-I02" => new Mpai.Paf.Fir.FirEachFace(aimName, Scrfd(settings), ArcFace(settings), gallery),      // every face of every sampled frame, where and when
            "1MMC-SIR-V2.5-I02" => new Mpai.Mmc.Sir.SirAimProcessor(aimName, Ecapa(settings), gallery, ports),
            "1MMC-ASR-V2.5-I02" => new Mpai.Aims.Asr.AsrAimProcessor(aimName, Mpai.Aims.Asr.AsrFactory.Create(settings), ports),
            "1OSD-IDR-V1.5-I01" => new IdentityReconciliationEach(aimName),                 // as TMA needs it: each identity answered as it arrives
            "1OSD-BVS-V1.5-I01" => new Mpai.Osd.Bvs.BvsAimProcessor(aimName, ports),
            _ => throw new NotSupportedException($"the TMA provider does not build {aimName}.")
        };
    }

    private Mpai.Mmc.Sir.SpeakerEmbedder Ecapa(IReadOnlyDictionary<string, string> s) => ecapa ??= new(MpaiPaths.Given(s, "EcapaModel"));
    private Mpai.Paf.Fir.ArcFaceRecogniser ArcFace(IReadOnlyDictionary<string, string> s) => arcFace ??= new(MpaiPaths.Given(s, "ArcFaceModel"));
    private Mpai.Osd.VisualScene.ScrfdFaceDetector Scrfd(IReadOnlyDictionary<string, string> s) => scrfd ??= new(MpaiPaths.Given(s, "ScrfdModel"));

    public void Dispose() { ecapa?.Dispose(); arcFace?.Dispose(); scrfd?.Dispose(); }
}
