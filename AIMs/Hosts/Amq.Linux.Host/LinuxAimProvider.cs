using System;
using System.Collections.Generic;

using AIF.Controller;

using Mpai.Aims.Asr;
using Mpai.Aims.Audio;
using Mpai.Aims.Tiq;
using Mpai.Aims.Tts;
using Mpai.Aims.Visual;

namespace Amq.Linux.Host;

// Composition root for Linux.
//
// Every AIM here is platform-neutral. Two deployments are offered:
//
//   files   (default) an image file in, a question WAV in, answers written out.
//                     No devices at all: the safest first run on a new machine.
//   devices           ALSA capture (arecord) and playback (aplay).
//
// Nothing in the AIMs differs between Linux and Windows; only this choice does.
public sealed class LinuxAimProvider
    : IAimProvider
{
    private readonly bool useDevices;

    public LinuxAimProvider(
        bool useDevices = false)
    {
        this.useDevices = useDevices;
    }

    public IAimProcessor Create(
        string aimName,
        IReadOnlyDictionary<string, string> settings)
    {
        return aimName switch
        {
            "CVE-VOA-V1.0" =>
                new VoaAimAdapter(
                    aimName,
                    new FileVisualAcquisition(
                        Setting(settings, "ImageFile", "/home/you/images/zebra.jpg")),
                    Setting(settings, "ImageFile", "/home/you/images/zebra.jpg")),

            "CAE-AOA-V1.0" =>
                new AoaAimAdapter(
                    aimName,
                    useDevices
                    ? new AlsaAudioAcquisition()
                    : new FileAudioAcquisition(
                          Setting(settings, "QuestionAudio", "/home/you/audio/question.wav")),
                    TimeSpan.FromSeconds(
                        Number(settings, "DurationSeconds", 5))),

            "MMC-ASR-V2.5" =>
                new AsrAimAdapter(
                    aimName,
                    AsrFactory.Create(settings)),

            "MMC-TIQ-V2.5" =>
                new TiqAimAdapter(
                    aimName,
                    TiqFactory.Create(settings)),

            "MMC-TTS-V2.5" =>
                new TtsAimAdapter(
                    aimName,
                    TtsFactory.Create(settings)),

            "CAE-AOD-V1.0" =>
                new AodAimAdapter(
                    aimName,
                    useDevices
                    ? new AplayAudioDelivery()
                    : new FileAudioDelivery(
                          Setting(settings, "OutputFolder", "/home/you/output"))),

            _ => throw new NotSupportedException(
                     $"No implementation available for {aimName}.")
        };
    }

    private static string Setting(
        IReadOnlyDictionary<string, string> settings,
        string key,
        string fallback)
    {
        return settings.TryGetValue(key, out var value) &&
               !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
    }

    private static double Number(
        IReadOnlyDictionary<string, string> settings,
        string key,
        double fallback)
    {
        return settings.TryGetValue(key, out var value) &&
               double.TryParse(
                   value,
                   System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out var number)
            ? number
            : fallback;
    }
}

