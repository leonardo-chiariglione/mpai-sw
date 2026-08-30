using System;
using System.Collections.Generic;

using AIF.Controller;

using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Mmc.Efi;

// MMC-EFI-V2.5 - Entity Face Interpretation, as an AIF IAimProcessor.
//
// Receives a Basic Visual Object (OSD-BVO) whose Visual Qualifier declares its
// content is a face, and produces the Face Personal Status (MMC-FPS).
//
// PHASE A (first pass): facial expression cannot be read from pixels by a
// heuristic, so this pass emits a NEUTRAL Face Personal Status (a calm Emotion) to
// prove the pipeline end-to-end. PHASE B replaces this engine with a facial-emotion
// model (e.g. HSEmotion, ONNX) for an effective Face Personal Status - without
// changing the interface.
public sealed class EfiAimProcessor : IAimProcessor
{
    private readonly string _instanceId;
    private readonly string _inPort;   // OSD-BVO
    private readonly string _outPort;  // MMC-FPS

    public EfiAimProcessor(string instanceId, AimPortReader ports)
    {
        _instanceId = instanceId;
        _inPort  = ports.Input("OSD-BVO-V1.5");
        _outPort = ports.Output("MMC-FPS-V2.5");
    }

    public string InstanceId => _instanceId;

    public System.Threading.Tasks.Task<Message> ProcessAsync(Message message)
    {
        if (!message.Ports.TryGetValue(_inPort, out var bvoJson) || string.IsNullOrWhiteSpace(bvoJson))
            return System.Threading.Tasks.Task.FromResult(
                Message.Error(message.MessageId, _instanceId, "no Basic Visual Object on input port"));

        // Phase A: neutral placeholder (calm), honestly first-pass.
        var fps = new FacePersonalStatus
        {
            FaceEmotion = Emotion.Of(FactorLabel.Of("CALMNESS", "calm", null, 0.5))
        };

        return System.Threading.Tasks.Task.FromResult(new Message
        {
            MessageId = message.MessageId,
            MessageType = message.MessageType,
            Ports = new Dictionary<string, string> { [_outPort] = MpaiJson.ToJson(fps) }
        });
    }
}
