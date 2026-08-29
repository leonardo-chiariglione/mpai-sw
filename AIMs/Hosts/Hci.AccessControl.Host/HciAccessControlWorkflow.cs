using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Gallery;

namespace Hci.AccessControl.Host;

// The HCI "check authorised users" choreography, path A: describe -> match ->
// fuse. It mirrors enrolment (same EFD/ESD description through the Controller)
// and then, instead of storing, matches each probe descriptor against the
// gallery and reconciles the two identities through HCI-IDR.
//
//   capture face  -> UAG-EFD -> FDO -> MatchFace(gallery)   -> face identity (OSD-IID)
//   capture voice  -> UAG-ESD -> SDO -> MatchSpeech(gallery) -> speaker identity (OSD-IID)
//   both identities -> UAG-IDR -> reconciled identity -> GRANT / DENY
//
// Everything runs through the public MPAI_AIFU_* API; the UA captures at the
// boundary and the Controller routes to each AIM. The gallery is the same
// Global Storage Repository enrolment wrote, so recognition reads exactly what
// enrolment stored - standard MPAI descriptor objects.
public sealed class HciAccessControlWorkflow
{
    private const string EfdAiw = "UAG-EFD-V1.0";
    private const string EsdAiw = "UAG-ESD-V1.0";
    private const string IdrAiw = "UAG-IDR-V1.0";

    // Match thresholds: cosine similarity above which a probe is that subject.
    // Faces (ArcFace) and voices (ECAPA) sit on different scales; these mirror
    // the values the recognition path uses.
    private const float FaceThreshold   = 0.35f;
    private const float SpeakerThreshold = 0.45f;

    private readonly UserAgent         _ua;
    private readonly IAimProvider      _provider;
    private readonly AimSettings       _settings;
    private readonly SubjectRepository _gallery;

    public HciAccessControlWorkflow(
        UserAgent ua, IAimProvider provider, AimSettings settings, SubjectRepository gallery)
    {
        _ua       = ua;
        _provider = provider;
        _settings = settings;
        _gallery  = gallery;
    }

    public sealed record Decision(bool Granted, string? SubjectId, string Reason);

    // The whole check: describe both modalities, match each against the gallery,
    // reconcile, and decide. A subject is granted when the reconciled identity
    // names an enrolled subject.
    public Decision CheckAuthorised(string imagePath, string wavPath)
    {
        // Face: describe -> match -> identity.
        var fdo = DescribeFace(imagePath);
        var faceId = fdo?.Embedding() is { } fe ? _gallery.MatchFace(fe, FaceThreshold) : null;

        // Voice: describe -> match -> identity.
        var sdo = DescribeSpeech(wavPath);
        var speakerId = sdo?.Embedding() is { } se ? _gallery.MatchSpeech(se, SpeakerThreshold) : null;

        if (faceId is null && speakerId is null)
            return new Decision(false, null, "neither face nor voice matched an enrolled subject");

        // Reconcile the two identities through IDR (through the Controller).
        var reconciled = Reconcile(
            faceId is { } f ? FaceIdentity(f.SubjectId, f.Similarity) : null,
            speakerId is { } s ? SpeakerIdentity(s.SubjectId, s.Similarity) : null);

        if (reconciled is null || reconciled.InstanceIdentifierData.Count == 0)
            return new Decision(false, null, "reconciliation produced no identity");

        var primary = reconciled.InstanceIdentifierData[0];
        bool granted = primary.LabelConfidenceLevel > 0 && !string.IsNullOrWhiteSpace(primary.InstanceLabel)
                       && primary.InstanceLabel is not "face" and not "speaker";

        return new Decision(granted, granted ? primary.InstanceLabel : null,
            granted ? $"reconciled identity: {primary.InstanceLabel}" : "no confident identity after reconciliation");
    }

    // ---- description (shared with enrolment's pattern) --------------------

    public FaceDescriptorsObject? DescribeFace(string imagePath)
    {
        if (!File.Exists(imagePath)) throw new FileNotFoundException($"Image not found: {imagePath}");
        _ua.MPAI_AIFU_Controller_Initialize();
        if (_ua.MPAI_AIFU_AIW_Start(EfdAiw, _provider, _settings, out var aiwId) != AifError.OK)
        { Console.WriteLine($"  could not start {EfdAiw}."); return null; }
        try
        {
            var bvo = BasicVisualObject.FromFile(Path.GetFileName(imagePath), File.ReadAllBytes(imagePath));
            var boundary = new Dictionary<string, string> { ["InputVisual"] = MpaiJson.ToJson(bvo) };
            var completed = Run(aiwId, boundary);
            if (completed is null) return null;
            string? json = completed.Ports.TryGetValue("FaceDescriptors", out var j) ? j : completed.Ports.Values.FirstOrDefault();
            return string.IsNullOrWhiteSpace(json) ? null : MpaiJson.FromJson<FaceDescriptorsObject>(json);
        }
        finally { _ua.MPAI_AIFU_AIW_Stop(aiwId); }
    }

    public SpeechDescriptorsObject? DescribeSpeech(string wavPath)
    {
        if (!File.Exists(wavPath)) throw new FileNotFoundException($"Speech not found: {wavPath}");
        _ua.MPAI_AIFU_Controller_Initialize();
        if (_ua.MPAI_AIFU_AIW_Start(EsdAiw, _provider, _settings, out var aiwId) != AifError.OK)
        { Console.WriteLine($"  could not start {EsdAiw}."); return null; }
        try
        {
            var bso = BasicSpeechObject.FromData(File.ReadAllBytes(wavPath), null);
            var boundary = new Dictionary<string, string> { ["InputSpeech"] = MpaiJson.ToJson(bso) };
            var completed = Run(aiwId, boundary);
            if (completed is null) return null;
            string? json = completed.Ports.TryGetValue("SpeechDescriptors", out var j) ? j : completed.Ports.Values.FirstOrDefault();
            return string.IsNullOrWhiteSpace(json) ? null : MpaiJson.FromJson<SpeechDescriptorsObject>(json);
        }
        finally { _ua.MPAI_AIFU_AIW_Stop(aiwId); }
    }

    // ---- reconciliation through IDR ---------------------------------------

    private InstanceIdentifier? Reconcile(InstanceIdentifier? faceId, InstanceIdentifier? speakerId)
    {
        _ua.MPAI_AIFU_Controller_Initialize();
        if (_ua.MPAI_AIFU_AIW_Start(IdrAiw, _provider, _settings, out var aiwId) != AifError.OK)
        { Console.WriteLine($"  could not start {IdrAiw}."); return null; }
        try
        {
            var boundary = new Dictionary<string, string>();
            if (faceId is not null)    boundary["InputFaceID"]    = MpaiJson.ToJson(faceId);
            if (speakerId is not null) boundary["InputSpeakerID"] = MpaiJson.ToJson(speakerId);

            var completed = Run(aiwId, boundary);
            if (completed is null) return null;
            string? json = completed.Ports.TryGetValue("ReconciledID", out var j) ? j : completed.Ports.Values.FirstOrDefault();
            return string.IsNullOrWhiteSpace(json) ? null : MpaiJson.FromJson<InstanceIdentifier>(json);
        }
        finally { _ua.MPAI_AIFU_AIW_Stop(aiwId); }
    }

    // ---- OSD-IID construction from a gallery match ------------------------
    // Mirrors how FIR/SIR shape their identity output: a single candidate whose
    // label is the subject and whose confidence is the match similarity, situated
    // at the person/speaker taxonomy layer.

    private static InstanceIdentifier FaceIdentity(string subjectId, float similarity) => new()
    {
        InstanceIdentifier_ = subjectId,
        InstanceIdentifierData = new List<InstanceCandidate>
        {
            new() { InstanceLabel = subjectId, LabelConfidenceLevel = similarity,
                    Taxonomy = new InstanceTaxonomy { TaxonomyLevelIDs = new() { "visual", "face", "person" } } }
        }
    };

    private static InstanceIdentifier SpeakerIdentity(string subjectId, float similarity) => new()
    {
        InstanceIdentifier_ = subjectId,
        InstanceIdentifierData = new List<InstanceCandidate>
        {
            new() { InstanceLabel = subjectId, LabelConfidenceLevel = similarity,
                    Taxonomy = new InstanceTaxonomy { TaxonomyLevelIDs = new() { "sound", "speech", "speaker" } } }
        }
    };

    // ---- run helper -------------------------------------------------------

    private AIF.Controller.Message? Run(int aiwId, Dictionary<string, string> boundary)
    {
        var (error, outcome) = _ua.RunAsync(aiwId, boundary).GetAwaiter().GetResult();
        if (error != AifError.OK || outcome is null) { Console.WriteLine($"  run failed: {error}"); return null; }
        if (outcome.Suspended) { Console.WriteLine($"  unexpectedly suspended on '{outcome.WaitingPort}'."); return null; }
        if (outcome.Completed is null) { Console.WriteLine("  no completed message."); return null; }
        if (outcome.Completed.IsError) { Console.WriteLine($"  {outcome.Completed.FailedAim}: {outcome.Completed.Payload}"); return null; }
        return outcome.Completed;
    }
}
