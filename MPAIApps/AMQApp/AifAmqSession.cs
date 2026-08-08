using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using AIF.Controller;

using Mpai.Core;

using AifMessage = AIF.Controller.Message;

namespace MPAIApps.AMQApp;

public sealed class AifAmqSession
{
    private readonly AimHost         _host;
    private readonly MachineExecutor _executor;
    private readonly DescriptorGraph _graph;

    private AimContext        _aoaContext;
    private Task<AifMessage>? _aoaTask;

    public AifAmqSession(AimHost host, MachineExecutor executor, DescriptorGraph graph)
    {
        _host     = host;
        _executor = executor;
        _graph    = graph;
    }

    public async Task<BasicVisualObject> AcquireAndDisplayImageAsync()
    {
        var voaResult = await _host.ProcessAsync("CVE-VOA-V1.0", new AifMessage
        {
            MessageId   = Guid.NewGuid().ToString(),
            MessageType = "AcquireImage",
            Ports       = new Dictionary<string, string>()
        });

        if (voaResult.IsError || voaResult.IsCancelled)
            throw new InvalidOperationException($"CVE-VOA failed: {voaResult.Payload}");

        await _host.ProcessAsync("CVE-VOD-V1.0", new AifMessage
        {
            MessageId   = voaResult.MessageId,
            MessageType = "DisplayImage",
            Ports       = new Dictionary<string, string>
            {
                ["InputVisual"] = voaResult.Payload
            }
        });

        return MpaiJson.FromJson<BasicVisualObject>(voaResult.Payload);
    }

    public async Task SpeakAsync(string text)
    {
        var ttsResult = await _host.ProcessAsync("MMC-TTS-V2.5", new AifMessage
        {
            MessageId   = Guid.NewGuid().ToString(),
            MessageType = "Speak",
            Ports       = new Dictionary<string, string>
            {
                ["InputText"] = MpaiJson.ToJson(BasicTextObject.FromText(text))
            }
        });

        if (ttsResult.IsError || ttsResult.IsCancelled) return;

        await _host.ProcessAsync("CAE-AOD-V1.0", new AifMessage
        {
            MessageId   = ttsResult.MessageId,
            MessageType = "Deliver",
            Ports       = new Dictionary<string, string>
            {
                ["InputAudio"] = ttsResult.Payload
            }
        });
    }

    public void StartListening()
    {
        _aoaContext = _host.StartAim("CAE-AOA-V1.0");
        _aoaTask    = _host.ProcessWithContextAsync(
            "CAE-AOA-V1.0",
            new AifMessage
            {
                MessageId   = Guid.NewGuid().ToString(),
                MessageType = "Record",
                Ports       = new Dictionary<string, string>()
            },
            _aoaContext);
    }

    public async Task<AmqResult> StopAndAnswerAsync(BasicVisualObject image)
    {
        if (_aoaTask is null)
            throw new InvalidOperationException("StartListening was not called.");

        _host.StopAim("CAE-AOA-V1.0");
        var aoaResult = await _aoaTask;
        _aoaTask      = null;

        if (aoaResult.IsError || aoaResult.IsCancelled)
            throw new InvalidOperationException($"AOA failed: {aoaResult.Payload}");

        var asrResult = await _host.ProcessAsync("MMC-ASR-V2.5", new AifMessage
        {
            MessageId   = Guid.NewGuid().ToString(),
            MessageType = "ASR",
            Ports       = new Dictionary<string, string>
            {
                ["InputAudio"] = aoaResult.Payload
            }
        });
        if (asrResult.IsError || asrResult.IsCancelled)
            throw new InvalidOperationException($"ASR failed: {asrResult.Payload}");

        var tiqResult = await _host.ProcessAsync("MMC-TIQ-V2.5", new AifMessage
        {
            MessageId   = asrResult.MessageId,
            MessageType = "TIQ",
            Ports       = new Dictionary<string, string>
            {
                ["InputText"]   = asrResult.Payload,
                ["InputVisual"] = MpaiJson.ToJson(image)
            }
        });
        if (tiqResult.IsError || tiqResult.IsCancelled)
            throw new InvalidOperationException($"TIQ failed: {tiqResult.Payload}");

        var ttsResult = await _host.ProcessAsync("MMC-TTS-V2.5", new AifMessage
        {
            MessageId   = tiqResult.MessageId,
            MessageType = "TTS",
            Ports       = new Dictionary<string, string>
            {
                ["InputText"] = tiqResult.Payload
            }
        });

        if (!ttsResult.IsError && !ttsResult.IsCancelled)
            await _host.ProcessAsync("CAE-AOD-V1.0", new AifMessage
            {
                MessageId   = ttsResult.MessageId,
                MessageType = "AOD",
                Ports       = new Dictionary<string, string>
                {
                    ["InputAudio"] = ttsResult.Payload
                }
            });

        return new AmqResult
        {
            Image        = image,
            Question     = MpaiJson.FromJson<BasicTextObject>(asrResult.Payload),
            Answer       = MpaiJson.FromJson<BasicTextObject>(tiqResult.Payload),
            SpeechAnswer = MpaiJson.FromJson<BasicSpeechObject>(ttsResult.Payload)
        };
    }
}
