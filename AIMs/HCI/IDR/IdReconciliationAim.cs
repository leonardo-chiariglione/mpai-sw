using System;
using System.Collections.Generic;
using System.Linq;

using Mpai.Core;

namespace Mpai.Hci.Idr;

// ---------------------------------------------------------------------------
//  ID Reconciliation AIM (HCI-IDR).  "Plain C# fusion over FIR/SIR; no model;
//  the work is policy." (M3154)
//
//  Reconciles the face-identity evidence (FIR) and speaker-identity evidence
//  (SIR) for ONE person - whom AVA has associated across the two modalities -
//  into a single reconciled identity. Score-level biometric fusion:
//
//    1. Each modality gives a score VECTOR over the common subject space
//       (SubjectGallery): FIR -> (subject, faceCos)[], SIR -> (subject, voiceCos)[].
//    2. NORMALISE each vector (min-max to [0,1]) so face-cosines and
//       voice-cosines are comparable before combining - the step biometric-
//       fusion literature flags as essential (raw cosine ranges differ by
//       modality; without this one modality dominates).
//    3. FUSE per subject: weighted sum  w*face + (1-w)*voice  over the UNION of
//       subjects. A subject scored by only one modality keeps that modality's
//       (weighted) score - so a missing modality degrades gracefully rather than
//       zeroing the subject. (Weighted sum, not product: it survives one absent
//       or near-zero modality, which a real system needs. Product/naive-Bayes is
//       available as an alternative rule.)
//    4. Emit a reconciled InstanceIdentifier (OSD-IID) whose InstanceIdentifierData
//       is the fused subjects RANKED by combined score (primary = top), each a
//       candidate at the person layer. This is the "list with decreasing
//       accuracy" the IID is designed to carry.
//
//  Confidence of the reconciled primary reflects agreement: two modalities
//  agreeing on a subject sum to a high combined score; disagreement spreads the
//  mass and lowers the top - honest uncertainty, no arbitration by fiat.
// ---------------------------------------------------------------------------
public sealed class IdReconciliationAim
{
    private readonly double _faceWeight;   // w in [0,1]; voice weight = 1-w
    private const string TaxonomyUri = "https://schemas.mpai.community/taxonomies/person.json";

    public IdReconciliationAim(double faceWeight = 0.5)
    {
        if (faceWeight < 0 || faceWeight > 1) throw new ArgumentOutOfRangeException(nameof(faceWeight));
        _faceWeight = faceWeight;
    }

    public enum Rule { WeightedSum, Product }

    // Reconcile one person's face + voice evidence into a single OSD-IID.
    // Either vector may be empty (that modality absent / no match).
    public InstanceIdentifier Reconcile(
        IReadOnlyList<SubjectScore> faceScores,
        IReadOnlyList<SubjectScore> voiceScores,
        Rule rule = Rule.WeightedSum,
        string? objectId = null,
        string? mInstanceID = null)
    {
        var face = Normalise(faceScores);
        var voice = Normalise(voiceScores);

        var subjects = new HashSet<string>(face.Keys);
        subjects.UnionWith(voice.Keys);

        var fused = new List<(string subject, double score)>();
        foreach (var subj in subjects)
        {
            bool hasF = face.TryGetValue(subj, out double f);
            bool hasV = voice.TryGetValue(subj, out double v);

            double combined = rule switch
            {
                // Product (naive-Bayes independence): only when BOTH present.
                Rule.Product when hasF && hasV => f * v,
                Rule.Product => hasF ? f : v,   // one modality -> pass through
                // Weighted sum over whichever modalities are present.
                _ => (hasF ? _faceWeight * f : 0) + (hasV ? (1 - _faceWeight) * v : 0)
            };
            fused.Add((subj, combined));
        }

        var ranked = fused.OrderByDescending(x => x.score).ToList();

        var candidates = ranked.Select(x => new InstanceCandidate
        {
            InstanceLabel = x.subject,
            LabelConfidenceLevel = Math.Clamp(x.score, 0.0, 1.0),
            Taxonomy = new InstanceTaxonomy
            {
                TaxonomyLevelIDs = new List<string> { "person" },
                TaxonomyDataURI = TaxonomyUri
            },
            TaxonomyConfidenceLevel = 1.0
        }).ToList();

        // Nothing to reconcile (both modalities empty) -> a coarse "person" IID.
        if (candidates.Count == 0)
        {
            candidates.Add(new InstanceCandidate
            {
                InstanceLabel = "person",
                LabelConfidenceLevel = 1.0,
                Taxonomy = new InstanceTaxonomy
                {
                    TaxonomyLevelIDs = new List<string> { "person" },
                    TaxonomyDataURI = TaxonomyUri
                },
                TaxonomyConfidenceLevel = 1.0
            });
        }

        return new InstanceIdentifier
        {
            MInstanceID = mInstanceID ?? "",
            InstanceIdentifier_ = Guid.NewGuid().ToString(),
            ObjectID = objectId,
            InstanceIdentifierData = candidates
        };
    }

    // Min-max normalise a score vector into [0,1]. If all equal (or single), map
    // to 1.0 (present with full within-modality confidence). Empty -> empty.
    private static Dictionary<string, double> Normalise(IReadOnlyList<SubjectScore> scores)
    {
        var map = new Dictionary<string, double>();
        if (scores.Count == 0) return map;
        float min = scores.Min(s => s.Score), max = scores.Max(s => s.Score);
        double range = max - min;
        foreach (var s in scores)
            map[s.SubjectId] = range > 1e-9 ? (s.Score - min) / range : 1.0;
        return map;
    }
}
