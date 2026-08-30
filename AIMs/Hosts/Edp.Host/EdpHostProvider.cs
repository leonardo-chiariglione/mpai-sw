using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Mmc.Edp;   // EdpAimProcessor, OllamaClient

namespace Edp.Host;

// Composition root for the EDP test host. Provides Entity Dialogue Processing,
// sharing one Ollama client (the local LLM engine). The model name is taken from
// the "OllamaModel" setting, defaulting to llama3.1.
public sealed class EdpHostProvider : IAimProvider, IDisposable
{
    private readonly AmdStore _store;
    private OllamaClient? _llm;

    public EdpHostProvider(AmdStore store) => _store = store;

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings)
        => aimName switch
        {
            "MMC-EDP-V2.5" => new EdpAimProcessor(aimName, Llm(settings), AimPortReader.Load(_store, aimName)),
            _ => throw new NotSupportedException($"EdpHostProvider does not provide '{aimName}'.")
        };

    private OllamaClient Llm(IReadOnlyDictionary<string, string> settings)
    {
        if (_llm is not null) return _llm;
        string model = settings.TryGetValue("OllamaModel", out var m) && !string.IsNullOrWhiteSpace(m) ? m : "llama3.1";
        return _llm = new OllamaClient(model);
    }

    public void Dispose() => _llm?.Dispose();
}
