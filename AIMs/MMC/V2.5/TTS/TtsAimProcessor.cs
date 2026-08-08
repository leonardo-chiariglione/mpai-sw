using System.Collections.Generic;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;

namespace Mpai.Aims.Tts;

// MMC-TTS-V2.5 — self-contained IAimProcessor.
// Reads its own port names from 1MMC-TTS-V2.5-I01.json at startup.
// No adapter needed.
public sealed class TtsAimProcessor : IAimProcessor
{
    private readonly string      _inputPort;
    private readonly string      _outputPort;
    private readonly PiperTtsAim _tts;

    public string InstanceId { get; }

    public TtsAimProcessor(
        string      instanceId,
        PiperTtsAim tts,
        AmdStore    store)
    {
        InstanceId  = instanceId;
        _tts        = tts;
        var ports   = AimPortReader.Load(store, instanceId);
        _inputPort  = ports.Input("OSD-TXO-V1.5");
        _outputPort = ports.Output("OSD-AUO-V1.5");
    }

    public async Task<Message> ProcessAsync(Message message)
    {
        var text   = MpaiJson.FromJson<BasicTextObject>(message.Ports[_inputPort]);
        var speech = await _tts.ProcessAsync(text);
        var json   = MpaiJson.ToJson(speech);

        return new Message
        {
            MessageId   = message.MessageId,
            MessageType = "BasicSpeechObject",
            DataType    = speech.Header,
            Payload     = json,
            Ports       = new Dictionary<string, string> { [_outputPort] = json }
        };
    }
}
