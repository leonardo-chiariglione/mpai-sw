using System;
using System.Collections.Generic;

using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Paf.Fir;

namespace Mpai.Cve.Vsi;

// ---------------------------------------------------------------------------
//  Visual Scene object Identification AIM (CVE-VSI-V1.0).
//
//  Identifies the objects of a described visual scene. Input: a BVS
//  (BasicVisualSceneDescriptors - objects + PointOfView, produced by the visual
//  describer). Output: a set of InstanceIdentifiers (OSD-IID), one per object,
//  each linked to the object it identifies via IID.ObjectID.
//
//  NON-DESTRUCTIVE: the BVS is not modified. Description (the scene) and
//  identification (these IIDs) stay separate concerns - VSI produces an identity
//  LAYER that references the described objects, exactly as OSD-AVA produces an
//  alignment layer without mutating the scenes.
//
//  DISPATCH BY OBJECT TYPE: a face object goes to FIR (PAF-FIR), yielding a
//  layered IID (person, or the coarser "face" if unrecognised). A generic object
//  would go to its own identifier - not built yet, so for such objects VSI emits
//  the coarsest honest IID ("object" at ["visual","object"]).
//
//  TODO: routing currently treats every BVS object as a face, because the visual
//  describer presently detects only faces (SCRFD). When a generic "find objects"
//  detector tags objects with a type, VSI will route on that type (face -> FIR,
//  body -> body-ID, generic -> generic-ID).
// ---------------------------------------------------------------------------
public sealed class VisualSceneObjectIdentificationAim
{
    private readonly FaceIdentityRecognitionAim _fir;
    private const string TaxonomyUri = "https://schemas.mpai.community/taxonomies/visual.json";

    public VisualSceneObjectIdentificationAim(FaceIdentityRecognitionAim fir)
        => _fir = fir ?? throw new ArgumentNullException(nameof(fir));

    // Identify every object in the BVS. Returns one InstanceIdentifier per
    // object, ObjectID-linked, non-destructive.
    public List<InstanceIdentifier> Identify(BasicVisualSceneDescriptors bvs, string? mInstanceID = null)
    {
        var result = new List<InstanceIdentifier>();
        if (bvs is null) return result;

        foreach (var entry in bvs.BasicVisualSceneDescriptorsEntries)
        {
            var obj = entry.VObjectIDOrVObject;
            if (obj is null) continue;

            string objectId = obj.BasicVisualObjectID;

            // Routing: today, treat as face -> FIR. (See TODO above.)
            byte[]? imageBytes = ExtractImageBytes(obj);
            InstanceIdentifier iid;
            if (imageBytes is not null && imageBytes.Length > 0)
            {
                iid = _fir.Identify(imageBytes, mInstanceID, objectId);
            }
            else
            {
                // No pixels available -> cannot run FIR; emit the coarsest honest
                // identification (it is a visual object) rather than nothing.
                iid = CoarseObjectIID(objectId, mInstanceID);
            }

            result.Add(iid);
        }

        return result;
    }

    // Pull the object's encoded image bytes. BasicVisualObject carries its image
    // data; the accessor name may differ on disk (Data / GetBytes / ...). Adjust
    // this one line if the field is named differently.
    private static byte[]? ExtractImageBytes(BasicVisualObject obj) => obj.Data;

    private static InstanceIdentifier CoarseObjectIID(string objectId, string? mInstanceID)
        => new InstanceIdentifier
        {
            MInstanceID = mInstanceID ?? "",
            InstanceIdentifier_ = Guid.NewGuid().ToString(),
            ObjectID = objectId,
            InstanceIdentifierData = new List<InstanceCandidate>
            {
                new InstanceCandidate
                {
                    InstanceLabel = "object",
                    LabelConfidenceLevel = 1.0,
                    Taxonomy = new InstanceTaxonomy
                    {
                        TaxonomyLevelIDs = new List<string> { "visual", "object" },
                        TaxonomyDataURI = TaxonomyUri
                    },
                    TaxonomyConfidenceLevel = 1.0
                }
            }
        };
}
