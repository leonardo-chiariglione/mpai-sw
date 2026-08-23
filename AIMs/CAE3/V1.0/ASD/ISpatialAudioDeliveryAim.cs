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
    // The whole PLACEMENT, not merely its attitude.
    //
    // This took a SpatialAttitude, which carries where something is and not
    // when: a backend that mixes needs both, since the start times are what
    // decide whether two Objects sound together or in turn. A SpaceTime holds
    // the pair, and AsdAim had it in hand all along.
    Task DeliverAsync(BasicAudioObject audio, SpaceTime? placement, PointOfView? listenerPointOfView);
}