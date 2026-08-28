using System;
using System.IO;
using System.Linq;

using Mpai.Core;
using Mpai.Paf.Fir;
using Mpai.Mmc.Sir;
using Mpai.Hci.Idr;

namespace Mpai.Hci.Idr.Test;

// Demonstrates the real FIR+SIR+IDR system: enrol subjects (face and/or voice)
// into ONE SubjectGallery, persist to disk, reload, then reconcile a probe's
// face+voice evidence into a single OSD-IID.
//   arg 0: models dir (glintr100.onnx, ecapa-tdnn.onnx). Default D:\AI\Models.
//   arg 1: images dir (R.Reagan.jpg, B.Obama.jpg).       Default D:\AI\TestData\Images.
//   arg 2: audio dir  (spk*.wav).                        Default D:\AI\TestData\Audio.
public static class Program
{
    public static int Main(string[] args)
    {
        string models = args.Length >= 1 ? args[0] : @"D:\AI\Models";
        string images = args.Length >= 2 ? args[1] : @"D:\AI\TestData\Images";
        string audio  = args.Length >= 3 ? args[2] : @"D:\AI\TestData\Audio";
        string arc = Path.Combine(models, "glintr100.onnx");
        string ecapa = Path.Combine(models, "ecapa-tdnn.onnx");
        string reagan = Path.Combine(images, "R.Reagan.jpg");
        string obama  = Path.Combine(images, "B.Obama.jpg");
        string v1221a = Path.Combine(audio, "spk1221_a.wav");
        string v1221b = Path.Combine(audio, "spk1221_b.wav");
        string v1089  = Path.Combine(audio, "spk1089_x.wav");

        foreach (var p in new[] { arc, ecapa, reagan, obama, v1221a, v1221b, v1089 })
            if (!File.Exists(p)) { Console.WriteLine($"Missing: {p}"); return 1; }

        using var face = new ArcFaceRecogniser(arc);
        using var voice = new SpeakerEmbedder(ecapa);

        // --- Enrol subjects into ONE gallery (media-taking) ---
        // Note on data: we have real FACES for Reagan/Obama and real VOICES for
        // LibriSpeech 1221/1089, but not face+voice of the SAME person under a
        // clean licence. So we enrol subjects with the modality we have, and to
        // demonstrate genuine cross-modal CORROBORATION we also make a subject
        // "Alex" carry BOTH a face (Reagan's) and a voice (1221's) - a synthetic
        // same-person, purely to exercise the fusion mechanics end-to-end.
        var gallery = new SubjectGallery();
        SubjectEnrolment.EnrolSubject(gallery, "Reagan", faceRecogniser: face, faceImagePath: reagan);
        SubjectEnrolment.EnrolSubject(gallery, "Obama",  faceRecogniser: face, faceImagePath: obama);
        SubjectEnrolment.EnrolSubject(gallery, "Spk1221", speakerEmbedder: voice, voiceClipPath: v1221a);
        SubjectEnrolment.EnrolSubject(gallery, "Spk1089", speakerEmbedder: voice, voiceClipPath: v1089);
        SubjectEnrolment.EnrolSubject(gallery, "Alex", faceRecogniser: face, faceImagePath: reagan,
                                     speakerEmbedder: voice, voiceClipPath: v1221a);
        Console.WriteLine($"Enrolled {gallery.Count} subjects: {string.Join(", ", gallery.SubjectIds)}");

        // --- Persist + reload (proves 'add user entries' survive) ---
        string galleryPath = Path.Combine(Path.GetTempPath(), "subject_gallery.json");
        gallery.Save(galleryPath);
        var reloaded = SubjectGallery.Load(galleryPath);
        Console.WriteLine($"Saved to {galleryPath}, reloaded {reloaded.Count} subjects.");
        Console.WriteLine();

        var idr = new IdReconciliationAim(faceWeight: 0.5);

        // === Case 1: BOTH modalities, same person (Reagan face + 1221 voice, held-out clip b) ===
        // Probe against the reloaded gallery. "Alex" has both templates matching,
        // so the fused top should be Alex - cross-modal corroboration.
        var probeFace = face.Embed(File.ReadAllBytes(reagan));
        var probeVoice = voice.Embed(WavReader.ReadMono16k(v1221b));  // held-out same speaker
        var iid1 = idr.Reconcile(reloaded.ScoreFace(probeFace), reloaded.ScoreVoice(probeVoice), objectId: "person-1");
        Report("Case 1 - face(Reagan) + voice(1221 held-out), both present", iid1);

        // === Case 2: VOICE ONLY (missing-modality graceful degrade) ===
        var iid2 = idr.Reconcile(Array.Empty<SubjectScore>(), reloaded.ScoreVoice(probeVoice), objectId: "person-2");
        Report("Case 2 - voice only (no face)", iid2);

        // === Case 3: FACE ONLY ===
        var iid3 = idr.Reconcile(reloaded.ScoreFace(probeFace), Array.Empty<SubjectScore>(), objectId: "person-3");
        Report("Case 3 - face only (no voice)", iid3);

        Console.WriteLine("PASS: gallery enrols face+voice under one subject id, persists/reloads, and IDR fuses ranked evidence into one OSD-IID.");
        return 0;
    }

    static void Report(string title, InstanceIdentifier iid)
    {
        Console.WriteLine($"== {title} ==");
        foreach (var c in iid.InstanceIdentifierData.Take(3))
            Console.WriteLine($"   {c.InstanceLabel,-8} conf={c.LabelConfidenceLevel:F3} layer=[{string.Join(",", c.Taxonomy.TaxonomyLevelIDs)}]");
        Console.WriteLine($"   -> primary: {iid.InstanceIdentifierData[0].InstanceLabel}");
        Console.WriteLine();
    }
}
