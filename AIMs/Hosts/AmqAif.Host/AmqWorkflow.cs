using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;

using AifMessage = AIF.Controller.Message;

namespace AmqAif.Host;

// The AMQ choreography (the 51-step workflow) housed by the User Agent.
//
// The AMQ composite AMD is the INFRASTRUCTURE (structure: which AIMs, which
// connections). This runner is ONE WAY of exploiting that infrastructure -
// the human-facing choreography - and it lives on the User Agent side,
// calling only the MPAI_AIFU_* API and moving data across boundary Ports.
//
// This first slice implements Phase 1 (steps 1-17): acquire the folder image
// and OCR it into the recognised file listing.
//
// HOW OCR IS REACHED, and why it changed. MMC-OCR is NOT a SubAIM of
// MMC-AMQ-V2.5: the specification lists VOA, SOA, ASR, TIQ, TTS, SOD and VOD.
// It is a tool this choreography uses. The previous version therefore reached
// into the running AIW - UserAgent.TryGetRuntime, then AimHost.RegisterRuntime
// and AimHost.ProcessAsync - to instantiate MMC-OCR inside AMQ's runtime and
// drive it directly.
//
// That is the User Agent doing the Controller's work, and it is the thing zero
// trust exists to prevent: an AIM was registered into a running AIW by something
// that is not the Controller, and then invoked outside the Topology that governs
// it. Nothing in the AMD said MMC-OCR was there; nothing could have stopped it.
//
// The fix needs no new mechanism. A User Agent that needs an AIM asks the
// Controller for an AIW containing it - one SubAIM and no code - exactly as
// UAG-SPK-V1.0 gives the User Agent a voice. The public API is sufficient:
// Start, RunAsync, Stop.
//
// UAG-OCR-V1.0 IS SCAFFOLDING, and deliberately so. Optical Character
// Recognition is to become an application in its own right, alongside AMQ, TST
// and ASM. When it does, this choreography points at that application's AIW and
// the placeholder AMD is deleted. Its ports are already the shape that
// application will have - a Visual Object in, Recognised Text out - so the
// change should be the module name and nothing else.
public sealed class AmqWorkflow
{
    private const string OcrAiw = "UAG-OCR-V1.0";

    private readonly UserAgent    _ua;
    private readonly IAimProvider _provider;
    private readonly AimSettings  _settings;

    // Phase 1 STARTS the AMQ AIW and leaves it running: the folder image is
    // written to its boundary, where the Topology routes it to CVE-VOA, and the
    // later phases need the same AIW. Nothing stopped it before, so it stayed
    // started with an input queued and its models loaded until the process ended.
    // The caller now has something to call.
    private int? _amqAiwId;

    public bool IsRunning => _amqAiwId is not null;

    // Boundary interactions the runner needs the host application to service.
    // (These are the "prompt/capture/display" primitives from the WDL.)
    public required Func<string, Task<byte[]>> CaptureImageFromUser { get; init; }
    public required Func<RecognisedText, Task> DisplayRecognisedText { get; init; }

    public AmqWorkflow(UserAgent ua, IAimProvider provider, AimSettings settings)
    {
        _ua       = ua;
        _provider = provider;
        _settings = settings;
    }

    // Phase 1 - steps 1 to 17.
    public async Task<RecognisedText> RunFolderOcrPhaseAsync()
    {
        // Step 1-3: User asks to start AMQ; UA initialises Controller and starts AIW.
        _ua.MPAI_AIFU_Controller_Initialize();
        var err = _ua.MPAI_AIFU_AIW_Start("MMC-AMQ-V2.5", _provider, _settings, out var aiwId);
        if (err != AifError.OK)
            throw new InvalidOperationException($"AIW_Start failed: {err}");

        _amqAiwId = aiwId;

        // Step 4-6: the AIW requires a Visual Object; UA prompts the user.
        // Step 7: user prints screen of the folder and sends the image to the UA.
        var folderBytes = await CaptureImageFromUser(
            "Print the screen of the image folder, then choose the file.");

        var folderImage = BasicVisualObject.FromFile("folder-screen.png", folderBytes);

        // Step 8-9: UA sends the folder image to the Controller for the AIW,
        // writing it to the composite's boundary INPUT Port (InputVisual).
        var inMsg = new AifMessage
        {
            MessageId   = Guid.NewGuid().ToString(),
            MessageType = "FolderImage",
            Ports       = new Dictionary<string, string>
            {
                ["InputVisual"] = MpaiJson.ToJson(folderImage)
            }
        };
        var w = _ua.PortInputWrite(aiwId, "InputVisual", inMsg);
        if (w != AifError.OK)
            throw new InvalidOperationException($"Port_Input_Write failed: {w}");

        // Step 10-12: the recognition itself, through an AIW of the User Agent's
        // own rather than through AMQ's runtime.
        var recognised = await RecogniseAsync(folderImage);

        // Step 17: UA displays the recognised file listing to the user.
        await DisplayRecognisedText(recognised);

        return recognised;
    }

    // The end of the choreography, whenever the caller decides that is - after
    // Phase 1 today, after the last phase once the others exist. Separate from
    // the phases because the AIW outlives any one of them.
    public void Stop()
    {
        if (_amqAiwId is not int aiwId) return;

        _amqAiwId = null;
        var err = _ua.MPAI_AIFU_AIW_Stop(aiwId);

        if (err != AifError.OK)
            Console.WriteLine($"[UA] AIW_Stop returned {err}");
    }

    // MMC-OCR as a tool of the User Agent: its own AIW, started, run and stopped
    // through the public API. The Controller instantiates the AIM, routes to it
    // per the Topology, and tears it down - which is the whole point.
    private async Task<RecognisedText> RecogniseAsync(BasicVisualObject image)
    {
        var started = _ua.MPAI_AIFU_AIW_Start(OcrAiw, _provider, _settings, out var ocrAiwId);
        if (started != AifError.OK)
            throw new InvalidOperationException($"AIW_Start {OcrAiw} failed: {started}");

        try
        {
            var boundary = new Dictionary<string, string>
            {
                ["InputVisual"] = MpaiJson.ToJson(image)
            };

            var (error, outcome) = await _ua.RunAsync(ocrAiwId, boundary);

            if (error != AifError.OK || outcome?.Completed is null)
                throw new InvalidOperationException($"{OcrAiw} failed: {error}");

            if (outcome.Completed.IsError)
                throw new InvalidOperationException(
                    $"{outcome.Completed.FailedAim}: {outcome.Completed.Payload}");

            if (!outcome.Completed.Ports.TryGetValue("OutputText", out var json))
                throw new InvalidOperationException($"{OcrAiw} produced no OutputText.");

            return MpaiJson.FromJson<RecognisedText>(json);
        }
        finally
        {
            _ua.MPAI_AIFU_AIW_Stop(ocrAiwId);
        }
    }
}