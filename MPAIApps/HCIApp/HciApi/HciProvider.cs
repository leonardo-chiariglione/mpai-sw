using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Mmc.Edp;   // EdpAimProcessor, OllamaClient
using Mpai.Paf.Psd;   // PsdAimProcessor
using Mpai.Paf.Gfd;   // GfdAimProcessor
using Mpai.Aims.Tts;  // TtsAimProcessor, TtsFactory

namespace Mpai.Hci.Api;

// Composition root for the HCI middleware Modules the API faÃ§ade runs: Entity
// Dialogue Processing (the local LLM, for SubmitDialogueIntent) and the Response
// and Scene Rendering SubAIMs - Personal Status De-multiplexing, Text-To-Speech,
// Generative Face Description (for ReceiveSpeakingAvatar). One Ollama client is
// shared; the model is the "OllamaModel" setting (default llama3.1).
public sealed class HciProvider : IAimProvider, IDisposable
{
    private readonly AmdStore _store;
    private OllamaClient?     _llm;

    public HciProvider(AmdStore store) => _store = store;

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings)
        => aimName switch
        {
            "MMC-EDP-V2.5" => new EdpAimProcessor(aimName, Llm(settings), AimPortReader.Load(_store, aimName)),
            "PAF-PSD-V1.6" => new PsdAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            "MMC-TTS-V2.5" => new TtsAimProcessor(aimName, TtsFactory.Create(settings), AimPortReader.Load(_store, aimName)),
            "PAF-GFD-V1.6" => new GfdAimProcessor(aimName, AimPortReader.Load(_store, aimName)),
            _ => throw new NotSupportedException($"HciProvider does not provide '{aimName}'.")
        };

    private OllamaClient Llm(IReadOnlyDictionary<string, string> settings)
    {
        if (_llm is not null) return _llm;
        string model = settings.TryGetValue("OllamaModel", out var m) && !string.IsNullOrWhiteSpace(m) ? m : "llama3.1";
        return _llm = new OllamaClient(model);
    }

    public void Dispose() => _llm?.Dispose();
}
