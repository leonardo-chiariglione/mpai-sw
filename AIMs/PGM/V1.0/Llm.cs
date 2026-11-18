using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Mpai.Pgm;

// The language model BKN asks. The AIM knows only this contract; which model answers is configuration (settings "Llm.Model", "Llm.Url").
public interface ILanguageModel
{
    // The answer to the system and user messages, as a JSON text that complies with the schema.
    Task<string> AnswerAsync(string system, string user, JsonObject schema);
}

// Ollama, over its HTTP interface. The model name is a setting; nothing here names one.
public sealed class OllamaModel : ILanguageModel
{
    public const string ModelSetting = "Llm.Model", UrlSetting = "Llm.Url";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly string _url;
    private string _model;
    private bool _named;

    public OllamaModel(string model, string url = "http://localhost:11434")
    {
        _model = model;
        _url = url.TrimEnd('/');
    }

    public string Model => _model;

    // How long Ollama keeps the model loaded after an answer (its "keep_alive", e.g. "30m"); null: Ollama's default (5 minutes). A demonstration that waits between commands sets it.
    public string? KeepAlive { get; init; }

    public static OllamaModel From(IReadOnlyDictionary<string, string> settings) =>
        new(settings.TryGetValue(ModelSetting, out var m) && m.Length > 0 ? m : throw new InvalidOperationException($"BKN needs the setting '{ModelSetting}'."),
            settings.TryGetValue(UrlSetting, out var u) && u.Length > 0 ? u : "http://localhost:11434");

    // ONE MODEL, ONE NAME (2026-11-18), as the dialogue's client does it (Mpai.Mmc.Edp.OllamaClient.OneNameOf): "llama3.2:3b" is the same Llama 3.2 3B as the model created from
    // Access for the dialogue (mpai-..., named from the URI of its weights); Ollama keeps a copy for each name, and two did not fit on the 4 GB card. When Ollama has the model
    // from Access, it is the one used.
    private const string LlamaWeights = "https://registry.ollama.ai/v2/library/llama3.2/blobs/sha256:dde5aa3fc5ffc17176b5e8bdc82f587b24b2678c6c66101bf7da77af9f7ccdff";
    private async Task NameAsync()
    {
        if (_named) return;
        _named = true;
        if (_model is not ("llama3.2:3b" or "llama3.2" or "llama3.2:latest")) return;
        var one = "mpai-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(LlamaWeights)))[..12].ToLowerInvariant();
        try
        {
            using var show = await Http.PostAsync(_url + "/api/show", new StringContent(new JsonObject { ["model"] = one }.ToJsonString(), Encoding.UTF8, "application/json"));
            if (show.IsSuccessStatusCode) _model = one;
        }
        catch (HttpRequestException) { }
    }

    public async Task<string> AnswerAsync(string system, string user, JsonObject schema)
    {
        await NameAsync();
        var body = new JsonObject
        {
            ["model"] = _model,
            ["stream"] = false,
            ["format"] = schema.DeepClone(),
            ["options"] = new JsonObject { ["temperature"] = 0 },
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user })
        };
        if (KeepAlive is not null) body["keep_alive"] = KeepAlive;
        using var response = await Http.PostAsync(_url + "/api/chat", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Ollama answered {(int)response.StatusCode}: {text}");
        return (string?)JsonNode.Parse(text)?["message"]?["content"] ?? throw new InvalidOperationException("Ollama gave no content.");
    }
}
