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
public sealed class AmqWorkflow
{
    private readonly UserAgent    _ua;
    private readonly IAimProvider _provider;
    private readonly AimSettings  _settings;

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

        // Step 10-12: the AIW routes the image to MMC-OCR through the Controller.
        // Here we drive the OCR stage through the AimHost (the Controller's
        // execution surface), reading the boundary input Port we just wrote.
        if (!_ua.TryGetRuntime(aiwId, out var host, out var ports))
            throw new InvalidOperationException("AIW runtime not available.");

        // MMC-OCR is a tool THIS choreography uses; it is not a SubAIM of the
        // AMQ infrastructure. The User Agent workflow instantiates it via the
        // provider and registers it in the runtime before routing to it.
        if (!host.Contains("MMC-OCR-V2.5"))
            host.RegisterRuntime(
                _provider.Create("MMC-OCR-V2.5", _settings.For("MMC-OCR-V2.5")));

        var ocrInput = await ports.OutputReadAsync("InputVisual");
        // ^ drain the boundary Port (FIFO) we wrote to; its payload is the image.

        var ocrResult = await host.ProcessAsync("MMC-OCR-V2.5", new AifMessage
        {
            MessageId   = ocrInput.MessageId,
            MessageType = "Recognise",
            Ports       = new Dictionary<string, string>
            {
                ["InputVisual"] = ocrInput.Ports["InputVisual"]
            }
        });
        if (ocrResult.IsError || ocrResult.IsCancelled)
            throw new InvalidOperationException($"OCR failed: {ocrResult.Payload}");

        // Step 13-16: OCR's RecognisedText goes back to the AIW, then the UA
        // reads it from the composite's boundary OUTPUT Port.
        if (ports.Has("OutputText"))
        {
            ports.InputWrite("OutputText", ocrResult);  // place on the boundary Port
        }

        var recognised = MpaiJson.FromJson<RecognisedText>(ocrResult.Payload);

        // Step 17: UA displays the recognised file listing to the user.
        await DisplayRecognisedText(recognised);

        return recognised;
    }
}
