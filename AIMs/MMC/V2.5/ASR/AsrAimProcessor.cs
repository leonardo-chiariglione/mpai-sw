using System.Collections.Generic;
using System.Threading.Tasks;

using AIF.Controller;
using Mpai.Core;

namespace Mpai.Aims.Asr;

// MMC-ASR-V2.5 — self-contained IAimProcessor.
// Reads its own port names from 1MMC-ASR-V2.5-I01.json at startup.
//
// ASR consumes SPEECH (OSD-SPO-V1.5): speech can be recognised, generic audio
// cannot. The input port is now typed as speech and the object arrives as a
// Basic Speech Object directly (no audio->speech reinterpretation needed).
public sealed class AsrAimProcessor : IAimProcessor
{
    private readonly string        _inputPort;
    private readonly string        _outputPort;
    private readonly WhisperAsrAim _asr;

    public string InstanceId { get; }

    public AsrAimProcessor(
        string        instanceId,
        WhisperAsrAim asr,
        AimPortReader      ports)
    {
        InstanceId   = instanceId;
        _asr         = asr;
        _inputPort   = ports.Input("OSD-SPO-V1.5");
        _outputPort  = ports.Output("OSD-TXO-V1.5");
    }

    public async Task<Message> ProcessAsync(Message message)
    {
        var speech = MpaiJson.FromJson<BasicSpeechObject>(message.Ports[_inputPort]);
        var text   = await _asr.ProcessAsync(speech);
        var json   = MpaiJson.ToJson(text);

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
