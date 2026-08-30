using System;
using System.Threading.Tasks;
using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Osd.Tod;

// A headless 3D Model Object Delivery device: reports what it would render rather
// than driving a display. It proves the 3OD delivery path through the Controller
// without a graphical device. The graphical device (a WebView-backed 3D renderer)
// is provided by the host application that owns a display surface, exactly as the
// loudspeaker device is provided for speech delivery.
public sealed class ConsoleModelDelivery : I3DModelDeliveryAim
{
    public Task DeliverAsync(Basic3DModelObject model)
    {
        var format = model.ModelQualifier is not null ? " (qualified)" : "";
        AimLog.Write("OSD-3OD-V1.5",
            $"rendering 3D Model Object {model.Basic3DModelObjectID}: {model.Data.Length:N0} bytes{format}");
        return Task.CompletedTask;
    }
}
