using System.Collections.Generic;
using System.Threading.Tasks;

using AIF.Controller;
using Mpai.Core;

namespace Mpai.Aims.Tiq;

// MMC-TIQ-V2.5 — self-contained IAimProcessor.
// Reads its own port names from 1MMC-TIQ-V2.5-I01.json at startup.
// Produces a text answer AND passes the input image through as OutputImage,
// so CVE-VOD can display it.
// No adapter needed.
public sealed class TiqAimProcessor : IAimProcessor
{
    private readonly string  _textInputPort;
    private readonly string  _visualInputPort;
    private readonly string  _textOutputPort;
    private readonly string  _visualOutputPort;
    private readonly ITiqAim _tiq;

    public string InstanceId { get; }

    public TiqAimProcessor(
        string   instanceId,
        ITiqAim  tiq,
        AimPortReader ports)
    {
        InstanceId        = instanceId;
        _tiq              = tiq;
        _textInputPort    = ports.Input("OSD-TXO-V1.5");
        _visualInputPort  = ports.Input("OSD-VIO-V1.5");
        _textOutputPort   = ports.Output("OSD-TXO-V1.5");
        _visualOutputPort = ports.Output("OSD-VIO-V1.5");
    }

    public async Task<Message> ProcessAsync(Message message)
    {
        var question    = MpaiJson.FromJson<BasicTextObject>(message.Ports[_textInputPort]);
        var imageJson   = message.Ports[_visualInputPort];
        var image       = MpaiJson.FromJson<BasicVisualObject>(imageJson);
        var answer      = await _tiq.ProcessAsync(question, image);
        var answerJson  = MpaiJson.ToJson(answer);

        return new Message
        {
            MessageId   = message.MessageId,
            MessageType = "BasicTextObject",
            DataType    = answer.Header,
            Payload     = answerJson,
            Ports       = new Dictionary<string, string>
            {
                [_textOutputPort]   = answerJson,
                [_visualOutputPort] = imageJson    // pass image through to CVE-VOD
            }
        };
    }
}
