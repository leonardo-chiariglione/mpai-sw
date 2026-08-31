using System;
using System.Collections.Generic;
using AIF.Controller;
using AIF.Store;
using Mpai.Paf.Psd;   // PsdAimProcessor
using Mpai.Paf.Gfd;   // GfdAimProcessor
using Mpai.Aims.Tts;  // TtsAimProcessor, TtsFactory

namespace CavApp;

// Composition root for the In-Cabin CAV app: provides the RSR Module's SubAIMs -
// Personal Status De-multiplexing, Text-To-Speech, Generative Face Description.
// RSR PRODUCES the Machine Speech + the Machine Face Descriptors; delivery (to the
// WebView renderer) is done by the app via the 3OD device.
internal sealed class CavProvider : IAimProvider
{
    private readonly AmdStore _store;
    public CavProvider(AmdStore store) => _store = store;

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings)
        => aimName switch
        {
            "PAF-PSD-V1.6" => new PsdAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            "MMC-TTS-V2.5" => new TtsAimProcessor(aimName, TtsFactory.Create(settings), AimPortReader.Load(_store, aimName)),
            "PAF-GFD-V1.6" => new GfdAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            _ => throw new NotSupportedException($"CavProvider does not provide '{aimName}'.")
        };
}
