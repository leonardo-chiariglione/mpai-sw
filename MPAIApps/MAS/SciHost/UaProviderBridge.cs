using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;

using Mpai.Aims.Asr;
using Mpai.Aims.Audio;
using Mpai.Aims.Tiq;
using Mpai.Aims.Tts;
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

    public UaProviderBridge(AmdStore store, string outputFolder)
    {
        _store        = store;
        _outputFolder = outputFolder;
    }

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings) =>
        aimName switch
        {
            "CVE-VOA-V1.0" => new VoaAimProcessor(aimName, new FileVisualAcquisition(string.Empty), _store, string.Empty),
            "CAE-AOA-V1.0" => new AoaAimProcessor(aimName, new FileAudioAcquisition(string.Empty), _store, TimeSpan.FromSeconds(5)),
            "MMC-ASR-V2.5" => new AsrAimProcessor(aimName, _asr ??= AsrFactory.Create(settings), _store),
            "MMC-TIQ-V2.5" => new TiqAimProcessor(aimName, _tiq ??= TiqFactory.Create(settings), _store),
            "MMC-TTS-V2.5" => new TtsAimProcessor(aimName, _tts ??= TtsFactory.Create(settings), _store),
            "CAE-AOD-V1.0" => new AodAimProcessor(aimName, new FileAudioDelivery(_outputFolder), _store),
            "CVE-VOD-V1.0" => new VodAimProcessor(aimName, new FileVisualDelivery(_outputFolder), _store),
            _ => throw new NotSupportedException($"No implementation for {aimName}.")
        };
}
