using System.Threading.Tasks;
using Mpai.Core.OSD;

namespace Mpai.Osd.Tod;

// 3D Model Object Delivery device abstraction. The 3D-model counterpart of
// ISpeechDeliveryAim: it delivers a 3D Model Object to a device (a 3D renderer),
// keeping the object typed as a 3D Model Object to the device edge, where the
// device renders it (3D to 2D). Independent of the other Object Delivery AIMs;
// 3OD has its own delivery device abstraction and implementations.
public interface I3DModelDeliveryAim
{
    Task DeliverAsync(Basic3DModelObject model);
}
