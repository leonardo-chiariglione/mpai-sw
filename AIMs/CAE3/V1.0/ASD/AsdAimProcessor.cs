using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using AIF.Controller;

using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Cae.Asd;

// CAE-ASD-V1.0 - the AIF-facing half of Audio Scene Delivery.
//
// AsdAim already does the work: DeliverSceneAsync places each object for a
// listener's Point of View through an IAudioDeliveryAim, spatially where the
// delivery implements ISpatialAudioDeliveryAim. This class was the missing half
// - the AIM had an engine and a place in the Topology but nothing the Controller
// could construct.
//
// Its output Port is HARDWARE, which is why this is legitimately an AIM and not
// a User Agent module: it renders a scene into a room rather than replying to a
// user. That is the same test that removed Speech Object Delivery from MMC-AMQ
// and MMC-TST, giving the opposite answer for a different reason.
public sealed class AsdAimProcessor : IAimProcessor
{
    private readonly AsdAim _asd;

    private readonly string _scenePort;
    private readonly string _outputPort;

    // The listener, kept between runs like anything else a scene needs. A
    // Command that moves the listener arrives at CAE-ASE; what reaches here is
    // the scene, which carries the Point of View that CAE-ASE set on it.
    private PointOfView _listener = new();

    public string InstanceId { get; }

    public AsdAimProcessor(string instanceId, AsdAim asd, AimPortReader ports)
    {
        InstanceId  = instanceId;
        _asd        = asd;
        _scenePort  = ports.Input("OSD-ASD-V1.5");
        _outputPort = ports.Output("OSD-AUO-V1.5");
    }

    public async Task<Message> ProcessAsync(Message message)
    {
        if (!message.Ports.TryGetValue(_scenePort, out var sceneJson))
        {
            return new Message
            {
                MessageId   = message.MessageId,
                MessageType = "NothingToDeliver"
            };
        }

        var scene = MpaiJson.FromJson<AudioSceneDescriptors>(sceneJson);

        // The scene's own listener wins when it has one; otherwise the last
        // known position stands. A context overrides what it provides.
        if (scene.ListenerPointOfView is not null)
            _listener = scene.ListenerPointOfView;

        Console.WriteLine(
            $"[CAE-ASD-V1.0] delivering {scene.AudioSceneDescriptorsID} " +
            $"({scene.AudioObjects?.Count ?? 0} object(s))");

        await _asd.DeliverSceneAsync(scene, _listener);

        return new Message
        {
            MessageId   = message.MessageId,
            MessageType = "AudioSceneDelivered",
            Ports       = new Dictionary<string, string> { [_outputPort] = sceneJson }
        };
    }
}