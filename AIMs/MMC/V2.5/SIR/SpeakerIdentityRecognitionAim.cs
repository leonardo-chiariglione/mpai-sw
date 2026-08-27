using System;
using System.Collections.Generic;

using Mpai.Core;

namespace Mpai.Mmc.Sir;

// ---------------------------------------------------------------------------
//  Instance Identifier (OSD-IID-V1.5) - the output of an identity AIM (SIR here,
//  FIR for faces). An ordered set of candidate labels; the first is primary.
//
//  TODO [architecture]: OSD-IID is shared identity territory for BOTH FIR and
//  SIR. This minimal type lives in the SIR namespace for now; it should be
//  extracted to a shared OSD home (with the other OSD types) and reused by FIR,
//  so both identity AIMs emit the same InstanceIdentifier type.
// ---------------------------------------------------------------------------
public sealed class InstanceIdentifier
{
    public string Header { get; init; } = "OSD-IID-V1.5";
    public string MInstanceID { get; init; } = "";
    public string? UEnvironmentID { get; init; }
    public string InstanceIdentifier_ { get; init; } = "";   // the schema's "InstanceIdentifier" id field
    public SimpleTime? InstanceTime { get; init; }
    public SpaceTime? InstanceSpaceTime { get; init; }
    public string? ObjectID { get; init; }                    // schema field is misspelt "ObjrctID"; corrected here
    public List<InstanceCandidate> InstanceIdentifierData { get; init; } = new();
    public DataExchangeMetadata? DataXMData { get; init; }
    public string? DescrMetadata { get; init; }
}

// One candidate identity: a label, a confidence, and the taxonomy it is drawn
// from. The primary identifier is the first element of InstanceIdentifierData.
public sealed class InstanceCandidate
{
    public string InstanceLabel { get; init; } = "";
    public double LabelConfidenceLevel { get; init; }
    public string Taxonomy { get; init; } = "";
}

// ---------------------------------------------------------------------------
//  Speaker Identity Recognition AIM (MMC-SIR-V2.5).
//  "Identifies a speaker based on their speech."
//
//  Input:  a SpeechObject's audio samples (16 kHz mono).
//  Output: a SpeakerID as an InstanceIdentifier (OSD-IID) - the identified
//          speaker as the primary candidate, with its confidence.
//
//  Twin of PAF-FIR: embed (ECAPA) -> match against the enrolled SpeakerDatabase
//  -> emit the identity. Identity-only: WHERE the speaker is (Spatial Attitude)
//  is the audio scene's job; ALIGNING sight and sound is OSD-AVA's; SIR only
//  says WHO. Unknown speakers (no match above threshold) yield an empty
//  InstanceIdentifierData (no candidate), the honest "not recognised".
//
//  The schema's auxiliary inputs (SpeechTime, SpeechOverlap, SpeechSceneGeometry,
//  AuxiliaryText) are refinements; AuxiliaryText in particular is a broadcast/TV
//  affordance (OSD-TMA lineage) not needed for CAV. The essential SIR is
//  SpeechObject -> SpeakerID, implemented here.
// ---------------------------------------------------------------------------
public sealed class SpeakerIdentityRecognitionAim : IDisposable
{
    private readonly SpeakerEmbedder _embedder;
    private readonly SpeakerDatabase _database;
    private readonly string _taxonomy;

    public SpeakerIdentityRecognitionAim(
        SpeakerEmbedder embedder,
        SpeakerDatabase database,
        string taxonomy = "EnrolledSpeakers")
    {
        _embedder = embedder ?? throw new ArgumentNullException(nameof(embedder));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _taxonomy = taxonomy;
    }

    // Identify the speaker of the given speech audio. Returns an OSD-IID whose
    // primary candidate is the recognised speaker, or with empty data if unknown.
    public InstanceIdentifier Identify(float[] speechSamples, string? mInstanceID = null)
    {
        var embedding = _embedder.Embed(speechSamples);
        var match = _database.Identify(embedding);

        var candidates = new List<InstanceCandidate>();
        if (match is not null)
        {
            candidates.Add(new InstanceCandidate
            {
                InstanceLabel = match.SpeakerId,
                LabelConfidenceLevel = match.Similarity,   // cosine as confidence proxy
                Taxonomy = _taxonomy
            });
        }

        return new InstanceIdentifier
        {
            MInstanceID = mInstanceID ?? "",
            InstanceIdentifier_ = Guid.NewGuid().ToString(),
            InstanceIdentifierData = candidates
        };
    }

    public void Dispose() => _embedder.Dispose();
}
