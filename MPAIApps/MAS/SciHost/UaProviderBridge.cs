using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;

using Mpai.Aims.Asr;
using Mpai.Aims.Audio;
using Mpai.Aims.Speech;
using Mpai.Aims.Tiq;
using Mpai.Aims.Tts;
using Mpai.Aims.Ttt;
using Mpai.Aims.Visual;

namespace Mpai.Mas.Sci;

// SCI composition root: the same AMQ SubAIM factory as the UI's provider, with
// the heavy BLIP model (and ASR/TTS cores) cached so fresh AIWs reuse them.
// (A shared library would avoid duplicating this; copied here to keep the demo
// self-contained and the working UI project untouched.)
public sealed class UaProviderBridge : IAimProvider
{
    private readonly AmdStore _store;
    private readonly string   _outputFolder;

    private ITiqAim?       _tiq;
    private WhisperAsrAim? _asr;
    private PiperTtsAim?   _tts;
    // Translation is the heaviest of the lot - an encoder and two decoder
    // sessions, close to a gigabyte - so it is cached like the others.
    // Without this every fresh AIW would reload M2M-100 from disk.
    private ITttAim?       _ttt;

    public UaProviderBridge(AmdStore store, string outputFolder)
    {
        _store        = store;
        _outputFolder = outputFolder;
    }

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings) =>
        aimName switch
        {
            "CVE-VOA-V1.0" => new VoaAimProcessor(aimName, new FileVisualAcquisition(string.Empty), AimPortReader.Load(_store, aimName), string.Empty),
            "CAE-AOA-V1.0" => new AoaAimProcessor(aimName, new FileAudioAcquisition(string.Empty), AimPortReader.Load(_store, aimName), TimeSpan.FromSeconds(5)),
            "MMC-ASR-V2.5" => new AsrAimProcessor(aimName, _asr ??= AsrFactory.Create(settings), AimPortReader.Load(_store, aimName)),
            "MMC-TIQ-V2.5" => new TiqAimProcessor(aimName, _tiq ??= TiqFactory.Create(settings), AimPortReader.Load(_store, aimName)),
            "MMC-TTS-V2.5" => new TtsAimProcessor(aimName, _tts ??= TtsFactory.Create(settings), AimPortReader.Load(_store, aimName)),
            "MMC-SOA-V2.5" => new SoaAimProcessor(aimName, new FileAudioAcquisition(string.Empty), AimPortReader.Load(_store, aimName), TimeSpan.FromSeconds(5)),
            "MMC-TTT-V2.5" => new TttAimProcessor(aimName, _ttt ??= TttFactory.Create(settings), AimPortReader.Load(_store, aimName)),
            "MMC-SOD-V2.5" => new SodAimProcessor(aimName, new FileAudioDelivery(_outputFolder), AimPortReader.Load(_store, aimName)),
            "CAE-AOD-V1.0" => new AodAimProcessor(aimName, new FileAudioDelivery(_outputFolder), AimPortReader.Load(_store, aimName)),
            "CVE-VOD-V1.0" => new VodAimProcessor(aimName, new FileVisualDelivery(_outputFolder), AimPortReader.Load(_store, aimName)),
            _ => throw new NotSupportedException($"No implementation for {aimName}.")
        };
}
