// NOTICE (third-party models and data). This source file is released under the BSD 3-Clause licence of this repository.
// The models and the databases it uses, and the data derived from them, are not part of it: each has its own licence, and some
// restrict use (for example to non-commercial research). Read THIRD-PARTY-NOTICES.md and the Terms of the AccessSources of the AIM.
using AIF.Controller;
using System.Security.Cryptography;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Mpai.Mmc.Edp;

// MMC-SUM-V2.5 - Summary. The running dialogue memory: a plain-text running summary
// of the conversation (carried in SummaryData), which EDP reads in and writes back
// updated. Mirrors schemas/MMC/V2.5/data/Summary.json (the full schema also carries
// structured SummaryElements; this implementation uses the plain-text SummaryData
// for the LLM's working memory).
public sealed class Summary
{
    public string Header { get; init; } = "MMC-SUM-V2.5";
    public string SummaryID { get; init; } = Guid.NewGuid().ToString();
    public List<SummaryDataItem> SummaryData { get; init; } = new();

    public string Text()
        => SummaryData.Count > 0 && SummaryData[0].Data is not null ? SummaryData[0].Data! : "";

    public static Summary Of(string text) => new()
    {
        SummaryData = new List<SummaryDataItem> { new() { Data = text } }
    };
}

public sealed class SummaryDataItem
{
    public string? Data { get; init; }
    public string? DataURI { get; init; }
    public long? DataLength { get; init; }
    public string? DataID { get; init; }
}

// Minimal client for a local Ollama server (http://127.0.0.1:11434). Uses the
// /api/chat endpoint with stream disabled, and returns the assistant's message
// content. HttpClient + System.Text.Json only - no external package.
//
// THE SAME MODEL, THE SAME WAY, ON EVERY MACHINE. Ollama chooses a context from the
// memory it finds - 4096 tokens on a 4 GB card, 32768 on a 24 GB one, whose cache is
// larger to set up and to fill at every request - and unloads a model 5 minutes after
// its last use, so the first turn after a pause loads it again. The context is
// therefore stated (always the same: a different one reloads the model), and the
// model kept loaded - for 30 minutes after its last use, not for as long as Ollama
// runs: a model kept forever held the 4 GB card, and the model of another App could
// not load (2026-11-18). And when its model is not loaded, the client frees the card
// first: the other models are unloaded (Ollama loads them again when they are asked).
public sealed class OllamaClient : IDisposable
{
    private readonly HttpClient _http;
    private string _model;
    private readonly int _context;
    private readonly int _keepAlive;

    // context: tokens of prompt and answer. keepAliveSeconds: how long the model stays
    // loaded after a request; negative, for as long as Ollama runs.
    public OllamaClient(string model = "llama3.1", string baseUrl = "http://127.0.0.1:11434",
                        int context = 4096, int keepAliveSeconds = 1800)
    {
        _model = model;
        _context = context;
        _keepAlive = keepAliveSeconds;
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(5) };
    }

    // Send a system + user prompt, return the assistant's text reply. temperature: Ollama's default (0.8) when not given; 0 for a reply
    // that keeps to the text it is given (a summary, a translation), the same each time.
    public Task<string> ChatAsync(string system, string user, double? temperature = null) =>
        PostAsync(model => new
        {
            model,
            stream = false,
            keep_alive = _keepAlive,
            options = temperature is double t ? (object)new { num_ctx = _context, temperature = t, seed = 1 } : new { num_ctx = _context },
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user",   content = user }
            }
        });

    // The same, the reply a JSON object, and the same reply to the same prompt
    // (temperature 0): for understanding, where what is understood is then checked.
    public Task<string> ChatJsonAsync(string system, string user) =>
        PostAsync(model => new
        {
            model,
            stream = false,
            format = "json",
            keep_alive = _keepAlive,
            options = new { temperature = 0, seed = 1, num_ctx = _context },
            messages = new[]
            {
                new { role = "system", content = system },
                new { role = "user",   content = user }
            }
        });

    // The same, the reply forced to a JSON Schema (Ollama's structured output): the
    // model can only answer with what the schema allows.
    public Task<string> ChatSchemaAsync(string system, IEnumerable<(string Role, string Content)> turns, JsonNode schema) =>
        PostAsync(model => new
        {
            model,
            stream = false,
            format = schema,
            keep_alive = _keepAlive,
            options = new { temperature = 0, seed = 1, num_ctx = _context },
            messages = new[] { new { role = "system", content = system } }
                .Concat(turns.Select(t => new { role = t.Role, content = t.Content })).ToArray()
        });

    private async Task<string> PostAsync(Func<string, object> build)
    {
        await ProvideAsync().ConfigureAwait(false);
        await FreeTheCardAsync().ConfigureAwait(false);
        var json = JsonSerializer.Serialize(build(_model));
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("/api/chat", content).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(body);
        // Ollama /api/chat (non-stream): { "message": { "role":"assistant", "content":"..." }, ... }
        if (doc.RootElement.TryGetProperty("message", out var msg) &&
            msg.TryGetProperty("content", out var contentEl))
            return contentEl.GetString() ?? "";
        return "";
    }

    // THE CARD IS SHARED (2026-11-18). A model that is not loaded is loaded beside the others when it fits on the card; when it does not, the others are unloaded first (Ollama
    // loads them again when they are asked for): on the 4 GB card the request otherwise waited for ever. Whether it fits: the card's memory (nvidia-smi), less what the loaded models
    // take of it, against the size of this model and its context. A card that cannot be measured is taken as full.
    private async Task FreeTheCardAsync()
    {
        try
        {
            using var ps = await _http.GetAsync("/api/ps").ConfigureAwait(false);
            if (!ps.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await ps.Content.ReadAsStringAsync().ConfigureAwait(false));
            var loaded = doc.RootElement.TryGetProperty("models", out var models)
                ? models.EnumerateArray().Select(m => (Name: m.GetProperty("name").GetString() ?? "", Vram: m.TryGetProperty("size_vram", out var v) && v.TryGetInt64(out var b) ? b : 0L))
                        .Where(m => m.Name.Length > 0).ToList() : [];
            bool Same(string n) => n == _model || n == _model + ":latest";
            if (loaded.Count == 0 || loaded.Any(m => Same(m.Name))) return;
            if (Card.Value is long card && await SizeAsync().ConfigureAwait(false) is long size && card - loaded.Sum(m => m.Vram) >= size + ContextBytes) return;   // it fits beside them
            foreach (var other in loaded)
                using (await _http.PostAsync("/api/generate", new StringContent(JsonSerializer.Serialize(new { model = other.Name, keep_alive = 0 }), Encoding.UTF8, "application/json")).ConfigureAwait(false)) { }
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException) { }      // Ollama answers the request itself, or says why it cannot
    }

    private const long ContextBytes = 600L << 20;          // the context and the working memory of a model beside its weights, about 0.6 GB at 4096 tokens

    // The size of this model's weights, as Ollama lists it.
    private async Task<long?> SizeAsync()
    {
        using var tags = await _http.GetAsync("/api/tags").ConfigureAwait(false);
        if (!tags.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await tags.Content.ReadAsStringAsync().ConfigureAwait(false));
        if (!doc.RootElement.TryGetProperty("models", out var models)) return null;
        foreach (var m in models.EnumerateArray())
            if ((m.GetProperty("name").GetString() is { } n) && (n == _model || n == _model + ":latest") && m.TryGetProperty("size", out var s) && s.TryGetInt64(out var bytes)) return bytes;
        return null;
    }

    // The memory of the graphics card, in bytes, once (nvidia-smi); null where it cannot be read.
    private static readonly Lazy<long?> Card = new(() =>
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("nvidia-smi", "--query-gpu=memory.total --format=csv,noheader,nounits")
                                                           { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
            if (p is null) return null;
            var text = p.StandardOutput.ReadToEnd(); p.WaitForExit(5000);
            return long.TryParse(text.Split('\n')[0].Trim(), out var mib) ? mib << 20 : null;
        }
        catch (Exception) { return null; }
    });

    // ONE MODEL, ONE NAME (2026-11-18). Llama 3.2 with 3 billion parameters was loaded twice on the card: as the model created from Access (mpai-..., the weights the L3s of
    // the dialogue declare) and as "llama3.2:3b", the same weights Ollama downloads under its own name. Ollama keeps a copy for each name, and two did not fit. Whoever asks for
    // "llama3.2:3b" is therefore given the model created from Access, when Ollama has it: one copy serves every App. LlamaWeights is the llm-weights of 1MMC-EDP-V2.5-I01.
    public const string LlamaWeights = "https://registry.ollama.ai/v2/library/llama3.2/blobs/sha256:dde5aa3fc5ffc17176b5e8bdc82f587b24b2678c6c66101bf7da77af9f7ccdff";
    public static string? OneNameOf(string model) => model is "llama3.2:3b" or "llama3.2" or "llama3.2:latest" ? ModelNameOf(LlamaWeights) : null;

    private async Task<bool> HasAsync(string model)
    {
        try { using var show = await _http.PostAsync("/api/show", new StringContent(JsonSerializer.Serialize(new { model }), Encoding.UTF8, "application/json")).ConfigureAwait(false); return show.IsSuccessStatusCode; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return false; }
    }

    // ---- THE MODEL FROM ACCESS ---------------------------------------------------------------------------------------------
    // The weights of the language model are data of Access, not something Ollama fetches: the Metadata of the AIM declares them
    // (AccessSources "llm-weights", with "llm-template" and "llm-parameters" where the model has them), by the URI they are
    // published at and their SHA-256, and the Controller has obtained and checked them. Ollama is the program that runs the
    // model: it is given the weights from Access and creates the model from them, once - under a name that comes from the
    // weights - and the AIM then uses that model. Ollama itself is part of the Implementation, not of Access.
    public const string WeightsName = "llm-weights", TemplateName = "llm-template", ParametersName = "llm-parameters";

    private IAccessReader? _access;
    private bool _provided;
    private readonly SemaphoreSlim _providing = new(1, 1);

    public void UseAccess(IAccessReader? access)
    {
        if (access is not null && access.SourcesOf(WeightsName).Count > 0) _access = access;
    }

    // The name of the model Ollama creates from weights obtained from that URI.
    public static string ModelNameOf(string weightsUri) =>
        "mpai-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(weightsUri)))[..12].ToLowerInvariant();

    private async Task ProvideAsync()
    {
        if (_provided) return;
        await _providing.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_provided) return;
            if (_access is not null) _model = await CreateFromAccessAsync(_access).ConfigureAwait(false);
            else if (OneNameOf(_model) is { } one && await HasAsync(one).ConfigureAwait(false)) _model = one;      // one name for Llama 3.2 3B
            _provided = true;
        }
        finally { _providing.Release(); }
    }

    private async Task<string> CreateFromAccessAsync(IAccessReader access)
    {
        var weights = access.SourcesOf(WeightsName)[0];
        var name = ModelNameOf(weights);
        using var http = new HttpClient { BaseAddress = _http.BaseAddress, Timeout = TimeSpan.FromHours(2) };

        // Already created from these weights.
        using (var show = await http.PostAsync("/api/show", Json(new { model = name })).ConfigureAwait(false))
            if (show.IsSuccessStatusCode) return name;

        // The weights are read from where Access keeps them, never held in memory.
        string digest;
        if (access.Open(weights, "", out var hashing) != AccessOutcome.OK)
            throw new InvalidOperationException($"The weights of the language model ({weights}) cannot be read from Access.");
        using (hashing) digest = Convert.ToHexString(await SHA256.HashDataAsync(hashing).ConfigureAwait(false)).ToLowerInvariant();

        using (var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"/api/blobs/sha256:{digest}")).ConfigureAwait(false))
            if (!head.IsSuccessStatusCode)
            {
                access.Open(weights, "", out var upload);
                using (upload)
                {
                    using var body = new StreamContent(upload);
                    using var sent = await http.PostAsync($"/api/blobs/sha256:{digest}", body).ConfigureAwait(false);
                    sent.EnsureSuccessStatusCode();
                }
            }

        string? Text(string entry) =>
            access.SourcesOf(entry) is { Count: > 0 } uris && access.Get(uris[0], "", out var data) == AccessOutcome.OK
                ? Encoding.UTF8.GetString(data) : null;

        var request = new JsonObject
        {
            ["model"] = name,
            ["files"] = new JsonObject { ["weights.gguf"] = $"sha256:{digest}" },
            ["stream"] = false
        };
        if (Text(TemplateName) is { } template) request["template"] = template;
        if (Text(ParametersName) is { } parameters) request["parameters"] = JsonNode.Parse(parameters);
        using var created = await http.PostAsync("/api/create", Json(request)).ConfigureAwait(false);
        created.EnsureSuccessStatusCode();
        return name;
    }

    private static StringContent Json(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    public void Dispose() => _http.Dispose();
}
