using System;
using System.Collections.Generic;
using AIF.Controller;
using AIF.Store;
using Mpai.Core;
using Mpai.Paf.Psd;      // PsdAimProcessor
using Mpai.Paf.Gfd;      // GfdAimProcessor (Generative Face Description)
using Mpai.Aims.Tts;     // TtsAimProcessor, TtsFactory
using Mpai.Aims.Speech;  // SodAimProcessor, ISpeechDeliveryAim, WinmmSpeechDelivery
namespace Rsr.Host;
// Composition root for the RSR (Response and Scene Rendering) test host. Builds the
// RSR composite's SubAIMs - Personal Status De-multiplexing (PAF-PSD), Text-To-Speech
// (MMC-TTS), Generative Face Description (PAF-GFD) - which PRODUCE the Machine Speech
// and the Machine Face Descriptors (the facial animation timeline, with lip-sync).
// RSR produces; the User Agent delivers. This host also builds Speech Object Delivery
// (MMC-SOD) so it can speak the produced speech aloud (the UA's delivery role).
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
            "PAF-GFD-V1.6" =>
                new GfdAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            "MMC-SOD-V2.5" =>
                new SodAimProcessor(aimName, Loudspeaker(), AimPortReader.Load(_store, aimName)),
            _ => throw new NotSupportedException($"RsrHostProvider does not provide '{aimName}'.")
        };
    private static ISpeechDeliveryAim Loudspeaker()
    {
#if WINDOWS_DEVICES
        return new WinmmSpeechDelivery();
#else
        throw new System.PlatformNotSupportedException("No non-Windows speech delivery device is configured.");
#endif
    }
    public void Dispose() { }
}
