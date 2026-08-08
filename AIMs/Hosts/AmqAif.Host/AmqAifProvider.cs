using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Aims.Asr;
using Mpai.Aims.Audio;
using Mpai.Aims.Ocr;
using Mpai.Aims.Tiq;
using Mpai.Aims.Tts;
using Mpai.Aims.Visual;

namespace AmqAif.Host;

// Composition root for the headless AIF host.
// The ONLY place that knows both the framework and the concrete AIMs.
// Uses self-contained *AimProcessor classes - no adapters.
// Each processor reads its own port names from its instance JSON via AmdStore.
public sealed class AmqAifProvider : IAimProvider
{
    private readonly bool     _headless;
    private readonly AmdStore _store;

    public AmqAifProvider(AmdStore store, bool headless = false)
    {
        _store    = store;
        _headless = headless;
    }

    public IAimProcessor Create(
        string aimName,
        IReadOnlyDictionary<string, string> settings)
    {
        return aimName switch
        {
            "CVE-VOA-V1.0" =>
                new VoaAimProcessor(
                    aimName,
                    _headless
                        ? new FileVisualAcquisition(
                              Setting(settings, "ImageFile", @"D:\AI\Images\zebra.jpg"))
                        : new WinFormsVisualAcquisition(),
                    _store,
                    Setting(settings, "SourceHint", @"D:\")),

            "CAE-AOA-V1.0" =>
                new AoaAimProcessor(
                    aimName,
                    _headless
                        ? new FileAudioAcquisition(
                              Setting(settings, "QuestionAudio", @"D:\AI\Audio\question.wav"))
                        : new WasapiAudioAcquisition(),
                    _store,
                    TimeSpan.FromSeconds(Number(settings, "DurationSeconds", 5))),

            "MMC-ASR-V2.5" =>
                new AsrAimProcessor(
                    aimName,
                    AsrFactory.Create(settings),
                    _store),

            "MMC-OCR-V2.5" =>
                new OcrAimProcessor(
                    aimName,
                    OcrFactory.Create(settings),
                    _store),

            "MMC-TIQ-V2.5" =>
                new TiqAimProcessor(
                    aimName,
                    TiqFactory.Create(settings),
                    _store),

            "MMC-TTS-V2.5" =>
                new TtsAimProcessor(
                    aimName,
                    TtsFactory.Create(settings),
                    _store),

            "CAE-AOD-V1.0" =>
                new AodAimProcessor(
                    aimName,
                    _headless
                        ? new FileAudioDelivery(
                              Setting(settings, "OutputFolder", @"D:\AI\Output"))
                        : new WinmmAudioDelivery(),
                    _store),

            "CVE-VOD-V1.0" =>
                new VodAimProcessor(
                    aimName,
                    new FileVisualDelivery(
                        Setting(settings, "OutputFolder", @"D:\AI\Output")),
                    _store),

            _ => throw new NotSupportedException(
                     $"No implementation available for {aimName}.")
        };
    }

    private static string Setting(
        IReadOnlyDictionary<string, string> settings,
        string key, string fallback) =>
        settings.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    private static double Number(
        IReadOnlyDictionary<string, string> settings,
        string key, double fallback) =>
        settings.TryGetValue(key, out var v) &&
        double.TryParse(v,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : fallback;
}
