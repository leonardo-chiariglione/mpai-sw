using System;

using AIF.Controller;
using AIF.Store;

using System.Collections.Generic;

using Mpai.Aims.Asr;
using Mpai.Aims.Audio;    // the DEVICES: a microphone yields audio, not speech
using Mpai.Aims.Speech;   // MMC-SOA and MMC-SOD: Speech Object acquisition and
                          // delivery, which is what the devices are used FOR
using Mpai.Aims.Tts;
using Mpai.Aims.Ttt;
using Mpai.Core;          // IAudioAcquisitionAim and IAudioDeliveryAim: the
                          // INTERFACES sit with the data types, the
                          // IMPLEMENTATIONS with the AIMs

namespace TstUi;

// Builds the AIMs of MMC-TST-V2.5 and of UAG-SPK-V1.0.
//
// The AUDIO DEVICES are the only part that is not portable. The Windows
// acquisition and delivery AIMs use NAudio and winmm and target net10.0-windows;
// the Linux ones shell out to arecord and aplay. Both sit behind
// IAudioAcquisitionAim and IAudioDeliveryAim, so this is the only file that has
// to know which platform it is on - which is the point of putting devices in
// edge AIMs in the first place.
internal sealed class TstProvider : IAimProvider
{
    private readonly AmdStore _store;

    public TstProvider(AmdStore store) => _store = store;

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings) =>
        aimName switch
        {
            "MMC-SOA-V2.5" =>
                new SoaAimProcessor(
                    aimName,
                    Microphone(),
                    AimPortReader.Load(_store, aimName),
                    TimeSpan.FromSeconds(Number(settings, "DurationSeconds", 15))),

            "MMC-ASR-V2.5" =>
                new AsrAimProcessor(aimName, AsrFactory.Create(settings), AimPortReader.Load(_store, aimName)),

            "MMC-TTT-V2.5" =>
                new TttAimProcessor(aimName, TttFactory.Create(settings), AimPortReader.Load(_store, aimName)),

            "MMC-TTS-V2.5" =>
                new TtsAimProcessor(aimName, TtsFactory.Create(settings), AimPortReader.Load(_store, aimName)),

            "MMC-SOD-V2.5" =>
                new SodAimProcessor(aimName, Loudspeaker(), AimPortReader.Load(_store, aimName)),

            _ => throw new NotSupportedException($"No implementation available for {aimName}.")
        };

    // Recording runs until the user presses Stop - the Pause signal - so the
    // duration is a ceiling, not the interaction. Fifteen seconds is a generous
    // ceiling for a sentence.
    private static IAudioAcquisitionAim Microphone()
    {
#if WINDOWS_DEVICES
        return new WasapiAudioAcquisition();
#else
        return new AlsaAudioAcquisition();
#endif
    }

    private static IAudioDeliveryAim Loudspeaker()
    {
#if WINDOWS_DEVICES
        return new WinmmAudioDelivery();
#else
        return new AplayAudioDelivery();
#endif
    }

    private static int Number(IReadOnlyDictionary<string, string> settings, string key, int fallback) =>
        settings.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : fallback;
}