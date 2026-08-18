using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Aims.Asr;
using Mpai.Aims.Audio;    // the DEVICES: arecord and aplay
using Mpai.Aims.Speech;   // MMC-SOA and MMC-SOD
using Mpai.Aims.Tts;
using Mpai.Aims.Ttt;
using Mpai.Core;

namespace Tst.Linux.Host;

// The AIMs of MMC-TST-V2.5 and UAG-SPK-V1.0, wired to the Linux devices.
//
// Nothing here is Linux-specific except the two device choices - arecord and
// aplay in place of WASAPI and winmm. That is the whole of the platform
// difference, because acquisition and delivery are SubAIMs: everything between
// them is the same code running the same Topology.
internal sealed class TstLinuxProvider : IAimProvider
{
    private readonly AmdStore _store;
    private readonly bool     _headless;

    public TstLinuxProvider(AmdStore store, bool headless)
    {
        _store    = store;
        _headless = headless;
    }

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings) =>
        aimName switch
        {
            "MMC-SOA-V2.5" =>
                new SoaAimProcessor(
                    aimName,
                    _headless
                        ? new FileAudioAcquisition(Setting(settings, "QuestionAudio", string.Empty))
                        : new AlsaAudioAcquisition(),
                    _store,
                    TimeSpan.FromSeconds(Number(settings, "DurationSeconds", 15))),

            "MMC-ASR-V2.5" => new AsrAimProcessor(aimName, AsrFactory.Create(settings), _store),
            "MMC-TTT-V2.5" => new TttAimProcessor(aimName, TttFactory.Create(settings), _store),
            "MMC-TTS-V2.5" => new TtsAimProcessor(aimName, TtsFactory.Create(settings), _store),

            "MMC-SOD-V2.5" =>
                new SodAimProcessor(
                    aimName,
                    _headless
                        ? new FileAudioDelivery(Setting(settings, "OutputFolder", "/tmp"))
                        : new AplayAudioDelivery(),
                    _store),

            _ => throw new NotSupportedException($"No implementation available for {aimName}.")
        };

    private static string Setting(IReadOnlyDictionary<string, string> settings, string key, string fallback) =>
        settings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static int Number(IReadOnlyDictionary<string, string> settings, string key, int fallback) =>
        settings.TryGetValue(key, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;
}