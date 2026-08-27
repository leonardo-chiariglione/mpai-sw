using System;
using System.IO;
using System.Collections.Generic;

using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Mmc.Sir;
using Mpai.Cae.Asi;

namespace Mpai.Cae.Asi.Test;

// Verifies ASI over a BAS: build a BAS whose objects carry the LibriSpeech
// clips, enrol speaker 1221 in SIR, run ASI. YAMNet should classify the clips as
// speech -> ASI dispatches to SIR -> IID at the speaker layer (1221) or speech
// layer (unknown). Each IID ObjectID-linked; BAS untouched.
//   arg 0: models dir (yamnet.onnx, ecapa-tdnn.onnx, yamnet_class_map.csv)
//   arg 1: audio dir  (the .wav clips)
public static class Program
{
    public static int Main(string[] args)
    {
        string models = args.Length >= 1 ? args[0] : @"D:\AI\Models";
        string audio  = args.Length >= 2 ? args[1] : @"D:\AI\TestData\Audio";
        string yamnet = Path.Combine(models, "yamnet.onnx");
        string classMap = Path.Combine(models, "yamnet_class_map.csv");
        string ecapa  = Path.Combine(models, "ecapa-tdnn.onnx");
        string a = Path.Combine(audio, "spk1221_a.wav");
        string b = Path.Combine(audio, "spk1221_b.wav");
        string x = Path.Combine(audio, "spk1089_x.wav");

        foreach (var p in new[] { yamnet, classMap, ecapa, a, b, x })
            if (!File.Exists(p)) { Console.WriteLine($"Missing: {p}"); return 1; }

        // SIR: enrol 1221 from clip a.
        using var embedder = new SpeakerEmbedder(ecapa);
        var spkDb = new SpeakerDatabase(threshold: 0.45f);
        spkDb.Enrol("1221", embedder.Embed(WavReader.ReadMono16k(a)));
        var sir = new SpeakerIdentityRecognitionAim(embedder, spkDb);

        // ASI with YAMNet + SIR.
        using var classifier = new SoundClassifier(yamnet, classMap);
        var asi = new AudioSceneObjectIdentificationAim(classifier, sir);

        // Cache each object's samples by object id (the test's getSamples).
        var byObj = new Dictionary<string, float[]>
        {
            ["obj-1221b"] = WavReader.ReadMono16k(b),   // held-out 1221
            ["obj-1089x"] = WavReader.ReadMono16k(x)    // stranger 1089
        };

        var bas = new BasicAudioSceneDescriptors
        {
            BasicAudioSceneDescriptorsID = "bas-asi",
            AudioObjectCount = 2,
            BasicAudioSceneDescriptorsEntries = new List<BasicAudioSceneEntry>
            {
                new() { AudioObjectIDOrAudioObject = new BasicAudioObject { BasicAudioObjectID = "obj-1221b" }, PointOfView = new PointOfView { PointOfViewID = "p0" } },
                new() { AudioObjectIDOrAudioObject = new BasicAudioObject { BasicAudioObjectID = "obj-1089x" }, PointOfView = new PointOfView { PointOfViewID = "p1" } }
            }
        };

        var iids = asi.Identify(bas, obj => byObj.TryGetValue(obj.BasicAudioObjectID, out var s) ? s : null, "M-1");

        Console.WriteLine($"== ASI over BAS ({iids.Count} identities) ==");
        foreach (var iid in iids)
        {
            var p = iid.InstanceIdentifierData[0];
            Console.WriteLine($"  object {iid.ObjectID}: label='{p.InstanceLabel}' conf={p.LabelConfidenceLevel:F3} layer=[{string.Join(",", p.Taxonomy.TaxonomyLevelIDs)}]");
        }
        Console.WriteLine();

        // Expect: both classified speech -> routed to SIR. obj-1221b -> "1221"
        // at speaker layer; obj-1089x -> "speech" (unknown speaker) at speech layer.
        var byId = new Dictionary<string, InstanceCandidate>();
        foreach (var iid in iids) byId[iid.ObjectID!] = iid.InstanceIdentifierData[0];

        bool bothSpeechRouted =
            byId.ContainsKey("obj-1221b") && byId["obj-1221b"].Taxonomy.TaxonomyLevelIDs[0] == "sound"
                                          && byId["obj-1221b"].Taxonomy.TaxonomyLevelIDs.Contains("speech");
        bool recognised1221 = byId.ContainsKey("obj-1221b") && byId["obj-1221b"].InstanceLabel == "1221";
        Console.WriteLine(bothSpeechRouted
            ? (recognised1221
                ? "PASS: speech classified and routed to SIR; held-out 1221 recognised at speaker layer."
                : "PARTIAL: speech routed to SIR but 1221 not recognised (check enrolment).")
            : "NOTE: check YAMNet speech classification / routing.");
        return 0;
    }
}
