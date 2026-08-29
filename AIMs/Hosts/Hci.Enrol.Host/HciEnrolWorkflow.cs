using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Gallery;

namespace Hci.Enrol.Host;

// The HCI enrolment choreography, driven by the User Agent through the public
// MPAI_AIFU_* API only - never invoking an AIM. The UA captures a face and a
// voice at the boundary and hands each to the Controller, which routes it to the
// description AIM (PAF-EFD / MMC-ESD) by Data Type; the resulting standard
// Descriptors Object comes back on the boundary and is stored in the gallery
// (AIF Global Storage) as a serialized standard MPAI type.
//
// Modelled exactly on HciAccessWorkflow: same Run / Completed helpers, same
// "capture at the boundary, route through the Controller" discipline. Where the
// access host routes to a recognition AIM and reads an identity, enrolment routes
// to a description AIM and stores a descriptor.
public sealed class HciEnrolWorkflow
{
    private const string EfdAiw = "UAG-EFD-V1.0";   // wraps PAF-EFD (one SubAIM, no code)
    private const string EsdAiw = "UAG-ESD-V1.0";   // wraps MMC-ESD (one SubAIM, no code)

    private readonly UserAgent        _ua;
    private readonly IAimProvider     _provider;
    private readonly AimSettings      _settings;
    private readonly SubjectRepository _gallery;

    public HciEnrolWorkflow(
        UserAgent ua, IAimProvider provider, AimSettings settings, SubjectRepository gallery)
    {
        _ua       = ua;
        _provider = provider;
        _settings = settings;
        _gallery  = gallery;
    }

    // Enrol a subject: describe the face and the voice through the Controller,
    // then store both Descriptors Objects in the gallery under the subject id.
    // Returns true if both descriptors were produced and stored.
    public bool Enrol(string subjectId, string imagePath, string wavPath)
    {
        var fdo = DescribeFace(imagePath);
        if (fdo is null) { Console.WriteLine("  face description failed - not enrolled."); return false; }

        var sdo = DescribeSpeech(wavPath);
        if (sdo is null) { Console.WriteLine("  speech description failed - not enrolled."); return false; }

        _gallery.EnrolFace(subjectId, fdo);
        _gallery.EnrolSpeech(subjectId, sdo);
        Console.WriteLine($"  stored face + speech descriptors for '{subjectId}'.");
        return true;
    }

    // Describe a face: UA wraps the captured image as a Basic Visual Object at the
    // boundary; the Controller routes it to EFD; the Face Descriptors Object comes
    // back on the FaceDescriptors boundary port.
    public FaceDescriptorsObject? DescribeFace(string imagePath)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"Image not found: {imagePath}");

        _ua.MPAI_AIFU_Controller_Initialize();
        if (_ua.MPAI_AIFU_AIW_Start(EfdAiw, _provider, _settings, out var aiwId) != AifError.OK)
        {
            Console.WriteLine($"  could not start {EfdAiw}.");
            return null;
        }

        try
        {
            var bvo = BasicVisualObject.FromFile(Path.GetFileName(imagePath), File.ReadAllBytes(imagePath));
            var boundary = new Dictionary<string, string> { ["InputVisual"] = MpaiJson.ToJson(bvo) };

            var completed = Run(_ua, aiwId, boundary);
            if (completed is null) return null;

            string? json = completed.Ports.TryGetValue("FaceDescriptors", out var j) ? j
                         : completed.Ports.Values.FirstOrDefault();
            return string.IsNullOrWhiteSpace(json) ? null : MpaiJson.FromJson<FaceDescriptorsObject>(json);
        }
        finally { _ua.MPAI_AIFU_AIW_Stop(aiwId); }
    }

    // Describe a voice: UA wraps the captured wav as a Basic Speech Object at the
    // boundary; the Controller routes it to ESD; the Speech Descriptors Object
    // comes back on the SpeechDescriptors boundary port.
    public SpeechDescriptorsObject? DescribeSpeech(string wavPath)
    {
        if (!File.Exists(wavPath))
            throw new FileNotFoundException($"Speech not found: {wavPath}");

        _ua.MPAI_AIFU_Controller_Initialize();
        if (_ua.MPAI_AIFU_AIW_Start(EsdAiw, _provider, _settings, out var aiwId) != AifError.OK)
        {
            Console.WriteLine($"  could not start {EsdAiw}.");
            return null;
        }

        try
        {
            var bso = BasicSpeechObject.FromData(File.ReadAllBytes(wavPath), null);
            var boundary = new Dictionary<string, string> { ["InputSpeech"] = MpaiJson.ToJson(bso) };

            var completed = Run(_ua, aiwId, boundary);
            if (completed is null) return null;

            string? json = completed.Ports.TryGetValue("SpeechDescriptors", out var j) ? j
                         : completed.Ports.Values.FirstOrDefault();
            return string.IsNullOrWhiteSpace(json) ? null : MpaiJson.FromJson<SpeechDescriptorsObject>(json);
        }
        finally { _ua.MPAI_AIFU_AIW_Stop(aiwId); }
    }

    // --- run helpers, modelled on HciAccessWorkflow ------------------------

    private static AIF.Controller.Message? Run(
        UserAgent ua, int aiwId, Dictionary<string, string> boundary)
    {
        var (error, outcome) = ua.RunAsync(aiwId, boundary).GetAwaiter().GetResult();
        return Completed(error, outcome);
    }

    private static AIF.Controller.Message? Completed(AifError error, UserAgent.RunOutcome? outcome)
    {
        if (error != AifError.OK || outcome is null)
        {
            Console.WriteLine($"  run failed: {error}");
            return null;
        }
        if (outcome.Suspended)
        {
            Console.WriteLine($"  unexpectedly suspended on '{outcome.WaitingPort}'.");
            return null;
        }
        if (outcome.Completed.IsError)
        {
            Console.WriteLine($"  {outcome.Completed.FailedAim}: {outcome.Completed.Payload}");
            return null;
        }
        return outcome.Completed;
    }
}
