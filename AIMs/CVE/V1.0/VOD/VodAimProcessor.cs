using System.Collections.Generic;
using System.Threading.Tasks;

using AIF.Controller;
using Mpai.Core;

namespace Mpai.Aims.Visual;

// CVE-VOD-V1.0 — self-contained IAimProcessor.
// Reads its own port names from 1CVE-VOD-V1.0-I01.json at startup.
// No adapter needed.
public sealed class VodAimProcessor : IAimProcessor
{
    private readonly string            _inputPort;
    private readonly string            _outputPort;
    private readonly IVisualDeliveryAim _vod;

    public string InstanceId { get; }

    public VodAimProcessor(
        string             instanceId,
        IVisualDeliveryAim vod,
        AimPortReader           ports)
    {
        InstanceId  = instanceId;
        _vod        = vod;
        _inputPort  = ports.Input("OSD-VIO-V1.5");
        _outputPort = ports.Output("OSD-VIO-V1.5");
    }

    public async Task<Message> ProcessAsync(Message message)
    {
        var visual = MpaiJson.FromJson<BasicVisualObject>(message.Ports[_inputPort]);
        await _vod.DeliverAsync(visual);

        return new Message
        {
            MessageId   = message.MessageId,
            MessageType = "BasicVisualObject",
            DataType    = visual.Header,
            Payload     = message.Ports[_inputPort],
            Ports       = new Dictionary<string, string>
            {
                [_outputPort] = message.Ports[_inputPort]
            }
        };
    }
}
