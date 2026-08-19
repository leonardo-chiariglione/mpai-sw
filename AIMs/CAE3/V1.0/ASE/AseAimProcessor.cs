using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using AIF.Controller;

using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Cae.Ase;

// CAE-ASE-V1.0 - the AIF-facing half of Audio Scene Editing.
//
// OPEN BY PORT, ACT BY COMMAND, as with CAE-AOE. One scene is open at a time,
// which is why no Command names it.
//
//   AudioScene    an existing scene, opened
//   AudioObject   an AudioObject from CAE-AOE, ACROSS THE TOPOLOGY
//
// That second Port is the point of this class. AseAim used to hold an AoeAim and
// call aoe.Materialize() to expand each child object - one AIM invoking another
// with no Controller between them, and nothing in any AMD saying it happened.
// The objects now ARRIVE, because the Topology says CAE-AOE feeds CAE-ASE, and
// this processor remembers what it has been given.
//
// Four of the seven Command fields belong here:
//
//   AddedObjects     place the named objects in the open scene
//   RemovedObjects   take them out
//   MovedObjects     old attitude -> new attitude
//   ChangedObjects   external attributes of a placed object
//
// with UserPoV setting the listener's Point of View.
public sealed class AseAimProcessor : IAimProcessor
{
    private readonly AseAim _ase;

    private readonly string _commandPort;
    private readonly string _objectPort;
    private readonly string _scenePort;
    private readonly string _povPort;
    private readonly string _outputPort;

    // What is open, between runs.
    private string? _openSceneId;

    // What CAE-AOE has sent, so that materialising a scene needs no call to
    // another AIM. An object placed in a scene has necessarily passed through
    // this Port to get there.
    private readonly Dictionary<string, AudioObject> _received = new();

    public string InstanceId { get; }

    public AseAimProcessor(
        string        instanceId,
        AseAim        ase,
        AimPortReader ports)
    {
        InstanceId   = instanceId;
        _ase         = ase;

        _commandPort = ports.Input("CAE-UCM-V1.0");
        _objectPort  = ports.Input("OSD-AUO-V1.5");
        _scenePort   = ports.Input("OSD-ASD-V1.5");
        _povPort     = ports.Input("OSD-OPV-V1.5");
        _outputPort  = ports.Output("OSD-ASD-V1.5");
    }

    public Task<Message> ProcessAsync(Message message)
    {
        // 1. an AudioObject from CAE-AOE - remembered, not fetched.
        if (message.Ports.TryGetValue(_objectPort, out var objectJson))
        {
            var received = MpaiJson.FromJson<AudioObject>(objectJson);
            if (!string.IsNullOrWhiteSpace(received.AudioObjectID))
            {
                _received[received.AudioObjectID] = received;
                Console.WriteLine($"[CAE-ASE-V1.0] received {received.AudioObjectID} from the Topology");
            }
        }

        // 2. opening, by Port.
        if (message.Ports.TryGetValue(_scenePort, out var sceneJson))
        {
            var opened = MpaiJson.FromJson<AudioSceneDescriptors>(sceneJson);
            if (!string.IsNullOrWhiteSpace(opened.AudioSceneDescriptorsID))
            {
                _openSceneId = opened.AudioSceneDescriptorsID;
                Console.WriteLine($"[CAE-ASE-V1.0] opened {_openSceneId}");
            }
        }

        PointOfView? pov = null;
        if (message.Ports.TryGetValue(_povPort, out var povJson))
            pov = MpaiJson.FromJson<PointOfView>(povJson);

        // 3. acting, by Command.
        if (message.Ports.TryGetValue(_commandPort, out var commandJson))
            Apply(MpaiJson.FromJson<UserCommand>(commandJson), pov);
        else if (pov is not null && _openSceneId is not null)
            _openSceneId = _ase.SetSceneListener(_openSceneId, pov).AssetId;

        if (_openSceneId is null)
            return Task.FromResult(new Message
            {
                MessageId   = Guid.NewGuid().ToString(),
                MessageType = "NoOpenScene"
            });

        // 4. the open scene, as it now stands. Resolution comes from what this
        // AIM has been given, not from calling CAE-AOE.
        var materialised = _ase.Materialize(_openSceneId, Resolve);

        return Task.FromResult(new Message
        {
            MessageId   = Guid.NewGuid().ToString(),
            MessageType = "AudioSceneDescriptors",
            Ports       = { [_outputPort] = MpaiJson.ToJson(materialised) }
        });
    }

    // An object this AIM has not been sent stays an identifier. That is honest -
    // AudioSceneObjectEntry carries an ObjectOrID - and it is better than
    // reaching for another AIM to fill the gap.
    private AudioObject? Resolve(string audioObjectId) =>
        _received.TryGetValue(audioObjectId, out var found) ? found : null;

    private void Apply(UserCommand command, PointOfView? pov)
    {
        var data = command.UserCommandData;
        if (data is null) return;

        var listener = data.UserPoV ?? pov;

        if (data.AddedObjects is { Objects.Count: > 0 } added)
        {
            foreach (var entry in added.Objects)
            {
                var objectId = IdOf(entry.ObjectID);
                if (objectId is null) continue;

                var placement = Placement(entry.SpatialAttitude);

                // The listener is NOT passed here. An entity reused in another
                // context keeps its own attributes unless the context provides
                // them, and the containing context is the SCENE: stamping the
                // scene's Point of View onto every entry would flatten exactly
                // the override the rule describes. It is set once, below.
                _openSceneId = _openSceneId is null
                    ? _ase.CreateScene(objectId, placement).AssetId
                    : _ase.AddObjectToScene(_openSceneId, objectId, placement).AssetId;

                Console.WriteLine($"[CAE-ASE-V1.0] placed {objectId} -> {_openSceneId}");
            }
        }

        if (_openSceneId is null)
        {
            Console.WriteLine("[CAE-ASE-V1.0] a Command arrived with no scene open - ignored.");
            return;
        }

        if (data.MovedObjects is { Objects.Count: > 0 } moved)
        {
            foreach (var entry in moved.Objects)
            {
                var objectId = IdOf(entry.ObjectID);
                if (objectId is null) continue;

                // A move is expressed as a re-placement at the new attitude. The
                // old one is carried by the Command for continuity of rendering;
                // the engine does not take it.
                _openSceneId = _ase.AddObjectToScene(
                    _openSceneId, objectId, Placement(entry.NewSpatialAttitude)).AssetId;

                Console.WriteLine($"[CAE-ASE-V1.0] moved {objectId}");
            }
        }

        if (data.ChangedObjects is { Objects.Count: > 0 } changed)
        {
            foreach (var entry in changed.Objects)
            {
                var objectId = IdOf(entry.ObjectID);
                if (objectId is null) continue;

                _openSceneId = _ase.AddObjectToScene(
                    _openSceneId, objectId, Placement(entry.SpatialAttitude)).AssetId;

                Console.WriteLine($"[CAE-ASE-V1.0] changed (external) {objectId}");
            }
        }

        if (data.RemovedObjects is { Objects.Count: > 0 })
        {
            // AseAim has no removal today. Saying so is better than silently
            // doing nothing, or than inventing a removal whose semantics for
            // scene identity nobody has decided.
            Console.WriteLine("[CAE-ASE-V1.0] RemovedObjects: not implemented by AseAim.");
        }

        // Once, on the scene - whatever else the Command did. The scene's Point
        // of View overrides each entry's; the entries keep their own when the
        // scene has none.
        if (listener is not null)
            _openSceneId = _ase.SetSceneListener(_openSceneId, listener).AssetId;
    }

    private static SpaceTime? Placement(SpatialAttitude? attitude) =>
        attitude is null ? null : new SpaceTime { SpatialAttitude1 = attitude };

    private static string? IdOf(ManagedObject? managed) =>
        managed is null ? null
        : !string.IsNullOrWhiteSpace(managed.ObjectID) ? managed.ObjectID
        : !string.IsNullOrWhiteSpace(managed.AudioObject?.AudioObjectID) ? managed.AudioObject!.AudioObjectID
        : null;
}