using System.Threading.Tasks;

namespace Mpai.Cae.Asd;

// A backend that plays a scene AS A SCENE: everything at once, mixed.
//
// ISpatialAudioDeliveryAim is handed one leaf at a time and acts on each, so a
// composition of three voices is delivered as three voices one after another.
// That says where each of them is and not what the arrangement sounds like.
//
// A mixing backend COLLECTS instead. AsdAim tells it when a delivery begins and
// when every leaf has been handed over, and the sound is made once at the end -
// which is the only point at which the whole of it is known.
//
// It also means the backend, not AsdAim, applies the start times: AsdAim delays
// between leaves so that a sequential backend plays them in order, and a mixing
// one wants the offsets instead, so that two Objects starting at the same moment
// actually start at the same moment.
public interface IMixingAudioDeliveryAim : ISpatialAudioDeliveryAim
{
    // A delivery is beginning; discard anything held from the last one.
    void Begin();

    // Every leaf has been handed over. Make the sound.
    Task FinishAsync();
}