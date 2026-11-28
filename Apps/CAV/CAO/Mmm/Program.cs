using System.Globalization;
using System.Text.Json.Nodes;

using AIF.Controller;
using Mpai.Aif.Api;
using Mpai.Cav.Ams;
using Mpai.Cav.Ess;
using Mpai.Cav.Hci;
using Mpai.Cav.Map;
using Mpai.Cav.Mas;
using Mpai.Cav.Recordings;
using Mpai.Cav.Ua;
using Mpai.Core;
using Mpai.Mmm;
using Mpai.Mmm.Client;
using Mpai.Ua;
using Mpai.Wdl;

// THE CAV IN THE MMM (2026-11-27). A human - the test engineer - runs a Process of the M-Instance: the CAV. The human needs no H-User: it registers its Process and controls it.
// The CAV is the MPAI CAV - CAV.orch over the Module 1CAV-CAO-V2.0-I01 - with its User Agent's devices (CavWorld); what it drives is a Vehicle Item of the M-Instance, an Item like any
// other (a 3D Model Object with its glTF), which it MM-Adds at the CAV Proving Ground and moves (MM-Move) as its vehicle moves. Its Offline Map is the Proving Ground's Town, as world.json
// describes it. The conditions of the Proving Ground - the grip of the road, the weather - are read from the M-Instance as the drive goes on. The human says where to go: the
// passenger's words, in the cabin, as in every drive of the CAV.
//
//   dotnet run --project Apps\CAV\CAO\Mmm -- --mmm http://127.0.0.1:7198 --who TessProva --first Tess --last Prova --org Test --email tess@example.com --credential <its password>
//              [--from "Entrance Plaza"] [--to "Roundabout"]
static string Arg(string[] args, string name, string? fallback = null)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback ?? throw new ArgumentException($"{name} is needed");
}
var root = Root();
var mmm = new Uri(Arg(args, "--mmm", "http://127.0.0.1:7198"));
var who = Arg(args, "--who");
var (from, to) = (Arg(args, "--from", "Entrance Plaza"), Arg(args, "--to", "Roundabout"));

// ---- the Proving Ground, as the CAV's Offline Map
var world = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "AllApps", "MMM", "Viewer", "world", "world.json")))!.AsObject();
var env = ProvingGround.Environment(world) ?? throw new InvalidOperationException("world.json has no Proving Ground: run AllApps\\MMM\\World\\make_proving_ground.py");
var (ex, ey) = ((double)env["x"]!, (double)env["y"]!);
var map = ProvingGroundMap.Of(env);
string Place(string name) => map.WayPoints.FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase))?.Id
                             ?? throw new ArgumentException($"no place {name} on the Proving Ground: {string.Join(", ", map.WayPoints.Where(w => w.Name is not null).Select(w => w.Name))}");
var route = map.FastestRoute(Place(from), Place(to)) ?? throw new InvalidOperationException($"no Route from {from} to {to}");
var sim = new Simulation(map, route, [], seed: 7);
sim.Mechanical(seed: 9);
Console.WriteLine($"The CAV of {who}: from {from} to {to}, {route.Count} road segments, {sim.Path.Length:0} m.");

// ---- the CAV, a Process of the M-Instance: registered by its human, with no Persona; its Vehicle Item placed at the Proving Ground
var cav = $"CAV-{who}";
using var http = new HttpClient { BaseAddress = mmm };
var profile = new JsonObject
{
    ["Header"] = "MMM-PPR-V2.2", ["MInstanceID"] = "MI1", ["humanID"] = $"{who}-human", ["PersonalProfileID"] = $"Profile-{who}-human",
    ["PersonalProfile"] = new JsonObject
    {
        ["Credential"] = Arg(args, "--credential"), ["AccountLevel"] = "Participant",
        ["Data"] = new JsonArray(new JsonObject { ["FirstName"] = Arg(args, "--first"), ["LastName"] = Arg(args, "--last"), ["Organisation"] = Arg(args, "--org"), ["Email"] = Arg(args, "--email") }),
        ["Processes"] = new JsonArray(new JsonObject { ["ProcessIDOrProcess"] = new JsonArray(new JsonObject { ["ProcessID"] = cav }) })
    }
};
var reg = await new MmmClient(http, $"{who}-human").PerformAsync("Register", new Complements { With = [profile] });
if (!reg.Tokens.TryGetValue(cav, out var token)) { Console.WriteLine($"Register: {reg.Http} {reg.Response.PaStatus?.Detail}"); return 1; }
var process = new MmmClient(http, cav, token);
var vehicle = $"{cav}-Vehicle";
JsonObject Attitude((double X, double Y, double Yaw) p) => new()
{
    ["Header"] = "OSD-OSA-V1.5", ["ObjectSpatialAttitudeID"] = $"SA-{vehicle}",
    ["Position"] = new JsonObject { ["Header"] = "OSD-OPS-V1.5", ["PositionID"] = $"SA-{vehicle}-P", ["CartPosition"] = new JsonArray(Math.Round(p.X, 3), Math.Round(p.Y, 3), 0.0) },
    ["Orientation"] = new JsonObject { ["Header"] = "OSD-OOR-V1.5", ["OrientationID"] = $"SA-{vehicle}-O", ["Orientation"] = new JsonArray(Math.Round(p.Yaw, 2), 0.0, 0.0) }
};
// Where the Vehicle Item is in the Proving Ground: the vehicle keeps to the right, half a lane from the line of its road; yaw 0 looks north, positive turns to the west.
(double X, double Y, double Yaw) Pose(Simulation s)
{
    var v = s.Mechanics!;
    var yaw = v.Heading * 180 / Math.PI - 90; yaw = (yaw + 540) % 360 - 180;
    return (v.East + Math.Sin(v.Heading) * RoadMap.LaneWidth / 2, v.North - Math.Cos(v.Heading) * RoadMap.LaneWidth / 2, yaw);
}
var model = new JsonObject
{
    ["Header"] = "OSD-3DO-V1.5", ["MInstanceID"] = "MI1", ["3DModelObjectID"] = vehicle,
    ["3DModelObjectSpaceTime"] = new JsonObject { ["Header"] = "OSD-STM-V1.5", ["SimpleTimeID"] = $"{vehicle}-T", ["SimpleTimeData"] = new JsonArray(new JsonObject { ["FlagsByte"] = 3, ["StartTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ["EndTime"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ["AccuracyMode"] = "single", ["AccuracyPlusMinus"] = 1 }) },
    ["Basic3DModelObjects"] = new JsonArray(new JsonObject
    {
        ["B3ObjectIDOrB3Object"] = new JsonArray(new JsonObject
        {
            ["Header"] = "OSD-B3O-V1.5", ["MInstanceID"] = "MI1", ["Basic3DModelObjectID"] = $"{vehicle}-Body",
            ["Basic3DModelObjectData"] = new JsonArray(new JsonObject { ["DataURI"] = "world/vehicle.glb" })
        })
    }),
    ["DescrMetadata"] = $"The vehicle the CAV of {who} drives"
};
var added = await process.PerformAsync("MM-Add", Uc2.C(vehicle, at: ProvingGround.ID, with: [model, Attitude(Pose(sim))]));
if (added.Http != 200) { Console.WriteLine($"MM-Add of the Vehicle Item: {added.Http} {added.Response.PaStatus?.Detail}"); return 1; }
Console.WriteLine($"{cav} placed {vehicle} at the {(string)env["name"]!}.");

// ---- the vehicle moves the Vehicle Item: after each step of the vehicle, the latest pose goes to the M-Instance
(double X, double Y, double Yaw)? latest = null;
var moving = new CancellationTokenSource();
var mover = Task.Run(async () =>
{
    (double X, double Y, double Yaw)? sent = null;
    while (!moving.IsCancellationRequested)
    {
        var p = latest;
        if (p is { } pose && pose != sent)
        {
            var r = await process.PerformAsync("MM-Move", Uc2.C(vehicle, from: ProvingGround.ID, to: ProvingGround.ID, with: Attitude(pose)));
            if (r.Http != 200) Console.WriteLine($"MM-Move: {r.Http} {r.Response.PaStatus?.Detail}");
            sent = pose;
        }
        await Task.Delay(100);
    }
});
// ---- the conditions of the Proving Ground, read from the M-Instance each second: the grip of the road and the weather the vehicle's sensors report
var reading = Task.Run(async () =>
{
    string? told = null;
    while (!moving.IsCancellationRequested)
    {
        var r = await process.PerformAsync("MM-Capture", Uc2.C(at: ProvingGround.ID, with: new JsonObject
        {
            ["Header"] = "OSD-OPV-V1.5", ["PointOfViewID"] = $"POV-{cav}", ["CartPosition"] = new JsonArray(0.0, 0.0, 1.2), ["Orientation"] = new JsonArray(0.0, 0.0, 0.0)
        }));
        var place = (r.Response.Complements?.With?[0]?["ItemValueOrRef"]?[0]?["Things"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(t => (string?)t["ID"] == ProvingGround.ID)?["Properties"];
        if (place is not null)
        {
            var friction = place["Friction"]?.GetValue<double>() ?? Simulation.DryFriction;
            var weather = (string?)place["Weather"] ?? "Clear";
            lock (sim)
            {
                sim.Surface = [(0, double.MaxValue, friction)];
                sim.Weather = weather switch { "Snow" => (-3, 4), "Rain" => (7, 0), "Fog" => (9, 0), _ => (15, 0) };
            }
            var now = $"{weather}, grip {friction:0.00}";
            if (now != told) Console.WriteLine($"The Proving Ground: {now}."); told = now;
        }
        await Task.Delay(1000);
    }
});

// ---- the CAV: the workflow over its Module, the devices of its User Agent
var all = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "AIMs", "aim-settings.json")))!.AsObject();
all[MasProvider.Msa] = new JsonObject { ["InitialHeading"] = (sim.Path.At(0).Heading * 180 / Math.PI).ToString(CultureInfo.InvariantCulture) };
var settings = Path.Combine(Path.GetTempPath(), "mpai-cav-mmm-" + Guid.NewGuid().ToString("N") + ".json");
var storage = Path.Combine(Path.GetTempPath(), "mpai-cav-mmm-" + Guid.NewGuid().ToString("N"));
File.WriteAllText(settings, all.ToJsonString());
const string voice = "en_GB-alan-medium";
try
{
    // The passenger asks, and agrees to whatever the CAV answers first; the drive ends on arrival.
    using var cabin = new CavWorld(sim, [new(null, $"Please take me to the {to}.", After: 3), new("", "Yes, let's go.")], text => Passenger(root, text, voice),
                                   AvatarUtterance.AvatarDatum("cav-avatar.glb"), maxSteps: 3000, endCue: null) { Stepped = s => latest = Pose(s) };
    using var api = new ControllerApi(Path.Combine(root, "AIMs", "AMDs"), settings, store => new CompositeProvider(new EssProvider(root), new AmsProvider(), new MasProvider(), new HciProvider(store, root)));
    var devices = new DeviceRegistry()
        .RegisterDevice("Vehicle", cabin.Vehicle).RegisterDevice("Cabin", cabin.Cabin).RegisterDevice("Hearing", cabin.Hearing)
        .RegisterDevice("Map", cabin.Map).RegisterDevice("PointOfView", cabin.PointOfView).RegisterDevice("Journal", cabin.Journal)
        .RegisterAcquire("OSD-BTO-V1.5", (bool _, string? _) => Task.FromResult<string?>(MpaiJson.ToJson(BasicTextObject.FromText(storage))));
    var interpreter = new WorkflowInterpreter(api.Async(), devices, line => Console.WriteLine("  " + line));
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
    var run = Task.Run(() => interpreter.RunAsync(new WorkflowReader().Read(File.ReadAllText(Path.Combine(root, "Apps", "CAV", "CAO", "CAV.orch"))), stop.Token));
    var said = 0;
    while (!cabin.Finished.IsCompleted && !run.IsCompleted && !stop.IsCancellationRequested)
    {
        foreach (var (at, text) in cabin.Said.Skip(said)) Console.WriteLine($"{at,6:0.0} s  the CAV: {text}");
        said = cabin.Said.Count;
        await Task.WhenAny(cabin.Finished, run, Task.Delay(500));
    }
    if (cabin.Failure is not null)       // which AIM did not do its part
        foreach (var x in api.Status(interpreter.ModuleId).Aims.Where(x => x.Status.ToString() is not ("Alive" or "Running") || x.Reports.Count > 0))
            Console.WriteLine($"  AIM {x.Aim}: {x.Status} {x.Reason} {string.Join("; ", x.Reports.TakeLast(3))}");
    stop.Cancel();
    try { await run.WaitAsync(TimeSpan.FromSeconds(60)); } catch (Exception) { }
    foreach (var (at, text) in cabin.Said.Skip(said)) Console.WriteLine($"{at,6:0.0} s  the CAV: {text}");
    Console.WriteLine(cabin.Failure is { } f ? $"Stopped: {f}" : $"The drive: {(sim.Collided ? "a collision" : "no collision")}; {sim.Path.Length - sim.EgoS:0} m from {to}; {cabin.Steps} steps.");
}
finally
{
    await Task.Delay(300); moving.Cancel();
    try { await Task.WhenAll(mover, reading); } catch { }
    try { File.Delete(settings); Directory.Delete(storage, recursive: true); } catch { }
}
return 0;

// The repository: the folder with AIMs\AMDs and schemas, from where this program runs.
static string Root()
{
    for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar)))
        if (Directory.Exists(Path.Combine(dir, "AIMs", "AMDs")) && Directory.Exists(Path.Combine(dir, "schemas"))) return dir;
    throw new DirectoryNotFoundException("the repository (AIMs\\AMDs, schemas) is not above this program");
}

// What the human says in the cabin, as its microphone gives it: the words spoken by a Piper voice the L3 of TTS declares, as a Basic Audio Object of PCM samples.
static string Passenger(string root, string text, string voice)
{
    var all = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "AIMs", "aim-settings.json")))!;
    var plain = (all["1MMC-TTS-V2.5-I01"] as JsonObject ?? new JsonObject()).ToDictionary(p => p.Key, p => (string)p.Value!);
    var settings = new Dictionary<string, string>(ModelFiles.SettingsOf(root, "1MMC-TTS-V2.5-I01", plain));
    var language = voice == "en_GB-alan-medium" ? "en-GB" : "en-US";
    settings["VoiceModel"] = settings["Voice:" + language]; settings["VoiceConfig"] = settings["VoiceConfig:" + language];
    foreach (var k in settings.Keys.Where(k => k.StartsWith("Voice:") || k.StartsWith("VoiceConfig:")).ToList()) settings.Remove(k);
    var wav = Mpai.Aims.Tts.TtsFactory.Create(settings).ProcessAsync(BasicTextObject.FromText(text)).GetAwaiter().GetResult().Data;
    var rate = BitConverter.ToInt32(wav, 24);
    byte[] pcm = wav[44..];
    for (var pos = 12; pos + 8 <= wav.Length;)
    {
        var id = System.Text.Encoding.ASCII.GetString(wav, pos, 4); var length = BitConverter.ToInt32(wav, pos + 4);
        if (id == "data") { pcm = wav[(pos + 8)..Math.Min(wav.Length, pos + 8 + length)]; break; }
        pos += 8 + length + (length & 1);
    }
    var qualifier = new AudioQualifier
    {
        AudioQualifierID = Guid.NewGuid().ToString(),
        Formats = new AudioFormats { ContentFormat = new AudioContentFormat { RawData = new AudioRawData { SampleSpace = new Pcm { SamplingFrequency = rate, Precision = 16 } } } },
        Attributes = new AudioAttributes { Source = "Real", Device = new AudioDevice { DeviceRole = "Capture", DeviceType = "Microphone", CaptureConfiguration = new CaptureConfiguration { ChannelCount = 1, SamplingMode = "Mono" } } }
    };
    return MpaiJson.ToJson(BasicAudioObject.FromData(pcm, qualifier));
}

// THE PROVING GROUND AS AN OFFLINE MAP OF THE CAV: way points at the junctions of the Town's streets and at their ends, a road segment each way between neighbours (one lane each way,
// 50 km/h), on the Proving Ground's own frame (east and north metres from its centre). The places a passenger may ask for are named.
static class ProvingGroundMap
{
    private static readonly Dictionary<(double, double), string> Names = new()
    {
        [(-275, 0)] = "Entrance Plaza", [(23, 0)] = "Roundabout", [(-110, 80)] = "Park", [(-250, 160)] = "North-West Corner", [(-40, -160)] = "South-East Corner"
    };

    public static RoadMap Of(JsonObject env)
    {
        var (ex, ey) = ((double)env["x"]!, (double)env["y"]!);
        var streets = env["roads"]!.AsArray().OfType<JsonObject>().Where(r => (string?)r["kind"] == "urban")
            .Select(r => r["centreline"]!.AsArray().Select(p => (X: (double)p![0]! - ex, Y: (double)p[1]! - ey)).ToArray()).ToList();
        bool On((double X, double Y) p, (double X, double Y)[] s) =>
            Math.Abs(s[0].X - s[1].X) < 1e-6 ? Math.Abs(p.X - s[0].X) < 1e-6 && p.Y >= Math.Min(s[0].Y, s[1].Y) - 1e-6 && p.Y <= Math.Max(s[0].Y, s[1].Y) + 1e-6
                                             : Math.Abs(p.Y - s[0].Y) < 1e-6 && p.X >= Math.Min(s[0].X, s[1].X) - 1e-6 && p.X <= Math.Max(s[0].X, s[1].X) + 1e-6;
        var corners = streets.SelectMany(s => s).Distinct().ToList();
        var crossings = (from a in streets from b in streets where Math.Abs(a[0].X - a[1].X) < 1e-6 && Math.Abs(b[0].Y - b[1].Y) < 1e-6
                         let p = (X: a[0].X, Y: b[0].Y) where On(p, a) && On(p, b) select p).ToList();
        var points = corners.Concat(crossings).Distinct().ToList();
        string Id((double X, double Y) p) => $"J{p.X.ToString(CultureInfo.InvariantCulture)}_{p.Y.ToString(CultureInfo.InvariantCulture)}";
        var wayPoints = points.Select(p => new WayPoint(Id(p), p.X, p.Y, Names.GetValueOrDefault(p))).ToList();
        var segments = new List<RoadSegment>();
        foreach (var s in streets)
        {
            var along = points.Where(p => On(p, s)).OrderBy(p => Math.Abs(s[0].X - s[1].X) < 1e-6 ? p.Y : p.X).ToList();
            foreach (var (a, b) in along.Zip(along.Skip(1)))
            {
                segments.Add(new RoadSegment($"S{Id(a)}-{Id(b)}", Id(a), Id(b), Math.Round(50 / 3.6, 3), 1));
                segments.Add(new RoadSegment($"S{Id(b)}-{Id(a)}", Id(b), Id(a), Math.Round(50 / 3.6, 3), 1));
            }
        }
        return new RoadMap("ProvingGround-Town", 45.0703, 7.6869, wayPoints, segments);
    }
}
