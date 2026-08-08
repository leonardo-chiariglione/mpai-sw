using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using AIF.Controller;

using Mpai.Core;

namespace Mpai.Aims.Tiq;

// AIF adapter for Text and Image Query (MMC-TIQ-V2.5).
//
// NOTE: This adapter is transitional. The end state is that the Controller
// routes data to this AIM using the port names declared in its own instance
// JSON (1MMC-TIQ-V2.5-I01.json), and the AIM reads those names from its
// own Metadata rather than from hardcoded constants. When that is in place
// this adapter disappears and the AIM implements IAimProcessor directly.
//
// In the interim the port names here MUST match those declared in
// 1MMC-TIQ-V2.5-I01.json ExternalPorts: "InputText", "InputVisual", "OutputText".
public sealed class TiqAimAdapter
    : IAimProcessor
{
    // Port names as declared in 1MMC-TIQ-V2.5-I01.json
    public const string TextInputPort   = "InputText";
    public const string VisualInputPort = "InputVisual";
    public const string OutputPort      = "OutputText";

    private readonly ITiqAim tiq;

    public string InstanceId { get; }

    public TiqAimAdapter(
        string instanceId,
        ITiqAim tiq)
    {
        InstanceId = instanceId;
        this.tiq = tiq;
    }

    public async Task<Message> ProcessAsync(
        Message message)
    {
        var question =
            MpaiJson.FromJson<BasicTextObject>(
                message.Ports[TextInputPort]);

        var image =
            MpaiJson.FromJson<BasicVisualObject>(
                message.Ports[VisualInputPort]);

        var answer =
            await tiq.ProcessAsync(question, image);

        var json = MpaiJson.ToJson(answer);

        return new Message
        {
            MessageId   = message.MessageId,
            MessageType = "BasicTextObject",
            DataType    = answer.Header,
            Payload     = json,
            Ports       = new Dictionary<string, string> { [OutputPort] = json }
        };
    }
}
