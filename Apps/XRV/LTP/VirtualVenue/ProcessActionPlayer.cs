using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

using Mpai.Mmm;
using Mpai.Mmm.Client;

namespace Mpai.Xrv.VirtualVenue;

// THE TIME-STAMPED PROCESS ACTIONS OF AN M-INSTANCE (N-26): what the performers, the participants, the audience and the operator do, each by its own Process, at the time the list
// gives (seconds from the start of the show; timeScale shortens it for a test). Nothing here knows the Script or the Module: the list is played as it is, live, over the MMM-API.
//   MM-Add / MM-Move: with a Spatial Attitude (OSD-OSA-V1.5) when the entry gives X, Y, Yaw (metres within the M-Location; the yaw as MMM-TEC takes it); "Audio": an Audio Object
//     (OSD-AUO-V1.5) the Request carries; "Model3D": a 3D Model Object (OSD-3DO-V1.5) the Request carries (an Item of the place: the console).
//   Property Change: the Item's light to "Intensity" (a Light Source, CVE-LTS-V1.0) - the operator's console lights up: Go.
//   Animate: the Process animates its Persona with an Animation Stream: UM-Capture, Identify (an Animation Qualifier, TFA-AMQ-V1.5, transport format a stream), MM-Animate; the
//     stream is then fed over the MMM's WebSocket.
//   Frame: one frame of a stream: a Body Descriptors Object (PAF-BDO-V1.6) of the joints given (the last frame is sent again every 5 s, so that the MMM does not end the stream).
// REACTIONS: a Process that watches the place (MM-Capture) and acts when it sees a light at least so strong: Bot turns to the portal when it brightens.
public sealed class ProcessActionPlayer(JsonObject plan, Func<string, MmmClient> clientOf, string wsBase, double timeScale = 1.0, Action<double, string>? log = null)
{
    public const string JointsFormat = "MPAI-XRV-Joints";
    private readonly Dictionary<string, (ClientWebSocket Socket, string Last)> feeds = new(StringComparer.Ordinal);
    private readonly string instance = plan["MInstance"]!.GetValue<string>();
    private readonly string root = plan["Root"]!.GetValue<string>();
    private readonly List<string> failed = new();

    public IReadOnlyList<string> Failed { get { lock (failed) return failed.ToList(); } }

    public async Task RunAsync(DateTimeOffset start, CancellationToken cancel)
    {
        var reactions = (plan["Reactions"] as JsonArray)?.OfType<JsonObject>().Select(r => Task.Run(() => ReactAsync(r, start, cancel), cancel)).ToList() ?? [];
        _ = Task.Run(() => KeepAliveAsync(closing.Token));                       // until CloseAsync
        foreach (var a in (plan["Actions"] as JsonArray)!.OfType<JsonObject>().OrderBy(a => a["t"]!.GetValue<double>()))
        {
            var due = start + TimeSpan.FromSeconds(a["t"]!.GetValue<double>() * timeScale);
            var wait = due - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancel);
            try { await PerformAsync(a); }
            catch (Exception e) when (e is not OperationCanceledException) { Fail($"{a.ToJsonString()[..Math.Min(120, a.ToJsonString().Length)]}: {e.Message}"); }
        }
        try { await Task.WhenAll(reactions); } catch (OperationCanceledException) { }
    }

    private readonly CancellationTokenSource closing = new();

    private void Fail(string why) { lock (failed) failed.Add(why); log?.Invoke(-1, "FAILED " + why); }

    private static JsonObject Attitude(string id, double x, double y, double yaw) => new()
    {
        ["Header"] = "OSD-OSA-V1.5", ["ObjectSpatialAttitudeID"] = id + "-sa", ["ObjectID"] = id,
        ["Position"] = new JsonObject { ["Header"] = "OSD-OPS-V1.5", ["PositionID"] = id + "-p", ["CartPosition"] = new JsonArray(x, y, 0.0) },
        ["Orientation"] = new JsonObject { ["Header"] = "OSD-OOR-V1.5", ["OrientationID"] = id + "-o", ["Orientation"] = new JsonArray(yaw, 0.0, 0.0) }
    };

    private static double? Num(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

    private async Task Check(string what, Task<MmmClient.Result> call)
    {
        var rs = await call;
        if (rs.Http is < 200 or >= 300) Fail($"{what}: {rs.Http} {rs.Response.PaStatus.Code} {rs.Response.PaStatus.Detail}");
    }

    private async Task PerformAsync(JsonObject a)
    {
        var process = a["Process"]!.GetValue<string>();
        var action = a["Action"]!.GetValue<string>();
        var client = clientOf(process);
        var nil = a["Nil"]?.GetValue<string>();
        var with = new List<JsonNode?>();
        if (Num(a["X"]) is { } x && Num(a["Y"]) is { } y) with.Add(Attitude(nil ?? process, x, y, Num(a["Yaw"]) ?? 0));
        log?.Invoke(a["t"]!.GetValue<double>(), $"{instance}: {process} {action} {nil}{(a["Say"] is { } say ? " - " + say : "")}");
        switch (action)
        {
            case "MM-Add":
                if (a["Audio"] is JsonObject audio) with.Insert(0, audio.DeepClone());
                if (a["Model3D"]?.GetValue<bool>() == true)
                    with.Insert(0, new JsonObject { ["Header"] = "OSD-3DO-V1.5", ["MInstanceID"] = instance, ["3DModelObjectID"] = nil, ["3DModelObjectSpaceTime"] = new JsonObject
                    {
                        ["Header"] = "OSD-STM-V1.5", ["SimpleTimeID"] = nil + "-st",
                        ["SimpleTimeData"] = new JsonArray(new JsonObject { ["FlagsByte"] = 3, ["StartTime"] = 0, ["EndTime"] = 0, ["AccuracyMode"] = "single", ["AccuracyPlusMinus"] = 0, ["TimeType"] = true, ["TimeUnit"] = "01" })
                    } });
                await Check($"MM-Add {nil}", client.PerformAsync("MM-Add", new Complements { Nil = nil, At = a["At"]!.GetValue<string>(), With = with }));
                break;
            case "MM-Move":
                await Check($"MM-Move {nil}", client.PerformAsync("MM-Move", new Complements { Nil = nil, To = a["To"]!.GetValue<string>(), With = with }));
                break;
            case "Property Change":
                await Check($"Property Change {nil}", client.PerformAsync("Property Change", new Complements { Nil = nil, With = [Light(nil!, Num(a["Intensity"]) ?? 1)] }));
                break;
            case "Animate":
                await AnimateAsync(client, nil!, a["Stream"]!.GetValue<string>());
                break;
            case "Frame":
                await FrameAsync(a["Stream"]!.GetValue<string>(), (JsonObject)a["Joints"]!);
                break;
            default: Fail($"unknown action {action}"); break;
        }
    }

    private JsonObject Light(string target, double intensity) => new()
    {
        ["Header"] = "CVE-LTS-V1.0", ["LightSourceID"] = target, ["MInstanceID"] = instance,
        ["LightSourceSpaceTime"] = new JsonObject { ["Header"] = "OSD-SPT-V1.5", ["SpaceTimeID"] = target + "-st" },
        ["LightSourceType"] = new JsonArray(new JsonObject { ["Point"] = new JsonObject { ["Header"] = "OSD-OPS-V1.5", ["PositionID"] = target + "-p", ["CartPosition"] = new JsonArray(0.0, 0.0, 0.0) } }),
        ["ColourInformation"] = new JsonObject { ["ColourData"] = "FFFFFF", ["ColourSpace"] = new JsonObject { ["Header"] = "TFA-CLQ-V1.5", ["ColourQualifierID"] = target + "-c", ["Formats"] = "sRGB" } },
        ["Intensity"] = intensity
    };

    private async Task AnimateAsync(MmmClient client, string persona, string data)
    {
        var q = new JsonObject
        {
            ["Header"] = "TFA-AMQ-V1.5", ["AnimationQualifierID"] = data + "-q", ["SubTypes"] = "Animation",
            ["Formats"] = new JsonObject { ["ContentFormat"] = "Skeleton", ["TransportFormat"] = "WebSocketAnimationStream" }    // the frames: Body Descriptors Objects of joint angles (JointsFormat)
        };
        await Check($"UM-Capture {data}", client.PerformAsync("UM-Capture", new Complements { Nil = data, At = root, With = [q] }));
        var identified = await client.PerformAsync("Identify", new Complements { Nil = data, With = [q.DeepClone()] });
        if (identified.Http != 201 || identified.Response.Complements?.Nil is not { } stream) { Fail($"Identify {data}: {identified.Http}"); return; }
        await Check($"MM-Animate {persona}", client.PerformAsync("MM-Animate", new Complements { Nil = persona, With = [JsonValue.Create(stream)] }));
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"{wsBase.TrimEnd('/')}/ws/v2.2/animation/{Uri.EscapeDataString(stream)}?token={client.Token}"), CancellationToken.None);
        lock (feeds) feeds[data] = (socket, "");
    }

    // A frame: the Body Descriptors Object of the joints (PAF-BDO-V1.6, its Data the joints as text).
    public static string Frame(string id, JsonObject joints) => new JsonObject
    {
        ["Header"] = "PAF-BDO-V1.6", ["BodyDescriptorsObjectID"] = id,
        ["BodyDescriptorsData"] = new JsonArray(new JsonObject { ["Data"] = new JsonObject { ["Format"] = JointsFormat, ["Joints"] = joints.DeepClone() }.ToJsonString() })
    }.ToJsonString();

    private async Task FrameAsync(string data, JsonObject joints)
    {
        (ClientWebSocket Socket, string Last) feed;
        lock (feeds) if (!feeds.TryGetValue(data, out feed)) { Fail($"no stream {data}"); return; }
        var frame = Frame($"{data}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}", joints);
        await feed.Socket.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, CancellationToken.None);
        lock (feeds) feeds[data] = (feed.Socket, frame);
    }

    private async Task KeepAliveAsync(CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try { await Task.Delay(5000, cancel); } catch (OperationCanceledException) { return; }
            List<(ClientWebSocket Socket, string Last)> all;
            lock (feeds) all = feeds.Values.ToList();
            foreach (var (socket, last) in all)
                if (last.Length > 0 && socket.State == WebSocketState.Open)
                    try { await socket.SendAsync(Encoding.UTF8.GetBytes(last), WebSocketMessageType.Text, true, cancel); } catch (WebSocketException) { }
        }
    }

    // A Process that reacts to what it sees: when the Item named has a light at least so strong, it waits and turns its Persona (MM-Move where it is, with the new yaw).
    private async Task ReactAsync(JsonObject r, DateTimeOffset start, CancellationToken cancel)
    {
        var client = clientOf(r["Process"]!.GetValue<string>());
        var persona = r["Nil"]!.GetValue<string>();
        var item = r["When"]!["Item"]!.GetValue<string>();
        var least = r["When"]!["IntensityAtLeast"]!.GetValue<double>();
        while (!cancel.IsCancellationRequested)
        {
            var rs = await client.PerformAsync("MM-Capture", new Complements { At = root });
            var things = (rs.Response.Complements?.With?.FirstOrDefault() as JsonObject)?["ItemValueOrRef"]?[0]?["Things"]?.AsArray().OfType<JsonObject>().ToList() ?? [];
            if (things.FirstOrDefault(t => (string?)t["ID"] == item) is { } seen && (Num(seen["Intensity"]) ?? 0) >= least
                && things.FirstOrDefault(t => (string?)t["ID"] == persona) is { } me && (string?)me["At"] is { } at)
            {
                await Task.Delay(TimeSpan.FromSeconds((Num(r["After"]) ?? 0) * timeScale), cancel);
                var place = things.FirstOrDefault(t => (string?)t["ID"] == at);
                var x = (Num(me["X"]) ?? 0) - (Num(place?["X"]) ?? 0);
                var y = (Num(me["Y"]) ?? 0) - (Num(place?["Y"]) ?? 0);
                log?.Invoke((DateTimeOffset.UtcNow - start).TotalSeconds / timeScale, $"{instance}: {r["Process"]} MM-Move {persona} - {r["Say"]}");
                await Check($"MM-Move {persona} (reaction)", client.PerformAsync("MM-Move", new Complements { Nil = persona, To = at, With = [Attitude(persona, Math.Round(x, 2), Math.Round(y, 2), Num(r["Yaw"]) ?? 0)] }));
                return;
            }
            await Task.Delay(150, cancel);
        }
    }

    public async Task CloseAsync()
    {
        closing.Cancel();
        List<ClientWebSocket> all;
        lock (feeds) all = feeds.Values.Select(f => f.Socket).ToList();
        foreach (var s in all) { try { if (s.State == WebSocketState.Open) await s.CloseAsync(WebSocketCloseStatus.NormalClosure, "over", CancellationToken.None); } catch { } s.Dispose(); }
    }
}
