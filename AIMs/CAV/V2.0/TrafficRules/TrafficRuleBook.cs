using System.Text.Json;
using System.Text.Json.Nodes;
using Mpai.Cav.Driving;

namespace Mpai.Cav.TrafficRules;

public enum TrafficSide { Right, Left }

public enum UnsignedPriority { FromRight, FromLeft, NoGeneralRule }

public enum RoadClass { BuiltUp, Rural, Expressway, Motorway }

public sealed record Rule(string RuleID, string Article, string? Paragraph, string Title, string Status, string Modality, IReadOnlyList<string> AppliesTo,
                          IReadOnlyList<string> Context, string Behaviour, IReadOnlyList<string> Parameters, string Automation, string Text);

public sealed record SignEffect(string Behaviour, string? Rule, string? Side);

public sealed record Sign(string SignID, string Name, string Category, string? Group, SignEffect? Effect);

// THE TRAFFIC RULES WHERE THE CAV DRIVES (CAV-TRR-V2.0): the rules of the Convention on Road Traffic of 1968 (Data\VC1968-Chapter2.json), the parameters of the
// jurisdiction - its profile and the profiles it is based on, the nearest winning (Data\Profiles) - and the signs (Data\Signs-1968.json). Json is the composed
// Traffic Rules, valid against schemas\CAV2\V2.0\data\TrafficRules.json; the rest reads it for the driving software.
public sealed class TrafficRuleBook
{
    public static string DefaultData => Path.Combine(AppContext.BaseDirectory, "TrafficRules");

    public JsonObject Json { get; }
    public string Jurisdiction { get; }
    public IReadOnlyList<Rule> Rules { get; }
    public IReadOnlyList<Sign> Signs { get; }
    private readonly JsonObject parameters;

    private TrafficRuleBook(JsonObject json)
    {
        Json = json;
        Jurisdiction = (string)json["Jurisdiction"]!["Code"]!;
        parameters = json["Parameters"]!.AsObject();
        Rules = json["Rules"]!.AsArray().Select(r => new Rule((string)r!["RuleID"]!, r["Article"]!.ToString(), (string?)r["Paragraph"], (string)r["Title"]!, (string)r["Status"]!,
            (string)r["Modality"]!, Strings(r["AppliesTo"]), Strings(r["Context"]), (string)r["Behaviour"]!, Strings(r["Parameters"]), (string)r["Automation"]!, (string)r["Text"]!)).ToList();
        Signs = (json["Signs"]?.AsArray() ?? []).Select(s => new Sign((string)s!["SignID"]!, (string)s["Name"]!, (string)s["Category"]!, (string?)s["Group"],
            s["Effect"] is JsonObject e ? new SignEffect((string)e["Behaviour"]!, (string?)e["Rule"], (string?)e["Side"]) : null)).ToList();
    }

    private static List<string> Strings(JsonNode? n) => n?.AsArray().Select(x => (string)x!).ToList() ?? [];

    // The Traffic Rules of a jurisdiction (RHT, LHT, IT, GB...): the Convention, the profile chain, the signs.
    public static TrafficRuleBook Load(string jurisdiction, string? dataDir = null, string? id = null)
    {
        var dir = dataDir ?? DefaultData;
        JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var convention = Read(Path.Combine(dir, "VC1968-Chapter2.json"));
        var signs = Read(Path.Combine(dir, "Signs-1968.json"));
        var chain = new List<JsonObject>();
        for (string? code = jurisdiction; code is not null;)
        {
            var file = Path.Combine(dir, "Profiles", code + ".json");
            if (!File.Exists(file)) throw new FileNotFoundException($"No traffic rules profile for '{code}'.", file);
            var p = Read(file);
            if (chain.Count > 5) throw new InvalidOperationException("Profiles based on each other in a loop.");
            chain.Add(p);
            code = (string?)p["Jurisdiction"]?["BasedOn"];
        }
        var merged = new JsonObject();
        for (var i = chain.Count - 1; i >= 0; i--)                                       // the base first, the jurisdiction's own last: the nearest wins
            foreach (var (name, value) in chain[i]["Parameters"]!.AsObject()) merged[name] = value?.DeepClone();
        var json = new JsonObject
        {
            ["Header"] = "CAV-TRR-V2.0",
            ["TrafficRulesID"] = id ?? $"TrafficRules-{jurisdiction}",
            ["Sources"] = new JsonArray(convention["Source"]!.DeepClone(), signs["Source"]!.DeepClone()),
            ["Jurisdiction"] = chain[0]["Jurisdiction"]!.DeepClone(),
            ["Parameters"] = merged,
            ["Rules"] = convention["Rules"]!.DeepClone(),
            ["Signs"] = signs["Signs"]!.DeepClone()
        };
        return new TrafficRuleBook(json);
    }

    public JsonNode? Parameter(string name) => parameters[name]?["Value"];

    public TrafficSide Side => Enum.Parse<TrafficSide>((string)Parameter("TrafficSide")!);

    public UnsignedPriority Priority => Enum.Parse<UnsignedPriority>((string)Parameter("UnsignedIntersectionPriority")!);

    public bool StopForPedestriansOnCrossing => Parameter("StopForPedestriansOnCrossing")?.GetValue<bool>() ?? false;

    public Rule Rule(string id) => Rules.FirstOrDefault(r => r.RuleID == id) ?? throw new KeyNotFoundException($"No rule {id}.");

    public IEnumerable<Rule> RulesFor(string behaviour) => Rules.Where(r => r.Behaviour == behaviour);

    public Sign? SignNamed(string name) => Signs.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    // The general speed limit of a class of road, in m/s (null: the jurisdiction sets none here; a sign or the map gives it).
    public double? SpeedLimit(RoadClass road)
    {
        var p = parameters["SpeedLimits"];
        if (p?["Value"] is not JsonObject byClass || byClass[road.ToString()] is not JsonNode v) return null;
        var unit = (string?)p["Unit"] ?? "km/h";
        var x = v.GetValue<double>();
        return unit switch { "km/h" => x / 3.6, "mph" => x * 0.44704, "m/s" => x, _ => throw new InvalidOperationException($"Unknown speed unit {unit}.") };
    }

    // Where a road user approaching an intersection comes from, seen by another: its heading (the direction it travels in, radians, counter-clockwise, y up)
    // against mine. A vehicle coming from my right travels at my heading turned a quarter counter-clockwise.
    public static string Relative(double myHeading, double otherHeading)
    {
        var d = Angles.Diff(otherHeading, myHeading);
        return d > Math.PI / 4 && d < 3 * Math.PI / 4 ? "Right" : d < -Math.PI / 4 && d > -3 * Math.PI / 4 ? "Left" : Math.Abs(d) >= 3 * Math.PI / 4 ? "Oncoming" : "Same";
    }

    // Article 18, paragraph 4: at an intersection without signs, does a driver give way to a vehicle approaching with that heading?
    public bool GivesWayAtUnsigned(double myHeading, double otherHeading) => (Priority, Relative(myHeading, otherHeading)) switch
    {
        (UnsignedPriority.FromRight, "Right") => true,
        (UnsignedPriority.FromLeft, "Left") => true,
        _ => false
    };

    // THE RULES ON THE ROAD MAP: who gives way at the end of each lane that leads into an intersection. A priority sign at the end of a lane decides (Article 5:
    // GIVE WAY and STOP give way, PRIORITY ROAD does not); without one, the rule of the jurisdiction for unsigned intersections (Article 18, paragraph 4). The
    // approaches of one intersection are the lanes that lead into Junction lanes ending within 'radius' m of each other. Roundabouts keep what the map says.
    // Returns, per lane changed, the rule that decided.
    public Dictionary<string, string> ApplyTo(RoadMap map, IReadOnlyDictionary<string, string>? signsAtEnd = null, double radius = 30)
    {
        var decided = new Dictionary<string, string>();
        var approaches = map.Lanes.Values.Where(l => l.Kind == LaneKind.Road && l.Next.Any(n => map.Lanes[n].Kind == LaneKind.Junction)).ToList();
        foreach (var lane in approaches)
        {
            if (signsAtEnd is not null && signsAtEnd.TryGetValue(lane.Id, out var signName))
            {
                var effect = SignNamed(signName)?.Effect ?? throw new KeyNotFoundException($"No sign '{signName}' with an effect.");
                bool? gives = effect.Behaviour switch { "GiveWay" or "StopAndGiveWay" => true, "HasPriority" => false, _ => null };
                if (gives is bool g) { lane.YieldAtEnd = g; decided[lane.Id] = $"{signName} ({effect.Rule ?? "VC68-5-1"})"; continue; }
            }
            var end = lane.Centre.At(lane.Centre.Length);
            var heading = lane.Centre.HeadingAt(lane.Centre.Length);
            var others = approaches.Where(o => o != lane && o.Centre.At(o.Centre.Length).DistanceTo(end) <= radius).ToList();
            if (others.Count == 0) continue;
            var gives18 = others.Any(o => GivesWayAtUnsigned(heading, o.Centre.HeadingAt(o.Centre.Length)));
            lane.YieldAtEnd = gives18;
            decided[lane.Id] = $"{(gives18 ? "gives way" : "has priority")}, {Priority} (VC68-18-4)";
        }
        return decided;
    }

    // WHICH RULE A MOTION CONSTRAINT OF M3287 (Ams\Planning\TrajectoryCheck) APPLIES: what a violation breaks, in the words of the Convention.
    public static readonly IReadOnlyDictionary<string, string[]> MotionConstraintRules = new Dictionary<string, string[]>
    {
        ["C1"] = ["VC68-13-3", "VC68-7-1"],                 // distance from other road users
        ["C2"] = ["VC68-10-3"],                             // out of the lane
        ["C4"] = ["VC68-13-1"],                             // above the speed the route allows (curves, the end of the way)
        ["C5"] = ["VC68-13-5"],                             // above the speed limit
        ["C6"] = ["VC68-17-1"]                              // abrupt acceleration or braking
    };
}
