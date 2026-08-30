using System.Collections.Generic;
using System.Threading.Tasks;
using AIF.Controller;
using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Osd.Tod;

// OSD-3OD-V1.5 - 3D Model Object Delivery. Self-contained IAimProcessor.
// Reads its own port names from 1OSD-3OD-V1.5-I01.json at startup.
//
// 3OD takes a 3D Model Object (its input port accepts both OSD-B3O and OSD-3DO)
// and delivers it to a device through its own delivery abstraction
// (I3DModelDeliveryAim), keeping the object typed as a 3D Model Object to the
// device edge, where the device renders it (3D to 2D on the screen). Independent
// of the other Object Delivery AIMs. The output port re-emits the 3D Model Object
// unchanged, so a downstream consumer still sees a 3D Model Object.
public sealed class TodAimProcessor : IAimProcessor
{
    private readonly string                _inputPort;
    private readonly string                _outputPort;
    private readonly I3DModelDeliveryAim   _tod;
    public string InstanceId { get; }

    public TodAimProcessor(
        string              instanceId,
        I3DModelDeliveryAim tod,
        AimPortReader       ports)
    {
        InstanceId  = instanceId;
        _tod        = tod;
        _inputPort  = ports.Input("OSD-B3O-V1.5");    // dual-typed port [OSD-B3O, OSD-3DO]
        _outputPort = ports.Output("OSD-B3O-V1.5");   // dual-typed port [OSD-B3O, OSD-3DO]
    }

    public async Task<Message> ProcessAsync(Message message)
    {
        var model = MpaiJson.FromJson<Basic3DModelObject>(message.Ports[_inputPort]);
        // An EMPTY 3D Model Object means there is nothing to render - the model
        // data is missing. Handing zero bytes to a renderer would turn that into a
        // second, unrelated error.
        if (model is null || model.Data.Length == 0)
        {
            System.Console.WriteLine("[OSD-3OD-V1.5] nothing to render - the 3D Model Object is empty.");
        }
        else
        {
            await _tod.DeliverAsync(model);   // deliver the 3D Model Object, as a 3D Model Object
        }
        return new Message
        {
            MessageId   = message.MessageId,
            MessageType = "Basic3DModelObject",
            DataType    = "OSD-B3O-V1.5",
            Payload     = message.Ports[_inputPort],
            Ports       = new Dictionary<string, string>
            {
                [_outputPort] = message.Ports[_inputPort]
            }
        };
    }
}
