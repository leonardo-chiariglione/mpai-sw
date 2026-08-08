using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using AIF.Controller;

using Mpai.Core;

namespace Mpai.Aims.Visual;

// AIF adapter for Visual Object Delivery (CVE-VOD-V1.0).
// Consumes the Basic Visual Object routed to its "VisualObject" port
// and passes it to the delivery AIM.
public sealed class VodAimAdapter
    : IAimProcessor
{
    public const string InputPort = "VisualObject";
    public const string OutputPort = "VisualObject";

    private readonly IVisualDeliveryAim vod;

    public string InstanceId { get; }

    public VodAimAdapter(
        string instanceId,
        IVisualDeliveryAim vod)
    {
        InstanceId = instanceId;
        this.vod = vod;
    }

    public async Task<Message> ProcessAsync(
        Message message)
    {
        var visual =
            MpaiJson.FromJson<BasicVisualObject>(
                message.Ports[InputPort]);

        await vod.DeliverAsync(visual);

        return new Message
        {
            MessageId = message.MessageId,
            MessageType = "BasicVisualObject",
            DataType = visual.Header,
            Payload = message.Ports[InputPort],
            Ports = new Dictionary<string, string>
            {
                [OutputPort] = message.Ports[InputPort]
            }
        };
    }
}