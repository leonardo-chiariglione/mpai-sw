using System;
using System.Collections.Generic;

using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Mmc.Sir;

namespace Mpai.Cae.Asi;

// ---------------------------------------------------------------------------
//  Audio Scene object Identification AIM (CAE-ASI-V2.5).
//
//  The audio twin of CVE-VSI. Identifies the objects of a described audio scene.
//  Input: a BAS (BasicAudioSceneDescriptors - sound objects + PointOfView/DOA,
//  from the audio describer). Output: a set of InstanceIdentifiers (OSD-IID),
//  one per object, ObjectID-linked, NON-DESTRUCTIVE (the BAS is not modified).
//
//  DISPATCH BY SOUND TYPE (via YAMNet classification, since - unlike vision,
//  where detection already says "face" - audio arrives untyped and must be
//  classified before routing):
//    speech-family  -> dispatch to SIR (MMC-SIR) -> layered IID at the speaker
//                      layer (or the coarser speech layer if the speaker is
//                      unknown).
//    other sound    -> a sound-class IID at ["sound", <yamnet-label>], e.g.
//                      ["sound","Siren"], ["sound","Vehicle horn"] - the
//                      CAV-safety-critical non-speech sounds.
//
//  Model-light router, exactly like VSI: the identity/classification models live
//  in the pieces it calls (YAMNet for sound class, SIR/ECAPA for the speaker).
// ---------------------------------------------------------------------------
public sealed class AudioSceneObjectIdentificationAim
{
    private readonly SoundClassifier _classifier;
    private readonly SpeakerIdentityRecognitionAim? _sir;   // optional: speaker layer
    private const string TaxonomyUri = "https://schemas.mpai.community/taxonomies/sound.json";

    public AudioSceneObjectIdentificationAim(SoundClassifier classifier, SpeakerIdentityRecognitionAim? sir = null)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _sir = sir;
    }

    // Identify each audio object in the BAS by its samples. The caller supplies a
    // way to get an object's mono-16k samples (the BasicAudioObject carries bytes;
    // decoding to samples is the acquisition/format concern, so it is injected).
    public List<InstanceIdentifier> Identify(
        BasicAudioSceneDescriptors bas,
        Func<BasicAudioObject, float[]?> getSamples,
        string? mInstanceID = null)
    {
        var result = new List<InstanceIdentifier>();
        if (bas is null) return result;

        foreach (var entry in bas.BasicAudioSceneDescriptorsEntries)
        {
            var obj = entry.AudioObjectIDOrAudioObject;
            if (obj is null) continue;
            string objectId = obj.BasicAudioObjectID;

            float[]? samples = getSamples(obj);
            if (samples is null || samples.Length == 0)
            {
                result.Add(CoarseSoundIID(objectId, mInstanceID));
                continue;
            }

            var top = _classifier.Classify(samples);
            var best = top.Count > 0 ? top[0] : null;

            if (best is not null && best.IsSpeech && _sir is not null)
            {
                // Speech -> SIR provides the speaker (or coarser speech) layer.
                var iid = _sir.Identify(samples, mInstanceID);
                // Attach the object link (SIR does not know the scene object id).
                result.Add(WithObjectId(iid, objectId));
            }
            else if (best is not null && best.IsSpeech)
            {
                // Speech but no SIR wired: identify at the speech layer.
                result.Add(SoundClassIID(objectId, mInstanceID, "speech", new[] { "sound", "speech" }, best.Score));
            }
            else if (best is not null)
            {
                // Non-speech: a sound-class IID at ["sound", <label>].
                result.Add(SoundClassIID(objectId, mInstanceID, best.Label, new[] { "sound", best.Label }, best.Score));
            }
            else
            {
                result.Add(CoarseSoundIID(objectId, mInstanceID));
            }
        }

        return result;
    }

    private static InstanceIdentifier WithObjectId(InstanceIdentifier iid, string objectId)
        => new InstanceIdentifier
        {
            Header = iid.Header,
            MInstanceID = iid.MInstanceID,
            UEnvironmentID = iid.UEnvironmentID,
            InstanceIdentifier_ = iid.InstanceIdentifier_,
            InstanceTime = iid.InstanceTime,
            InstanceSpaceTime = iid.InstanceSpaceTime,
            ObjectID = objectId,
            InstanceIdentifierData = iid.InstanceIdentifierData,
            DataXMData = iid.DataXMData,
            DescrMetadata = iid.DescrMetadata
        };

    private static InstanceIdentifier SoundClassIID(string objectId, string? mInstanceID, string label, string[] path, double conf)
        => new InstanceIdentifier
        {
            MInstanceID = mInstanceID ?? "",
            InstanceIdentifier_ = Guid.NewGuid().ToString(),
            ObjectID = objectId,
            InstanceIdentifierData = new List<InstanceCandidate>
            {
                new InstanceCandidate
                {
                    InstanceLabel = label,
                    LabelConfidenceLevel = conf,
                    Taxonomy = new InstanceTaxonomy { TaxonomyLevelIDs = new List<string>(path), TaxonomyDataURI = TaxonomyUri },
                    TaxonomyConfidenceLevel = 1.0
                }
            }
        };

    private static InstanceIdentifier CoarseSoundIID(string objectId, string? mInstanceID)
        => SoundClassIID(objectId, mInstanceID, "sound", new[] { "sound" }, 1.0);
}
