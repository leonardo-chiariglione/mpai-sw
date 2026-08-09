using System.Collections.Generic;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;

namespace Mpai.Aims.Audio;

// MMC-SOA-V2.5 — Speech Object Acquisition. Self-contained IAimProcessor.
// Reads its own port names from 1MMC-SOA-V2.5-I01.json at startup.
//
// The physical acquisition is identical to AOA (capturing sound waves is
// acoustic regardless of content); SOA differs only in that it produces a
// Basic SPEECH Object (OSD-SPO-V1.5) rather than a Basic Audio Object. It
// therefore reuses the same IAudioAcquisitionAim device capture and re-wraps
// the captured bytes as speech.
//
// Zero-trust input handling mirrors AOA: if a Speech Object was piped to the
// input port, SOA uses it; otherwise it acquires from the device.
public sealed class SoaAimProcessor : IAimProcessor
{
    private readonly string                 _inputPort;
    private readonly string                 _outputPort;
    private readonly IAudioAcquisitionAim   _aoa;
    private readonly IStartStopAcquisition? _startStop;
    private readonly System.TimeSpan        _duration;

    public string InstanceId { get; }

    public SoaAimProcessor(
        string               instanceId,
        IAudioAcquisitionAim aoa,
        AmdStore             store,
        System.TimeSpan?     duration = null)
    {
        InstanceId  = instanceId;
        _aoa        = aoa;
        _startStop  = aoa as IStartStopAcquisition;
        _duration   = duration ?? System.TimeSpan.FromSeconds(5);
        var ports   = AimPortReader.Load(store, instanceId);
        _inputPort  = ports.InputOrDefault("OSD-SPO-V1.5", "InputSpeech");
        _outputPort = ports.Output("OSD-SPO-V1.5");
    }

    public async Task<Message> ProcessAsync(Message message)
    {
        // Use a piped speech input if the Controller delivered one.
        if (message.Ports.TryGetValue(_inputPort, out var suppliedJson) &&
            !string.IsNullOrWhiteSpace(suppliedJson))
        {
            return new Message
            {
                MessageId   = message.MessageId,
                MessageType = "BasicSpeechObject",
                DataType    = "OSD-SPO-V1.5",
                Payload     = suppliedJson,
                Ports       = new Dictionary<string, string> { [_outputPort] = suppliedJson }
            };
        }

        // No input delivered — acquire fresh from the device (same as AOA).
        var context = message.Context;
        BasicAudioObject audio;

        if (_startStop is not null &&
            context.StopToken != System.Threading.CancellationToken.None)
        {
            _startStop.StartAcquire();
            try
            {
                await Task.Delay(System.Threading.Timeout.Infinite, context.StopToken);
            }
            catch (System.OperationCanceledException) { /* StopToken fired - expected */ }
            audio = await _startStop.StopAcquireAsync();
        }
        else
        {
            audio = await _aoa.AcquireAsync(new AcquisitionRequest { Duration = _duration });
        }

        // Re-interpret the captured audio as a Basic Speech Object (OSD-SPO).
        var speech = audio.AsSpeech();
        var json   = MpaiJson.ToJson(speech);

        return new Message
        {
            MessageId   = message.MessageId,
            MessageType = "BasicSpeechObject",
            DataType    = "OSD-SPO-V1.5",
            Payload     = json,
            Ports       = new Dictionary<string, string> { [_outputPort] = json }
        };
    }
}
