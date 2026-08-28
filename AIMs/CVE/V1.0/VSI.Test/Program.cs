using System;
using System.IO;
using System.Collections.Generic;

using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Paf.Fir;
using Mpai.Cve.Vsi;

namespace Mpai.Cve.Vsi.Test;

// Verifies VSI over a BVS: build a BVS whose objects carry real face images,
// enrol one person in FIR, run VSI, and check each object gets an ObjectID-linked
// InstanceIdentifier at the right layer (person for the enrolled face, "face" for
// the stranger).
//   Uses the same images FIR was verified on: R.Reagan.jpg (enrolled) and
//   B.Obama.jpg (stranger). Adjust paths/model dir via args if needed.
public static class Program
{
    public static int Main(string[] args)
    {
        string models = args.Length >= 1 ? args[0] : @"D:\AI\Models";
        string images = args.Length >= 2 ? args[1] : @"D:\AI\AIMs\PAF\V1.6\FIR.RecogTest";
        string modelPath = Path.Combine(models, "glintr100.onnx");

        // Fall back: look for the face images wherever the FIR recog test kept them.
        string reagan = FindFirst(new[] {
            Path.Combine(images, "R.Reagan.jpg"),
            @"D:\AI\Models\R.Reagan.jpg" });
        string obama = FindFirst(new[] {
            Path.Combine(images, "B.Obama.jpg"),
            @"D:\AI\Models\B.Obama.jpg" });

        if (modelPath is null || !File.Exists(modelPath)) { Console.WriteLine($"Missing model: {modelPath}"); return 1; }
        if (reagan is null || obama is null) { Console.WriteLine("Missing R.Reagan.jpg / B.Obama.jpg - pass their folder as arg 2."); return 1; }

        // FIR: enrol Reagan only.
        using var recogniser = new ArcFaceRecogniser(modelPath);
        var gallery = new SubjectGallery(faceThreshold: 0.35f);
        gallery.EnrolEmbeddings("Reagan", face: recogniser.Embed(File.ReadAllBytes(reagan)));
        using var fir = new FaceIdentityRecognitionAim(recogniser, gallery);

        // Build a BVS with two visual objects carrying the two face images.
        var bvs = new BasicVisualSceneDescriptors
        {
            BasicVisualSceneDescriptorsID = "bvs-vsi",
            VisualObjectCount = 2,
            BasicVisualSceneDescriptorsEntries = new List<BasicVisualSceneEntry>
            {
                new() { VObjectIDOrVObject = MakeObj("obj-reagan", File.ReadAllBytes(reagan)), PointOfView = new PointOfView { PointOfViewID = "p0" } },
                new() { VObjectIDOrVObject = MakeObj("obj-obama",  File.ReadAllBytes(obama)),  PointOfView = new PointOfView { PointOfViewID = "p1" } }
            }
        };

        // Run VSI.
        var vsi = new VisualSceneObjectIdentificationAim(fir);
        var iids = vsi.Identify(bvs, mInstanceID: "M-1");

        Console.WriteLine($"== VSI over BVS ({iids.Count} identities) ==");
        foreach (var iid in iids)
        {
            var p = iid.InstanceIdentifierData[0];
            Console.WriteLine($"  object {iid.ObjectID}: label='{p.InstanceLabel}' conf={p.LabelConfidenceLevel:F3} layer=[{string.Join(",", p.Taxonomy.TaxonomyLevelIDs)}]");
        }
        Console.WriteLine();

        // Expect: obj-reagan -> "Reagan" at person layer; obj-obama -> "face" (stranger).
        var byId = new Dictionary<string, InstanceCandidate>();
        foreach (var iid in iids) byId[iid.ObjectID!] = iid.InstanceIdentifierData[0];

        bool ok = iids.Count == 2
               && byId.ContainsKey("obj-reagan") && byId["obj-reagan"].InstanceLabel == "Reagan"
               && byId["obj-reagan"].Taxonomy.TaxonomyLevelIDs.Count == 3
               && byId.ContainsKey("obj-obama") && byId["obj-obama"].InstanceLabel == "face"
               && byId["obj-obama"].Taxonomy.TaxonomyLevelIDs.Count == 2;
        Console.WriteLine(ok
            ? "PASS: enrolled face identified as person; stranger falls back to 'face' layer; both ObjectID-linked."
            : "FAIL: VSI identification not as expected.");
        return ok ? 0 : 1;
    }

    static BasicVisualObject MakeObj(string id, byte[] data)
        => new BasicVisualObject { BasicVisualObjectID = id, Data = data };

    static string? FindFirst(string[] paths) { foreach (var p in paths) if (File.Exists(p)) return p; return null; }
}
