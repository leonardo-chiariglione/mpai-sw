using System;
using System.Collections.Generic;

using AIF.Controller;

using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Paf.Gfd;

// PAF-GFD-V1.6 - Generative Face Description, as an AIF IAimProcessor.
//
// The generative counterpart of Entity Face Description (analysis): where the
// analysis EFD reads a face and produces its descriptors, this generative EFD takes
// the machine's (generated) Face Personal Status and produces the Machine Face
// Descriptors - the facial EXPRESSION the CAV should display - coded as FACS Action
// Units via EM-FACS. This is the "Entity Face Description" SubAIM of the Response
// and Scene Rendering composite, used to impersonate the CAV visually.
//
// The machine's Face Personal Status carries an Emotion (its generated/simulated
// affect, e.g. CALMNESS/calm or HAPPINESS/happy). EM-FACS maps that emotion, at its
// Degree, to the Action Unit activations that express it (e.g. happiness -> AU6 +
// AU12). The AU descriptor is renderer-agnostic: it drives the 2D visual delivery
// now, and a 3D FACS avatar later, unchanged.
public sealed class GfdAimProcessor : IAimProcessor
{
    private readonly string _instanceId;
    private readonly string _inPort;   // MMC-FPS (machine Face Personal Status)
    private readonly string _outPort;  // PAF-FDO (Machine Face Descriptors = AU weights)

    public GfdAimProcessor(string instanceId, AimPortReader ports)
    {
        _instanceId = instanceId;
        _inPort  = ports.Input("MMC-FPS-V2.5");
        _outPort = ports.Output("PAF-FDO-V1.6");
    }

    public string InstanceId => _instanceId;

    public System.Threading.Tasks.Task<Message> ProcessAsync(Message message)
    {
        if (!message.Ports.TryGetValue(_inPort, out var fpsJson) || string.IsNullOrWhiteSpace(fpsJson))
            return System.Threading.Tasks.Task.FromResult(
                Message.Error(message.MessageId, _instanceId, "no Face Personal Status on input port"));

        var fps = MpaiJson.FromJson<FacePersonalStatus>(fpsJson);

        // The machine's emotion category + its intensity (Degree).
        string? category = fps?.FaceEmotion?.Category;
        double intensity = fps?.FaceEmotion?.Degree ?? 0.6;

        // If the machine's affect is carried as a Cognitive State (e.g. SURPRISE),
        // fall back to it for the expression when no emotion is present.
        if (category is null && fps?.FaceCognitiveState?.Category is { } cog)
        {
            category = cog;
            intensity = fps.FaceCognitiveState.Degree ?? 0.6;
        }

        var aus = EmFacs.ToActionUnits(category, intensity);

        return System.Threading.Tasks.Task.FromResult(new Message
        {
            MessageId = message.MessageId,
            MessageType = message.MessageType,
            Ports = new Dictionary<string, string> { [_outPort] = MpaiJson.ToJson(aus) }
        });
    }
}
