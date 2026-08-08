using System;
using System.Collections.Generic;

using Mmc.Tts;
using Mmc.Tts.Piper;

namespace Mpai.Aims.Tts;

// Builds the Piper-backed TTS AIM from deployment settings.
//
// Settings: PiperExecutable, VoiceModel, VoiceConfig
public static class TtsFactory
{
    public static PiperTtsAim Create(
        IReadOnlyDictionary<string, string> settings)
    {
        var runner =
            new PiperProcessRunner(
                new PiperConfiguration
                {
                    ExecutablePath = Setting(settings, "PiperExecutable")
                });

        var configuration =
            new MpaiTtsV1Configuration
            {
                ModelPath = Setting(settings, "VoiceModel"),
                ConfigPath = Setting(settings, "VoiceConfig")
            };

        var mpaiTts =
            new MpaiTtsV1(
                runner,
                new SpeechObjectBuilder(),
                configuration);

        var voice =
            PiperVoiceProfile.Load(
                configuration.ConfigPath);

        return new PiperTtsAim(
            mpaiTts,
            voice);
    }

    private static string Setting(
        IReadOnlyDictionary<string, string> settings,
        string key)
    {
        if (!settings.TryGetValue(key, out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"MMC-TTS-V2.5 setting '{key}' is missing.");
        }

        return value;
    }
}
