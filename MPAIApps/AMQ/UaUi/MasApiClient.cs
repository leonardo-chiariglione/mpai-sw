using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Mpai.Mas.Rca;

// RCA-side client for the MPAI-MAS Remote API (draft spec, MPAI-MAS V1.0).
//
// Implements the published /MPAI/AIFU/ routes verbatim:
//   POST   /MPAI/AIFU/Controller                      -> create SCI, returns {id, prefix?}
//   POST   /MPAI/AIFU/{cid}/AIW/Start   {"module":..} -> start AIW, returns {id=mid,...}
//   GET    /MPAI/AIFU/{cid}/AIW/{mid}/Pause
//   GET    /MPAI/AIFU/{cid}/AIW/{mid}                  -> status
//   GET    /MPAI/AIFU/{cid}/AIW/{mid}/Resume
//   POST   /MPAI/AIFU/{cid}/AIW/{mid}/Input/{pid}      CONTENT-TYPE: MPAI/port-data
//   GET    /MPAI/AIFU/{cid}/AIW/{mid}/Output/{pid}     -> MPAI/port-data
//   GET    /MPAI/AIFU/{cid}/AIW/{mid}/Stop
//   DELETE /MPAI/AIFU/Controller/{cid}
//
// Spec conformance notes:
//  * The Controller-create response MAY return an alternative "prefix" that
//    MUST replace "/MPAI/AIFU" in all subsequent requests for that SCI. This
//    client honours that override (see _prefix).
//  * The spec mandates TLS. For the one-machine demo we pass an http:// base
//    URL; switching to https:// later needs no code change here.
//  * IDs are opaque strings (UUIDs recommended); this client treats them as
//    opaque and never parses them.
//  * Standard HTTP status codes signal failure; non-success throws MasApiException.
public sealed class MasApiClient : IDisposable
{
    private const string DefaultPrefix   = "/MPAI/AIFU";
    private const string PortDataMedia    = "MPAI/port-data";

    private readonly HttpClient _http;
    private readonly bool       _ownsHttp;

    // The route prefix in force. Starts as the default; replaced by the value
    // returned in the Controller-create response, if any.
    private string _prefix = DefaultPrefix;

    public string ControllerId { get; private set; } = string.Empty;

    // baseUrl example: "http://localhost:5005" (demo) or "https://host" (prod).
    public MasApiClient(string baseUrl, HttpClient? http = null)
    {
        if (http is null)
        {
            _http = new HttpClient { BaseAddress = new Uri(baseUrl) };
            _ownsHttp = true;
        }
        else
        {
            _http = http;
            if (_http.BaseAddress is null) _http.BaseAddress = new Uri(baseUrl);
            _ownsHttp = false;
        }
    }

    // ── 2.1 Initialise the Controller Instance ───────────────────────────────
    // POST /MPAI/AIFU/Controller -> 201, {"id": "...", "prefix"?: "..."}
    public async Task<string> CreateControllerAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, DefaultPrefix + "/Controller");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var resp = await _http.SendAsync(req, ct);
        await EnsureAsync(resp, "Create Controller");

        var doc = await ReadJsonAsync(resp, ct);
        ControllerId = GetString(doc, "id")
            ?? throw new MasApiException("Create Controller response missing 'id'.");

        // Honour the optional prefix override for all subsequent requests.
        var prefix = GetString(doc, "prefix");
        if (!string.IsNullOrWhiteSpace(prefix))
            _prefix = prefix!.TrimEnd('/');

        return ControllerId;
    }

    // ── 2.4 Start AI Module (AIW) ────────────────────────────────────────────
    // POST /{prefix}/{cid}/AIW/Start  {"module":"..."} -> 200, {"id": mid, ...}
    public async Task<StartResult> StartAiwAsync(string module, CancellationToken ct = default)
    {
        RequireController();
        var body = JsonSerializer.Serialize(new { module });
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_prefix}/{ControllerId}/AIW/Start")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var resp = await _http.SendAsync(req, ct);
        await EnsureAsync(resp, "Start AIW");

        var doc = await ReadJsonAsync(resp, ct);
        return new StartResult
        {
            ModuleId = GetString(doc, "id")
                ?? throw new MasApiException("Start AIW response missing 'id'."),
            Name     = GetString(doc, "name"),
            State    = GetString(doc, "state"),
            Change   = GetString(doc, "change")
        };
    }

    // ── 2.5 Pause / 2.6 Status / 2.7 Resume ──────────────────────────────────
    public Task<StateResult> PauseAsync(string mid, CancellationToken ct = default) =>
        StateGetAsync($"{_prefix}/{ControllerId}/AIW/{mid}/Pause", "Pause", ct);

    public Task<StateResult> StatusAsync(string mid, CancellationToken ct = default) =>
        StateGetAsync($"{_prefix}/{ControllerId}/AIW/{mid}", "Status", ct);

    public Task<StateResult> ResumeAsync(string mid, CancellationToken ct = default) =>
        StateGetAsync($"{_prefix}/{ControllerId}/AIW/{mid}/Resume", "Resume", ct);

    private async Task<StateResult> StateGetAsync(string route, string what, CancellationToken ct)
    {
        RequireController();
        using var req = new HttpRequestMessage(HttpMethod.Get, route);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var resp = await _http.SendAsync(req, ct);
        await EnsureAsync(resp, what);
        var doc = await ReadJsonAsync(resp, ct);
        return new StateResult
        {
            ModuleId = GetString(doc, "id"),
            Name     = GetString(doc, "name"),
            State    = GetString(doc, "state"),
            Change   = GetString(doc, "change")
        };
    }

    // ── 3.1 Send Input Data ──────────────────────────────────────────────────
    // POST /{prefix}/{cid}/AIW/{mid}/Input/{pid}  CONTENT-TYPE: MPAI/port-data
    public async Task SendInputAsync(
        string mid, string portId, byte[] portData, CancellationToken ct = default)
    {
        RequireController();
        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"{_prefix}/{ControllerId}/AIW/{mid}/Input/{portId}")
        {
            Content = new ByteArrayContent(portData)
        };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue(PortDataMedia);

        using var resp = await _http.SendAsync(req, ct);
        await EnsureAsync(resp, $"Send Input '{portId}'");
    }

    // ── 3.2 Receive Output Data ──────────────────────────────────────────────
    // GET /{prefix}/{cid}/AIW/{mid}/Output/{pid} -> MPAI/port-data bytes
    public async Task<byte[]> ReceiveOutputAsync(
        string mid, string portId, CancellationToken ct = default)
    {
        RequireController();
        using var req = new HttpRequestMessage(
            HttpMethod.Get, $"{_prefix}/{ControllerId}/AIW/{mid}/Output/{portId}");
        using var resp = await _http.SendAsync(req, ct);
        await EnsureAsync(resp, $"Receive Output '{portId}'");
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }

    // ── 4.1 Stop AIW / 4.2 Delete Controller ─────────────────────────────────
    public async Task StopAiwAsync(string mid, CancellationToken ct = default)
    {
        RequireController();
        using var req = new HttpRequestMessage(
            HttpMethod.Get, $"{_prefix}/{ControllerId}/AIW/{mid}/Stop");
        using var resp = await _http.SendAsync(req, ct);
        await EnsureAsync(resp, "Stop AIW");
    }

    public async Task DeleteControllerAsync(CancellationToken ct = default)
    {
        RequireController();
        using var req = new HttpRequestMessage(
            HttpMethod.Delete, $"{DefaultPrefix}/Controller/{ControllerId}");
        using var resp = await _http.SendAsync(req, ct);
        await EnsureAsync(resp, "Delete Controller");
        ControllerId = string.Empty;
        _prefix = DefaultPrefix;
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    private void RequireController()
    {
        if (string.IsNullOrEmpty(ControllerId))
            throw new MasApiException("No Controller instance. Call CreateControllerAsync first.");
    }

    private static async Task EnsureAsync(HttpResponseMessage resp, string what)
    {
        if (resp.IsSuccessStatusCode) return;
        string body = string.Empty;
        try { body = await resp.Content.ReadAsStringAsync(); } catch { }
        throw new MasApiException(
            $"{what} failed: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}. {body}".Trim());
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        var text = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object &&
        obj.TryGetProperty(name, out var v) &&
        v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}

public sealed class StartResult
{
    public required string ModuleId { get; init; }
    public string? Name   { get; init; }
    public string? State  { get; init; }
    public string? Change { get; init; }
}

public sealed class StateResult
{
    public string? ModuleId { get; init; }
    public string? Name   { get; init; }
    public string? State  { get; init; }
    public string? Change { get; init; }
}

public sealed class MasApiException : Exception
{
    public MasApiException(string message) : base(message) { }
}
