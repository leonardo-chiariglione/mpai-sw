using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Paf.Psd;      // PsdAimProcessor
using Mpai.Aims.Tts;     // TtsAimProcessor, TtsFactory
using Mpai.Aims.Speech;  // SodAimProcessor, ISpeechDeliveryAim, WinmmSpeechDelivery

namespace Rsr.Host;

// Composition root for the RSR (Response and Scene Rendering) speech path. Builds
// Personal Status De-multiplexing (PAF-PSD), Text-To-Speech (MMC-TTS), and Speech
// Object Delivery (MMC-SOD -> loudspeaker) - the same TTS/SOD/loudspeaker chain
// CAV-MAC uses to speak, plus PAF-PSD to de-multiplex the machine's Personal Status.
// The machine speaks its Entity Dialogue Processing response aloud.
internal sealed class RsrHostProvider : IAimProvider, IDisposable
{
    private readonly AmdStore _store;

    public RsrHostProvider(AmdStore store) => _store = store;

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings)
        => aimName switch
        {
            "PAF-PSD-V1.6" =>
                new PsdAimProcessor(aimName, AimPortReader.Load(_store, aimName)),

            "MMC-TTS-V2.5" =>
                new TtsAimProcessor(aimName, TtsFactory.Create(settings), AimPortReader.Load(_store, aimName)),

            "MMC-SOD-V2.5" =>
                new SodAimProcessor(aimName, Loudspeaker(), AimPortReader.Load(_store, aimName)),

            _ => throw new NotSupportedException($"RsrHostProvider does not provide '{aimName}'.")
        };

    private static ISpeechDeliveryAim Loudspeaker()
    {
        // Speech Object Delivery has its own device (independent of Audio Object
        // Delivery). On Windows this is the winmm-backed speech delivery.
#if WINDOWS_DEVICES
        return new WinmmSpeechDelivery();
#else
        throw new System.PlatformNotSupportedException("No non-Windows speech delivery device is configured.");
#endif
    }

    public void Dispose() { }
}
