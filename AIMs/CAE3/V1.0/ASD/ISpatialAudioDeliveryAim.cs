using System.Threading.Tasks;

using Mpai.Core;

namespace Mpai.Cae.Asd;

// Optional richer capability a delivery backend can implement to actually
// use positional information, rather than just ignoring it. AsdAim checks
// for this interface and falls back to plain IAudioDeliveryAim.DeliverAsync
// (position ignored entirely) if a backend doesn't implement it - existing
// backends (FileAudioDelivery, AplayAudioDelivery, WinmmAudioDelivery) are
// untouched and keep working exactly as before.
public interface ISpatialAudioDeliveryAim : IAudioDeliveryAim
{
    Task DeliverAsync(BasicAudioObject audio, SpatialAttitude? objectPosition, PointOfView? listenerPointOfView);
}