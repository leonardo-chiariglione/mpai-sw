using System.Collections.Generic;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;

namespace Mpai.Aims.Asr;

// MMC-ASR-V2.5 — self-contained IAimProcessor.
// Reads its own port names from 1MMC-ASR-V2.5-I01.json at startup.
// No adapter needed.
public sealed class AsrAimProcessor : IAimProcessor
{
    private readonly string        _inputPort;
    private readonly string        _outputPort;
    private readonly WhisperAsrAim _asr;

    public string InstanceId { get; }

    public AsrAimProcessor(
        string        instanceId,
        WhisperAsrAim asr,
        AmdStore      store)
    {
        InstanceId   = instanceId;
        _asr         = asr;
        var ports    = AimPortReader.Load(store, instanceId);
        _inputPort   = ports.Input("OSD-AUO-V1.5");
        _outputPort  = ports.Output("OSD-TXO-V1.5");
    }

    public async Task<Message> ProcessAsync(Message message)
    {
        var audio = MpaiJson.FromJson<BasicAudioObject>(message.Ports[_inputPort]);
        var text  = await _asr.ProcessAsync(audio.AsSpeech());
        var json  = MpaiJson.ToJson(text);

        return new Message
        {
            MessageId   = message.MessageId,
            MessageType = "BasicTextObject",
            DataType    = text.Header,
            Payload     = json,
            Ports       = new Dictionary<string, string> { [_outputPort] = json }
        };
    }
}
