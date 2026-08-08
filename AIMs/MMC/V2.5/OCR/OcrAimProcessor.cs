using System.Collections.Generic;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;

namespace Mpai.Aims.Ocr;

// MMC-OCR-V2.5 — self-contained IAimProcessor.
// Reads its own port names from 1MMC-OCR-V2.5-I01.json at startup.
// Pure: a Visual Object in, Recognised Text out. No OS access, no adapter.
public sealed class OcrAimProcessor : IAimProcessor
{
    private readonly string   _inputPort;
    private readonly string   _outputPort;
    private readonly IOcrAim  _ocr;

    public string InstanceId { get; }

    public OcrAimProcessor(
        string   instanceId,
        IOcrAim  ocr,
        AmdStore store)
    {
        InstanceId  = instanceId;
        _ocr        = ocr;
        var ports   = AimPortReader.Load(store, instanceId);
        _inputPort  = ports.Input("OSD-VIO-V1.5");
        _outputPort = ports.Output("MMC-RTX-V2.5");
    }

    public async Task<Message> ProcessAsync(Message message)
    {
        var image = MpaiJson.FromJson<BasicVisualObject>(message.Ports[_inputPort]);
        var text  = await _ocr.ProcessAsync(image);
        var json  = MpaiJson.ToJson(text);

        return new Message
        {
            MessageId   = message.MessageId,
            MessageType = "RecognisedText",
            DataType    = text.Header,
            Payload     = json,
            Ports       = new Dictionary<string, string> { [_outputPort] = json }
        };
    }
}
