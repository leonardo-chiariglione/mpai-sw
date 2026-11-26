using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Aims.Asr;          // AsrAimProcessor, AsrFactory
using Mpai.Mmc.Nlu;           // NluAimProcessor
using Mpai.Mmc.Qam;           // QamAimProcessor
using Mpai.Mmc.Aqm;           // AqmPlugin
using Mpai.Osd.Vii;           // ViiAimProcessor
using Mpai.Osd.VisualScene;   // YoloxObjectDetector

namespace Mpai.Providers;

// Leaf provider for the MMC-MQA-V2.5 Module (Multimodal Question Answering, 2026-11-25, N-50 e). An App of its own - not CPS, not AMQ: the Controller builds the
// composite from its L3; this provider supplies the leaf AIMs its Topology names:
//   CVE-VII - Visual Instance Identification (YOLOX): what the object shown is
//   MMC-ASR - speech to text (Whisper), for a spoken question
//   MMC-NLU - the Refined Text, the Text Descriptors Object, the Instance Identifier passed on
//   MMC-QAM - Question Analysis: the Intention
//   MMC-AQM - Answer to Question: the Machine Text (a language model through Ollama)
//   PAF-PMX - Portable Avatar Multiplexing: the answer in the machine's Portable Avatar
// The picture, the microphone and the avatar are the User Agent's.
public sealed class MqaProvider : IAimProvider, IDisposable
{
    private readonly AmdStore _store;
    private YoloxObjectDetector? _yolox;

    public MqaProvider(AmdStore store) => _store = store;

    public string? ImplementationOf(string aimName) => CanCreate(aimName) ? AimBinaries.Of(aimName) : null;

    public bool CanCreate(string aimName) =>
        aimName is "1CVE-VII-V1.0-I01" or "1MMC-ASR-V2.5-I01" or "1MMC-NLU-V2.5-I01" or "1MMC-QAM-V2.5-I01" or "1MMC-AQM-V2.5-I01" or "1PAF-PMX-V1.6-I01";

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings, AIF.SharedStorage.ISharedStorage? storage)
        => aimName switch
        {
            "1CVE-VII-V1.0-I01" => new ViiAimProcessor(aimName, _yolox ??= new YoloxObjectDetector(MpaiPaths.Given(settings, "YoloxModel")), AimPortReader.Load(_store, aimName)),
            "1MMC-ASR-V2.5-I01" => new AsrAimProcessor(aimName, AsrFactory.Create(settings), AimPortReader.Load(_store, aimName)),
            "1MMC-NLU-V2.5-I01" => new NluAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            "1MMC-QAM-V2.5-I01" => new QamAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            "1MMC-AQM-V2.5-I01" => Aqm(aimName, settings),
            "1PAF-PMX-V1.6-I01" => new Mpai.Paf.Pmx.PmxAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            _ => throw new NotSupportedException($"MqaProvider does not provide '{aimName}'.")
        };

    private IAimProcessor Aqm(string aimName, IReadOnlyDictionary<string, string> settings)
    {
        var model = settings.TryGetValue("OllamaModel", out var m) && !string.IsNullOrWhiteSpace(m) ? m : "llama3.2:3b";
        var client = settings.TryGetValue("OllamaUrl", out var url) && !string.IsNullOrWhiteSpace(url) ? new Mpai.Mmc.Edp.OllamaClient(model, url) : new Mpai.Mmc.Edp.OllamaClient(model);
        return new AqmAimProcessor(aimName, AimPortReader.Load(_store, aimName), (system, user) => client.ChatAsync(system, user, 0.2));
    }

    public void Dispose() => _yolox?.Dispose();
}
