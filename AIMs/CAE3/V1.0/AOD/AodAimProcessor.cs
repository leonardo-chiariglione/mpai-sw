using System.Collections.Generic;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;

namespace Mpai.Aims.Audio;

// CAE-AOD-V1.0 — self-contained IAimProcessor.
// Reads its own port names from 1CAE-AOD-V1.0-I01.json at startup.
// No adapter needed.
public sealed class AodAimProcessor : IAimProcessor
{
    private readonly string            _inputPort;
    private readonly string            _outputPort;
    private readonly IAudioDeliveryAim _aod;

    public string InstanceId { get; }

    public AodAimProcessor(
        string            instanceId,
        IAudioDeliveryAim aod,
        AmdStore          store)
    {
        InstanceId  = instanceId;
        _aod        = aod;
        var ports   = AimPortReader.Load(store, instanceId);
        _inputPort  = ports.Input("OSD-AUO-V1.5");
        _outputPort = ports.Output("OSD-AUO-V1.5");
    }

    public async Task<Message> ProcessAsync(Message message)
    {
        var speech = MpaiJson.FromJson<BasicSpeechObject>(message.Ports[_inputPort]);
        await _aod.DeliverAsync(speech.AsAudio());

        return new Message
        {
            MessageId   = message.MessageId,
            MessageType = "BasicAudioObject",
            DataType    = speech.Header,
            Payload     = message.Ports[_inputPort],
            Ports       = new Dictionary<string, string>
            {
                [_outputPort] = message.Ports[_inputPort]
            }
        };
    }
}
