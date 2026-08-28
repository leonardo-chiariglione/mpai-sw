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

namespace Hci.Access.Host;

// The HCI "check authorised users" choreography, driven by the User Agent
// exactly as MMC-TST's spoken interface (TstVoiceTest) does - through the public
// MPAI_AIFU_* API only, never invoking an AIM. The UA captures at the boundary
// and hands data to the Controller, which routes it to the AIM by Data Type;
// outputs come back on the boundary. AIWs are started once and kept alive so
// their models load a single time.
//
// This first slice PROVES the identity infrastructure: MMC-SIR through the
// Controller, printing the speaker identity. The full choreography (a spoken
// "speak your password" via a UAG prompt AIW, press-to-stop capture, FIR + SIR
// -> IDR -> grant) grows from here, reusing the same Run / RunWithPressToStop
// helpers modelled here.
public sealed class HciAccessWorkflow
{
    private const string SirAiw = "UAG-SIR-V1.0";   // wraps MMC-SIR (one SubAIM, no code)
    private const string FirAiw = "UAG-FIR-V1.0";   // wraps PAF-FIR (one SubAIM, no code)

    private readonly UserAgent    _ua;
    private readonly IAimProvider _provider;
    private readonly AimSettings  _settings;

    public HciAccessWorkflow(UserAgent ua, IAimProvider provider, AimSettings settings)
    {
        _ua       = ua;
        _provider = provider;
        _settings = settings;
    }

    // Identify the speaker in a wav, through the Controller. Returns the OSD-IID.
    public InstanceIdentifier? IdentifySpeaker(string wavPath)
    {
        if (!File.Exists(wavPath))
            throw new FileNotFoundException($"Speech fixture not found: {wavPath}");

        _ua.MPAI_AIFU_Controller_Initialize();

        if (_ua.MPAI_AIFU_AIW_Start(SirAiw, _provider, _settings, out var sirAiwId) != AifError.OK)
        {
            Console.WriteLine($"  could not start {SirAiw}.");
            return null;
        }

        try
        {
            // The User Agent captures the speech at the boundary (here: reads the
            // enrolled clip) and wraps it as the Basic Speech Object the Controller
            // routes to SIR's OSD-BSO input port.
            var bso = BasicSpeechObject.FromData(File.ReadAllBytes(wavPath), null);
            var boundary = new Dictionary<string, string>
            {
                ["InputSpeech"] = MpaiJson.ToJson(bso)
            };

            var completed = Run(_ua, sirAiwId, boundary);
            if (completed is null) { Console.WriteLine("  [diag] Run returned null (see error above)."); return null; }

            // Diagnostics.

            // The speaker identity (OSD-IID) comes back on the AIW's OutputSpeakerID boundary port.
            string? iidJson = completed.Ports.TryGetValue("OutputSpeakerID", out var j) ? j
                            : completed.Ports.Values.FirstOrDefault();
            return string.IsNullOrWhiteSpace(iidJson) ? null : MpaiJson.FromJson<InstanceIdentifier>(iidJson);
        }
        finally
        {
            _ua.MPAI_AIFU_AIW_Stop(sirAiwId);
        }
    }

    // Identify the user's face in a picture, through the Controller. Returns the OSD-IID.
    public InstanceIdentifier? IdentifyFace(string imagePath)
    {
        if (!File.Exists(imagePath))
            throw new FileNotFoundException($"Image fixture not found: {imagePath}");

        _ua.MPAI_AIFU_Controller_Initialize();

        if (_ua.MPAI_AIFU_AIW_Start(FirAiw, _provider, _settings, out var firAiwId) != AifError.OK)
        {
            Console.WriteLine($"  could not start {FirAiw}.");
            return null;
        }

        try
        {
            // UA captures the picture at the boundary and wraps it as the Basic
            // Visual Object the Controller routes to FIR's OSD-BVO input port.
            var bvo = BasicVisualObject.FromFile(Path.GetFileName(imagePath), File.ReadAllBytes(imagePath));
            var boundary = new Dictionary<string, string>
            {
                ["InputVisual"] = MpaiJson.ToJson(bvo)
            };

            var completed = Run(_ua, firAiwId, boundary);
            if (completed is null) return null;

            // The face identity (OSD-IID) comes back on the AIW's FaceID boundary port.
            string? iidJson = completed.Ports.TryGetValue("FaceID", out var j) ? j
                            : completed.Ports.Values.FirstOrDefault();
            return string.IsNullOrWhiteSpace(iidJson) ? null : MpaiJson.FromJson<InstanceIdentifier>(iidJson);
        }
        finally
        {
            _ua.MPAI_AIFU_AIW_Stop(firAiwId);
        }
    }

    // --- run helpers, modelled on TstVoiceTest -----------------------------

    private static AIF.Controller.Message? Run(
        UserAgent ua, int aiwId, Dictionary<string, string> boundary)
    {
        var (error, outcome) = ua.RunAsync(aiwId, boundary).GetAwaiter().GetResult();
        return Completed(error, outcome);
    }

    // Press-to-stop capture (for the future spoken "password" step): run on a
    // background task; on Enter, ask the Controller to PAUSE (SOA closes the mic)
    // then RESUME so the pipeline carries on. Stop would discard the recording.
    private static AIF.Controller.Message? RunWithPressToStop(
        UserAgent ua, int aiwId, Dictionary<string, string> boundary)
    {
        var running = Task.Run(() => ua.RunAsync(aiwId, boundary));
        while (!running.IsCompleted)
        {
            if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Enter)
            {
                ua.MPAI_AIFU_AIW_Pause(aiwId);
                ua.MPAI_AIFU_AIW_Resume(aiwId);
                break;
            }
            Thread.Sleep(25);
        }
        var (error, outcome) = running.GetAwaiter().GetResult();
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
        if (outcome.Completed is null) return null;
        if (outcome.Completed.IsError)
        {
            Console.WriteLine($"  {outcome.Completed.FailedAim}: {outcome.Completed.Payload}");
            return null;
        }
        return outcome.Completed;
    }
}
