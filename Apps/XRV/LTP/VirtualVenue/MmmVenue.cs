using System.Globalization;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;

using Mpai.Mmm;
using Mpai.Mmm.Client;
using Mpai.Ua;

namespace Mpai.Xrv.VirtualVenue;

// A VENUE OF XRV-LTP THAT IS AN M-INSTANCE OF MMM-TEC V2.2 (N-26, 2026-11-15): the Real Environment ("Theatre") or the Virtual Environment ("Square") of the two-metaverse demo. The show is
// a collection of Processes in the M-Instance - the performers, the participants, the audience, the operator - each acting by its own Process Actions. This device is one more Process:
// the one that animates the Items of the venue (its lamps and effects) and watches the venue for the Module. It acts only by Process Actions over the MMM-API (MmmClient) and the
// Animation Streams of the MMM (WebSocket), with the token the M-Instance gave it.
//
// WHAT IT GIVES (Data In: RE Data In XRV-RDI-V1.0 for the Real side, VE Data In XRV-VDI-V1.0 for the Virtual side), from MM-Capture of the venue every few hundred milliseconds, whenever
// what it sees changes - always all it sees, so that the Descriptors of the Module describe the whole venue:
//   - each Persona that is not its own: present (a 3D Model Object, Real side; a Metaverse Object, Virtual side), with where it is and faces (a Spatial Attitude, OSD-OSA-V1.5: the place
//     in metres within the M-Location it is at, the yaw as the M-Instance gives it) and, while an Animation Stream animates it, its body: the latest frame of the stream, a Body
//     Descriptors Object (PAF-BDO-V1.6) - on the Real side in a MoCap Object (PAF-MCO-V1.6), on the Virtual side as Data of the Metaverse Object;
//   - each sound of the place (an Audio Object, OSD-AUO-V1.5, read with its data) while it lasts (the duration of its PCM, 16 kHz mono 16-bit);
//   - the Go of a console (Real side): a console Item of the operator whose light goes on gives a Control Object (XRV-CTO-V1.0) with Control Data "Go".
// WHAT IT DOES (Data Out: XRV-RDO-V1.0 / XRV-VDO-V1.0): each command is performed with Property Change (MMM-PCP-V2.2) on an Item of the venue, as its Light Source (CVE-LTS-V1.0):
//   - a DMX command (Protocol DMX, Universe, Channel, Value 0-255) on the Real side: the Fixture of that Channel (the ControllerID REG gives) to Intensity Value/255;
//   - {Object, Property, Value}: Property "Intensity" (0-1) or "Colour" (RRGGBB). Anything else is not performed and said (Unperformed).
// THE ITEMS OF THE VENUE are placed with MM-Add before anything else, at the stage, with their initial Intensity: on the Real side the Fixtures of the RE Venue Specification and the
// extra Items given; on the Virtual side the Virtual Objects of the VE Venue Specification and the extra Items given.
public sealed class MmmVenue : IDevice
{
    public const string RDI = "XRV-RDI-V1.0", RDO = "XRV-RDO-V1.0", VDI = "XRV-VDI-V1.0", VDO = "XRV-VDO-V1.0";

    private readonly MmmClient venue;
    private readonly bool real;
    private readonly string root, stage, environment, instance, wsBase;
    private readonly IReadOnlyDictionary<string, double> items;
    private readonly IReadOnlySet<string> consoles, areas;
    private readonly TimeSpan every;
    private readonly SemaphoreSlim placing = new(1, 1);
    private bool placed;
    private long count;
    private readonly List<string> unperformed = new();
    private readonly Dictionary<string, (double Intensity, string Colour)> lights = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> frames = new(StringComparer.Ordinal);          // the latest frame of each Animation Stream watched
    private readonly Dictionary<string, Task> watching = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (JsonObject Body, long Start, long End)> sounds = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource stopping = new();

    // real: the Real side (RE Data In/Out) or the Virtual side. root: the M-Location of the whole venue (captured); stage: where the venue's Items are placed. items: the Items of the
    // venue with their initial Intensity (0-1). consoles: the Items of the operator whose light is a Go. wsBase: ws://host:port of the M-Instance (its Animation Streams).
    // areas: the M-Locations where a Persona is part of the show (the stage; for the participants, where they are); a Persona elsewhere (in the wings, in the street) is not given.
    public MmmVenue(MmmClient venue, bool real, string root, string stage, IReadOnlyDictionary<string, double> items, string environment, string instance, string wsBase,
                    IEnumerable<string>? consoles = null, IEnumerable<string>? areas = null, TimeSpan? captureEvery = null)
    {
        this.venue = venue; this.real = real; this.root = root; this.stage = stage; this.items = items; this.environment = environment; this.instance = instance;
        this.wsBase = wsBase.TrimEnd('/'); this.consoles = new HashSet<string>(consoles ?? [], StringComparer.Ordinal); every = captureEvery ?? TimeSpan.FromMilliseconds(150);
        this.areas = new HashSet<string>(areas ?? [stage], StringComparer.Ordinal);
    }

    // The Items of the venue of a Venue Specification: the Fixtures (RE: FixtureID of each DMX Channel) or the Virtual Objects (VE), with an Intensity of 0.
    public static IEnumerable<string> ItemsOf(JsonObject venueSpecification) =>
        (venueSpecification["DMXUniverses"] as JsonArray)?.SelectMany(u => (u?["Channels"] as JsonArray) ?? []).Select(c => c?["FixtureID"]?.GetValue<string>()).OfType<string>()
        ?? (venueSpecification["VirtualObjects"] as JsonArray)?.Select(o => o?["3DModelObjectID"]?.GetValue<string>()).OfType<string>()
        ?? [];

    public IReadOnlyList<string> Unperformed { get { lock (unperformed) return unperformed.ToList(); } }
    private void Say(string why) { lock (unperformed) unperformed.Add(why); }

    // What the device gave and did, for whoever shows the run (time in Unix ms, text).
    public event Action<string, string>? Gave, Did;

    private async Task PlaceAsync()
    {
        await placing.WaitAsync();
        try
        {
            if (placed) return;
            foreach (var (id, intensity) in items)
            {
                var model = new JsonObject { ["Header"] = "OSD-3DO-V1.5", ["MInstanceID"] = instance, ["3DModelObjectID"] = id, ["3DModelObjectSpaceTime"] = Simple(id + "-st", 0) };
                var rs = await venue.PerformAsync("MM-Add", new Complements { Nil = id, At = stage, With = [model] });
                if (rs.Http != 200) { Say($"MM-Add {id} at {stage}: {rs.Http} {rs.Response.PaStatus.Code} {rs.Response.PaStatus.Detail}"); continue; }
                await LightAsync(id, intensity, "FFFFFF");
            }
            placed = true;
        }
        finally { placing.Release(); }
    }

    private async Task LightAsync(string target, double intensity, string colour)
    {
        intensity = Math.Clamp(intensity, 0, 1);
        var rs = await venue.PerformAsync("Property Change", new Complements { Nil = target, With = [LightSource(target, intensity, colour)] });
        if (rs.Http != 200) { Say($"Property Change {target}: {rs.Http} {rs.Response.PaStatus.Code} {rs.Response.PaStatus.Detail}"); return; }
        lock (lights) lights[target] = (intensity, colour);
    }

    public async Task DeliverAsync(string dataType, string json, CancellationToken cancel)
    {
        if (dataType != (real ? RDO : VDO)) return;
        await PlaceAsync();
        var root = JsonNode.Parse(json);
        var commands = new List<(JsonObject Command, string? Controller)>();
        if (real)
            foreach (var c in root?["REDataOut"]?["ControlObjects"]?.AsArray().OfType<JsonObject>() ?? [])
            {
                if (c["ControlObject"]?["ControlData"]?.GetValue<string>() is { } text && JsonNode.Parse(text) is JsonObject command)
                    commands.Add((command, c["ControlObject"]?["ControllerID"]?.GetValue<string>()));
            }
        else
            foreach (var m in root?["VEDataOut"]?["MetaverseObjects"]?.AsArray().OfType<JsonObject>() ?? [])
                foreach (var d in m["MetaverseObject"]?["MetaverseObjectData"]?.AsArray().OfType<JsonObject>() ?? [])
                    if (d["Data"]?.GetValue<string>() is { } text && JsonNode.Parse(text) is JsonObject command) commands.Add((command, null));

        foreach (var (command, controller) in commands)
        {
            string target; string property; JsonNode? value;
            if (command["Protocol"]?.GetValue<string>() == "DMX")
            {
                if (controller is null) { Say($"a DMX command with no Fixture: {command.ToJsonString()}"); continue; }
                target = controller; property = "Intensity"; value = JsonValue.Create((command["Value"]?.GetValue<double>() ?? 0) / 255.0);
            }
            else
            {
                target = command["Object"]?.GetValue<string>() ?? ""; property = command["Property"]?.GetValue<string>() ?? ""; value = command["Value"];
                if (target.Length == 0 || property.Length == 0) { Say($"a command with no Object or no Property: {command.ToJsonString()}"); continue; }
            }
            (double Intensity, string Colour) now;
            lock (lights) now = lights.GetValueOrDefault(target, (0, "FFFFFF"));
            switch (property)
            {
                case "Intensity": await LightAsync(target, value!.GetValue<double>(), now.Colour); break;
                case "Colour": await LightAsync(target, now.Intensity, value!.GetValue<string>()); break;
                default: Say($"{target}.{property}: not a Property this venue performs"); continue;
            }
            Did?.Invoke(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture), $"{target}.{property} = {value?.ToJsonString()} ({root?["DescrMetadata"]?.GetValue<string>() ?? ""})");
        }
    }

    private JsonObject LightSource(string target, double intensity, string colour) => new()
    {
        ["Header"] = "CVE-LTS-V1.0", ["LightSourceID"] = target, ["MInstanceID"] = instance,
        ["LightSourceSpaceTime"] = new JsonObject { ["Header"] = "OSD-SPT-V1.5", ["SpaceTimeID"] = target + "-st" },
        ["LightSourceType"] = new JsonArray(new JsonObject { ["Point"] = new JsonObject { ["Header"] = "OSD-OPS-V1.5", ["PositionID"] = target + "-p", ["CartPosition"] = new JsonArray(0.0, 0.0, 0.0) } }),
        ["ColourInformation"] = new JsonObject { ["ColourData"] = colour, ["ColourSpace"] = new JsonObject { ["Header"] = "TFA-CLQ-V1.5", ["ColourQualifierID"] = target + "-c", ["Formats"] = "sRGB" } },
        ["Intensity"] = intensity
    };

    // The latest frame of an Animation Stream, watched as the MMM lets whoever sees the Persona watch it.
    private void Watch(string stream)
    {
        lock (watching) if (watching.ContainsKey(stream)) return; else watching[stream] = Task.Run(async () =>
        {
            try
            {
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(new Uri($"{wsBase}/ws/v2.2/animation/{Uri.EscapeDataString(stream)}/watch?token={venue.Token}"), stopping.Token);
                var buffer = new byte[65536];
                while (socket.State == WebSocketState.Open && !stopping.IsCancellationRequested)
                {
                    var got = new List<byte>();
                    WebSocketReceiveResult r;
                    do { r = await socket.ReceiveAsync(buffer, stopping.Token); got.AddRange(buffer.AsSpan(0, r.Count).ToArray()); } while (!r.EndOfMessage);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    lock (frames) frames[stream] = Encoding.UTF8.GetString(got.ToArray());
                }
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException) { }
            finally { lock (watching) watching.Remove(stream); }
        });
    }

    public async IAsyncEnumerable<(string DataType, string Json)> ReadAsync([EnumeratorCancellation] CancellationToken cancel)
    {
        await PlaceAsync();
        string? last = null;
        var consoleOn = new Dictionary<string, bool>(StringComparer.Ordinal);
        while (!cancel.IsCancellationRequested)
        {
            var rs = await venue.PerformAsync("MM-Capture", new Complements { At = root });
            if (rs.Http == 200 && rs.Response.Complements?.With?.FirstOrDefault() is JsonObject scene)
            {
                var things = scene["ItemValueOrRef"]?[0]?["Things"]?.AsArray().OfType<JsonObject>().ToList() ?? [];
                var places = things.Where(t => (string?)t["Kind"] == "Location").ToDictionary(t => t["ID"]!.GetValue<string>(), t => (X: D(t["X"]), Y: D(t["Y"])), StringComparer.Ordinal);
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var persons = new List<(string Id, JsonObject Attitude, string? Frame)>();
                var go = new List<string>();
                var heard = new List<JsonObject>();
                foreach (var t in things)
                {
                    var id = t["ID"]!.GetValue<string>();
                    var kind = (string?)t["Kind"];
                    if (kind == "Location" || (string?)t["Owner"] == venue.ProcessID) continue;
                    if (consoles.Contains(id))
                    {
                        var on = D(t["Intensity"]) > 0;
                        if (on && !consoleOn.GetValueOrDefault(id)) go.Add(id);
                        consoleOn[id] = on;
                        continue;
                    }
                    if (kind == "Audio")
                    {
                        if (!sounds.ContainsKey(id) && (await venue.ReadAsync(id)) is { Http: 200, Item: { } read } && read["ItemValueOrRef"]?[0] is JsonObject body)
                        {
                            var start = (long)D(t["Start"]);
                            sounds[id] = (body, start, start + DurationMs(body));
                        }
                        if (sounds.TryGetValue(id, out var s) && now <= s.End) heard.Add(s.Body);
                        continue;
                    }
                    if (kind != "Persona" || items.ContainsKey(id)) continue;
                    var at = (string?)t["At"] ?? "";
                    if (!areas.Contains(at)) continue;
                    var (px, py) = places.GetValueOrDefault(at);
                    var attitude = Attitude(id, Math.Round(D(t["X"]) - px, 2), Math.Round(D(t["Y"]) - py, 2), t["Yaw"] is JsonValue y ? y.GetValue<double>() : 0, now);
                    string? frame = null;
                    if ((string?)t["Animation"] is { } stream) { Watch(stream); lock (frames) frame = frames.GetValueOrDefault(stream); }
                    persons.Add((id, attitude, frame));
                }
                var signature = string.Join("|", persons.OrderBy(p => p.Id, StringComparer.Ordinal).Select(p => $"{p.Id}:{p.Attitude["Position"]!["CartPosition"]!.ToJsonString()}:{p.Attitude["Orientation"]!["Orientation"]![0]}:{p.Frame}"))
                                + "|" + string.Join(",", heard.Select(h => h["AudioObjectID"]?.GetValue<string>()));
                if (go.Count > 0 || signature != last)
                {
                    last = signature;
                    var dataIn = real ? RealDataIn(persons, heard, go, now) : VirtualDataIn(persons, now);
                    var text = dataIn.ToJsonString();
                    Gave?.Invoke(now.ToString(CultureInfo.InvariantCulture), signature + (go.Count > 0 ? " GO" : ""));
                    yield return (real ? RDI : VDI, text);
                }
            }
            else Say($"MM-Capture {root}: {rs.Http} {rs.Response.PaStatus.Code} {rs.Response.PaStatus.Detail}");
            try { await Task.Delay(every, cancel); } catch (OperationCanceledException) { yield break; }
        }
    }

    private static double D(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;

    // How long the PCM of an Audio Object lasts (16 kHz mono 16-bit), in ms; 5 s if it says nothing this device reads.
    private static long DurationMs(JsonObject audio)
    {
        foreach (var b in (audio["BasicAudioObjects"] as JsonArray) ?? [])
            foreach (var o in (b?["BAObjectIDOrBAObject"] as JsonArray) ?? [])
                foreach (var d in (o?["BasicAudioObjectData"] as JsonArray) ?? [])
                    if (d?["Data"]?.GetValue<string>() is { } text) return text.Length * 3 / 4 / 2 * 1000L / 16000;
        return 5000;
    }

    private JsonObject Attitude(string id, double x, double y, double yaw, long now)
    {
        while (yaw <= -180) yaw += 360;
        while (yaw > 180) yaw -= 360;
        var n = $"{id}-sa-{now}";
        return new JsonObject
        {
            ["Header"] = "OSD-OSA-V1.5", ["ObjectSpatialAttitudeID"] = n, ["ObjectID"] = id,
            ["Position"] = new JsonObject { ["Header"] = "OSD-OPS-V1.5", ["PositionID"] = n + "-p", ["CartPosition"] = new JsonArray(x, y, 0.0) },
            ["Orientation"] = new JsonObject { ["Header"] = "OSD-OOR-V1.5", ["OrientationID"] = n + "-o", ["Orientation"] = new JsonArray(yaw, 0.0, 0.0) }
        };
    }

    private JsonObject RealDataIn(List<(string Id, JsonObject Attitude, string? Frame)> persons, List<JsonObject> heard, List<string> go, long now)
    {
        var id = $"mmm-rdi-{Interlocked.Increment(ref count)}";
        var content = new JsonObject();
        if (persons.Count > 0)
        {
            content["3DModelObjects"] = new JsonArray(persons.Select(p => (JsonNode)new JsonObject
            {
                ["3DModelObjectTime"] = Simple(p.Id + "-t", now),
                ["3DModelObject"] = new JsonObject { ["Header"] = "OSD-3DO-V1.5", ["MInstanceID"] = instance, ["3DModelObjectID"] = p.Id, ["3DModelObjectSpaceTime"] = Simple(p.Id + "-st", now) }
            }).ToArray());
            content["MoCapObjects"] = new JsonArray(persons.Select(p =>
            {
                var data = new JsonArray(new JsonObject { ["Data"] = p.Attitude.ToJsonString() });
                if (p.Frame is not null) data.Add(new JsonObject { ["Data"] = p.Frame });
                return (JsonNode)new JsonObject
                {
                    ["MoCapObjectTime"] = Simple(p.Id + "-mt", now),
                    ["MoCapObject"] = new JsonObject { ["Header"] = "PAF-MCO-V1.6", ["MoCapObjectID"] = p.Id, ["MoCapData"] = data }
                };
            }).ToArray());
        }
        if (heard.Count > 0)
            content["AudioObjects"] = new JsonArray(heard.Select(h => (JsonNode)new JsonObject { ["AudioObjectTime"] = Simple(h["AudioObjectID"] + "-t", now), ["AudioObject"] = h.DeepClone() }).ToArray());
        if (go.Count > 0)
            content["ControlObjects"] = new JsonArray(go.Select(c => (JsonNode)new JsonObject { ["ControlObject"] = new JsonObject
            {
                ["Header"] = "XRV-CTO-V1.0", ["ControlObjectID"] = $"{id}-{c}", ["UEnvironmentID"] = environment, ["ControlObjectTime"] = Simple($"{id}-{c}-t", now), ["ControllerID"] = c, ["ControlData"] = "Go",
                ["ControlQualifier"] = new JsonObject { ["Header"] = "TFA-CXQ-V1.5", ["ControlQualifierID"] = $"{id}-{c}-q", ["SubTypes"] = new JsonObject(), ["Formats"] = "OSC" }
            } }).ToArray());
        return new JsonObject { ["Header"] = RDI, ["MInstanceID"] = instance, ["UEnvironmentID"] = environment, ["REDataInID"] = id, ["REDataInTime"] = Simple(id + "-t", now), ["REDataIn"] = content };
    }

    private JsonObject VirtualDataIn(List<(string Id, JsonObject Attitude, string? Frame)> persons, long now)
    {
        var id = $"mmm-vdi-{Interlocked.Increment(ref count)}";
        return new JsonObject
        {
            ["Header"] = VDI, ["MInstanceID"] = instance, ["VEDataInID"] = id, ["UEnvironmentID"] = environment, ["VEDataInTime"] = Simple(id + "-t", now),
            ["VEDataIn"] = new JsonObject
            {
                ["MetaverseObjects"] = new JsonArray(persons.Select(p =>
                {
                    var data = new JsonArray(new JsonObject { ["ID"] = p.Id }, new JsonObject { ["Data"] = p.Attitude.ToJsonString() });
                    if (p.Frame is not null) data.Add(new JsonObject { ["Data"] = p.Frame });
                    return (JsonNode)new JsonObject
                    {
                        ["MetaverseObject"] = new JsonObject
                        {
                            ["Header"] = "XRV-MVO-V1.0", ["MetaverseObjectID"] = p.Id, ["UEnvironmentID"] = environment, ["MetaverseObjectTime"] = Simple(p.Id + "-t", now),
                            ["MetaverseObjectData"] = data,
                            ["MetaverseQualifier"] = new JsonObject { ["Header"] = "TFA-MAQ-V1.5", ["MetaverseQualifierID"] = p.Id + "-q", ["SubTypes"] = new JsonObject(), ["Formats"] = new JsonObject() }
                        }
                    };
                }).ToArray())
            }
        };
    }

    // A Simple Time (OSD-STM-V1.5): one absolute time, in milliseconds.
    private static JsonObject Simple(string id, double ms) => new()
    {
        ["Header"] = "OSD-STM-V1.5", ["SimpleTimeID"] = id,
        ["SimpleTimeData"] = new JsonArray(new JsonObject { ["FlagsByte"] = 3, ["StartTime"] = ms, ["EndTime"] = ms, ["AccuracyMode"] = "single", ["AccuracyPlusMinus"] = 0, ["TimeType"] = true, ["TimeUnit"] = "01" })
    };

    public Task SafeStopAsync() { stopping.Cancel(); return Task.CompletedTask; }

    // The latest frame of an Animation Stream the device watches (for whoever shows the run), or null.
    public string? FrameOf(string stream) { lock (frames) return frames.GetValueOrDefault(stream); }

    // The light of each Item of the venue, as the device last set it (for whoever shows the run).
    public IReadOnlyDictionary<string, (double Intensity, string Colour)> Lights { get { lock (lights) return new Dictionary<string, (double, string)>(lights); } }
}
