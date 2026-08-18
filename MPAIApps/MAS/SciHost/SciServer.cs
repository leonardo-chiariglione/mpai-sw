using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Mas.Rca;   // MpaiPortData (boundary serializer)

namespace Mpai.Mas.Sci;

// A stand-in MPAI-MAS Service Controller Instance (SCI) for the AMQ demo.
//
// Implements the MPAI-MAS Remote API ( /MPAI/AIFU/ ) over HttpListener, mapping
// each route to the in-process AIF Controller (UserAgent / MPAI_AIFU_*). Holds
// the Controller + AMQ + models server-side; the RCA is a thin client.
//
// Bridging the MAS per-port model to our executor:
//   * The RCA POSTs input ports (Input/{pid}); we buffer them.
//   * On the first Output GET, we run the AIW with all buffered inputs at once
//     (UserAgent.RunAsync with the full boundary-port set) and cache the outputs.
//   * Subsequent Output GETs serve from that cached result.
// This resolves the (currently unspecified) execution-trigger and output-ready
// behaviour of the draft spec, pragmatically, on the SCI side. The RCA speaks
// the published API unchanged.
public sealed class SciServer
{
    private const string Prefix = "/MPAI/AIFU";

    private readonly string _amdRepo;
    private readonly string _settingsFile;
    private readonly string _outputFolder;
    private readonly string _listenUrl;

    // Server-side AIF state (loaded once).
    private UserAgent _ua = null!;
    private UaProviderBridge _provider = null!;
    private AimSettings _settings = null!;

    // Live SCI sessions: cid -> session.
    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    public SciServer(string amdRepo, string settingsFile, string outputFolder,
                     string listenUrl = "http://localhost:5005/")
    {
        _amdRepo      = amdRepo;
        _settingsFile = settingsFile;
        _outputFolder = outputFolder;
        _listenUrl    = listenUrl;
    }

    // A per-Controller session: one AIW (module) instance, buffered inputs,
    // and cached outputs after the run.
    private sealed class Session
    {
        public string ControllerId = "";
        public int    AiwId        = -1;
        public string Module       = "";
        public string State        = "ACTIVE";
        public readonly Dictionary<string, string> Inputs = new();   // pid -> port-data JSON
        public Dictionary<string, string>? Outputs;                  // pid -> port-data JSON (after run)
    }

    public async Task RunAsync()
    {
        LoadServerSide();

        using var listener = new HttpListener();
        listener.Prefixes.Add(_listenUrl);
        listener.Start();
        Console.WriteLine($"[SCI] Listening on {_listenUrl}  (AMQ ready, models loaded)");

        while (true)
        {
            var ctx = await listener.GetContextAsync();
            _ = Task.Run(() => HandleAsync(ctx));   // handle concurrently
        }
    }

    // Load the Controller + AMQ + models ONCE (server-side).
    private void LoadServerSide()
    {
        Console.WriteLine("[SCI] Loading Controller + AMQ + models…");
        var store = new AmdStore(_amdRepo);
        store.Scan();
        _settings = AimSettings.Load(_settingsFile);
        Directory.CreateDirectory(_outputFolder);

        _ua = new UserAgent(store);
        _ua.MPAI_AIFU_Controller_Initialize();
        _provider = new UaProviderBridge(store, _outputFolder);

        // Warm the models by starting an AIW once (loads BLIP/ASR/TTS).
        _ua.MPAI_AIFU_AIW_Start("MMC-AMQ-V2.5", _provider, _settings, out _);
        Console.WriteLine("[SCI] Models loaded.");
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            var req = ctx.Request;
            var path = req.Url!.AbsolutePath.TrimEnd('/');
            var method = req.HttpMethod;
            Console.WriteLine($"[SCI] {method} {path}");

            // Route dispatch on the /MPAI/AIFU/ surface.
            // POST /MPAI/AIFU/Controller
            if (method == "POST" && path == $"{Prefix}/Controller")
            { await CreateController(ctx); return; }

            // DELETE /MPAI/AIFU/Controller/{cid}
            if (method == "DELETE" && path.StartsWith($"{Prefix}/Controller/"))
            { DeleteController(ctx, path.Substring($"{Prefix}/Controller/".Length)); return; }

            // Everything else: /MPAI/AIFU/{cid}/AIW/...
            var rest = path.StartsWith(Prefix + "/") ? path.Substring(Prefix.Length + 1) : "";
            var segs = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
            // segs: {cid} AIW [Start | {mid} [Pause|Resume|Stop|Input/{pid}|Output/{pid}]]

            if (segs.Length >= 2 && segs[1] == "AIW")
            {
                var cid = segs[0];
                if (!_sessions.TryGetValue(cid, out var session))
                { Write(ctx, 404, "text/plain", "Unknown Controller."); return; }

                // POST .../AIW/Start
                if (method == "POST" && segs.Length == 3 && segs[2] == "Start")
                { await StartAiw(ctx, session); return; }

                if (segs.Length >= 4)
                {
                    var mid = segs[2];
                    var op  = segs[3];

                    if (session.AiwId.ToString() != mid && session.Module != mid)
                    { /* accept either the numeric aiwId or the module id we returned */ }

                    // GET .../AIW/{mid}/Pause | Resume | Stop | (status)
                    if (method == "GET" && op == "Pause")  { session.State = "PAUSED";  WriteState(ctx, session); return; }
                    if (method == "GET" && op == "Resume") { session.State = "ACTIVE";  WriteState(ctx, session); return; }
                    if (method == "GET" && op == "Stop")   { session.State = "STOPPED"; Write(ctx, 200, "text/plain", "OK"); return; }

                    // POST .../AIW/{mid}/Input/{pid}
                    if (method == "POST" && op == "Input" && segs.Length == 5)
                    { await ReceiveInput(ctx, session, segs[4]); return; }

                    // GET .../AIW/{mid}/Output/{pid}
                    if (method == "GET" && op == "Output" && segs.Length == 5)
                    { await SendOutput(ctx, session, segs[4]); return; }
                }

                // GET .../AIW/{mid}  (status)
                if (method == "GET" && segs.Length == 3)
                { WriteState(ctx, session); return; }
            }

            Write(ctx, 404, "text/plain", "No such route.");
        }
        catch (Exception ex)
        {
            try { Write(ctx, 500, "text/plain", "SCI error: " + ex.Message); } catch { }
            Console.WriteLine("[SCI] EXCEPTION: " + ex);
        }
    }

    // ── Route handlers ───────────────────────────────────────────────────────
    private Task CreateController(HttpListenerContext ctx)
    {
        var cid = Guid.NewGuid().ToString();
        _sessions[cid] = new Session { ControllerId = cid };
        // No prefix override: we keep the default /MPAI/AIFU.
        Write(ctx, 201, "application/json", JsonSerializer.Serialize(new { id = cid }));
        return Task.CompletedTask;
    }

    private void DeleteController(HttpListenerContext ctx, string cid)
    {
        _sessions.TryRemove(cid, out _);
        Write(ctx, 200, "text/plain", "OK");
    }

    private async Task StartAiw(HttpListenerContext ctx, Session s)
    {
        var body = await ReadBodyAsync(ctx.Request);
        string module = "MMC-AMQ-V2.5";
        try
        {
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("module", out var m) && m.ValueKind == JsonValueKind.String)
                module = m.GetString()!;
        }
        catch { }

        // Start a fresh AIW for this session (reuses cached models via provider).
        var err = _ua.MPAI_AIFU_AIW_Start(module, _provider, _settings, out var aiwId);
        if (err != AifError.OK)
        { Write(ctx, 500, "text/plain", $"Start failed: {err}"); return; }

        s.AiwId  = aiwId;
        s.Module = module;
        s.State  = "ACTIVE";
        s.Inputs.Clear();
        s.Outputs = null;

        var resp = new
        {
            controller = s.ControllerId,
            id         = aiwId.ToString(),
            name       = module,
            state      = s.State,
            change     = DateTime.UtcNow.ToString("o")
        };
        Write(ctx, 200, "application/json", JsonSerializer.Serialize(resp));
    }

    private async Task ReceiveInput(HttpListenerContext ctx, Session s, string pid)
    {
        var bytes = await ReadBodyBytesAsync(ctx.Request);

        // The body is MAS wire "port-data" (an object per the OSD schema, data
        // inline). Translate it into our INTERNAL object JSON (MpaiJson) that the
        // Controller/AIMs expect. The translation depends on the port's type.
        string internalJson = pid switch
        {
            "InputVisual" => MpaiJson.ToJson(MpaiPortData.ToVisual(bytes)),
            "InputText"   => MpaiJson.ToJson(MpaiPortData.ToText(bytes)),
            // Re-attach the Speech Qualifier, which MpaiPortData.ToSpeech drops.
            // MMC-ASR takes the input language from it, so without this every
            // remote utterance was recognised in whatever language the server
            // had configured, whatever the client asked for.
            "InputSpeech" => MpaiJson.ToJson(
                                 BasicSpeechObject.FromData(
                                     MpaiPortData.ToSpeech(bytes).Data,
                                     MpaiQualifierData.SpeechQualifierFrom(bytes))),
            "InputAudio"  => MpaiJson.ToJson(MpaiPortData.ToAudio(bytes)),

            // MMC-TST introduced Selector ports; AMQ has none, so the switch
            // had no case for them and they fell to the pass-through below.
            // That happened to work - the client could send internal JSON - but
            // only by accident, and it put the wire format at the mercy of
            // whatever the client chose to send.
            "LanguageSelector" or "MediaSelector"
                          => MpaiJson.ToJson(MpaiSelectorData.ToSelector(bytes)),
            _             => Encoding.UTF8.GetString(bytes)   // unknown port: pass through
        };

        // The first input AFTER a completed run starts a NEW round.
        //
        // Without this the session accumulates. A German sentence posted to
        // InputText in one exchange was still sitting in Inputs when the next
        // exchange posted InputSpeech, so the AIW ran with both and translated
        // the stale text. Locally that cannot happen - every RunAsync is handed
        // a fresh boundary dictionary and sees only what that run supplied - but
        // over MAS the inputs arrive one request at a time and nothing in the
        // API says when a round has ended. The first input after a result is the
        // only moment that can mean it.
        if (s.Outputs is not null) s.Inputs.Clear();

        s.Inputs[pid] = internalJson;
        s.Outputs = null;   // new input invalidates any prior run
        Write(ctx, 200, "text/plain", "OK");
    }

    private async Task SendOutput(HttpListenerContext ctx, Session s, string pid)
    {
        // On first Output GET after inputs, run the AIW with all buffered inputs.
        if (s.Outputs is null)
        {
            try
            {
                s.Outputs = await RunAiwAsync(s);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SCI] RUN EXCEPTION: " + ex);
                Write(ctx, 500, "text/plain", "Run failed: " + ex.Message);
                return;
            }
        }

        if (s.Outputs.TryGetValue(pid, out var portData))
            Write(ctx, 200, "MPAI/port-data", portData);
        else
            Write(ctx, 404, "text/plain", $"No output for port '{pid}'.");
    }

    // Run the AIW once with all buffered boundary inputs; collect boundary outputs.
    private async Task<Dictionary<string, string>> RunAiwAsync(Session s)
    {
        var ports = new Dictionary<string, string>(s.Inputs);

        // AMQ suspends; TST does not, and the difference decides how the AIW is
        // run.
        //
        // The two-step below is AMQ's choreography: the image goes in first, the
        // AIW suspends waiting for the question, and the rest of the inputs
        // resume it. Applied to an AIW that never suspends, it is destructive -
        // the first call runs with an EMPTY dictionary, nothing suspends, the
        // resume never happens, and every input the client sent is discarded.
        // That is what made MMC-SOA report "skipped (no input available)" for a
        // request that had posted InputSpeech moments earlier.
        //
        // The presence of InputVisual is what distinguishes them, and it is a
        // property of the request rather than a hard-coded module name.
        var isTwoStep = ports.ContainsKey("InputVisual");

        AifError e2;
        UserAgent.RunOutcome? o2;

        if (isTwoStep)
        {
            // AMQ: for text mode also supply an empty speech so SOA runs.
            //
            // Note this means something ELSE for TST, where an empty Speech
            // Object is how a caller asks MMC-SOA to acquire from a microphone -
            // which a server does not have. Another reason to keep the two paths
            // apart rather than share one.
            if (ports.ContainsKey("InputText") && !ports.ContainsKey("InputSpeech"))
                ports["InputSpeech"] = MpaiJson.ToJson(BasicSpeechObject.FromData(Array.Empty<byte>()));

            var visual = new Dictionary<string, string>();
            visual["InputVisual"] = ports["InputVisual"];

            var (e1, o1) = await _ua.RunAsync(s.AiwId, visual);

            var question = ports.Where(kv => kv.Key != "InputVisual")
                                .ToDictionary(kv => kv.Key, kv => kv.Value);

            (e2, o2) = (o1 is not null && o1.Suspended)
                ? await _ua.ResumeAsync(s.AiwId, question)
                : (e1, o1);
        }
        else
        {
            // One run, every buffered input, which is what the local host does.
            (e2, o2) = await _ua.RunAsync(s.AiwId, ports);
        }

        if (e2 != AifError.OK)
            Console.WriteLine($"[SCI] run returned {e2}");

        if (o2?.Completed is { IsError: true } failed)
            Console.WriteLine($"[SCI] {failed.FailedAim}: {failed.Payload}");

        var outputs = new Dictionary<string, string>();
        if (o2?.Completed is not null)
            foreach (var kv in o2.Completed.Ports)
            {
                // Translate the AIM's INTERNAL object JSON into MAS wire port-data
                // for the RCA, by output port type.
                var pid = kv.Key;
                var internalJson = kv.Value;
                byte[] wire = pid switch
                {
                    "OutputText"   => MpaiPortData.FromText(MpaiJson.FromJson<BasicTextObject>(internalJson)),
                    "OutputSpeech" => MpaiPortData.FromSpeech(MpaiJson.FromJson<BasicSpeechObject>(internalJson)),
                    "OutputAudio"  => MpaiPortData.FromAudio(MpaiJson.FromJson<BasicAudioObject>(internalJson)),
                    "OutputVisual" => MpaiPortData.FromVisual(MpaiJson.FromJson<BasicVisualObject>(internalJson)),
                    _              => Encoding.UTF8.GetBytes(internalJson)
                };
                outputs[pid] = Encoding.UTF8.GetString(wire);
            }
        return outputs;
    }

    // ── HTTP helpers ─────────────────────────────────────────────────────────
    private static async Task<string> ReadBodyAsync(HttpListenerRequest req)
    {
        using var r = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
        return await r.ReadToEndAsync();
    }

    private static async Task<byte[]> ReadBodyBytesAsync(HttpListenerRequest req)
    {
        using var ms = new MemoryStream();
        await req.InputStream.CopyToAsync(ms);
        return ms.ToArray();
    }

    private void WriteState(HttpListenerContext ctx, Session s)
    {
        var resp = new
        {
            controller = s.ControllerId,
            id         = s.AiwId.ToString(),
            name       = s.Module,
            state      = s.State,
            change     = DateTime.UtcNow.ToString("o")
        };
        Write(ctx, 200, "application/json", JsonSerializer.Serialize(resp));
    }

    private void Write(HttpListenerContext ctx, int status, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = contentType;
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.OutputStream.Close();
    }
}
