using System.Text.Json;
using Mpai.Cav.Driving;
using Mpai.Cav.TrafficRules;

namespace Mpai.Aif.Tests;

// THE TRAFFIC RULES OF THE CAV (2026-11-19): the Convention on Road Traffic of 1968 (Chapter II and Article 34 bis) as data (AIMs\CAV\V2.0\TrafficRules\Data), composed
// for a jurisdiction into a CAV-TRR-V2.0 valid against schemas\CAV2\V2.0\data\TrafficRules.json, and applied to the road map: who gives way at an intersection.
[Trait("Group", "Fast")]
public class CavTrafficRulesTests
{
    public static TheoryData<string> Jurisdictions => ["RHT", "LHT", "IT", "GB"];

    [Theory, MemberData(nameof(Jurisdictions))]
    public void TheTrafficRulesOfEachJurisdictionAreValidAgainstTheSchema(string code)
    {
        var book = TrafficRuleBook.Load(code);
        var schemas = AIF.Metadata.PublishedSchemas.At(Repository.Schemas);
        var schema = schemas[Path.GetFullPath(Path.Combine(Repository.Schemas, "CAV2", "V2.0", "data", "TrafficRules.json"))];
        using var doc = JsonDocument.Parse(book.Json.ToJsonString());
        lock (AIF.Metadata.PublishedSchemas.Lock)
        {
            var result = schema.Evaluate(doc.RootElement, new Json.Schema.EvaluationOptions { OutputFormat = Json.Schema.OutputFormat.List });
            Assert.True(result.IsValid, string.Join("\n", result.Details.Where(d => d.Errors is not null).SelectMany(d => d.Errors!.Select(e => $"{d.InstanceLocation}: {e.Value}")).Take(10)));
        }
    }

    [Fact]
    public void TheRulesCoverChapterTwoAndEveryNameTheyUseExists()
    {
        var book = TrafficRuleBook.Load("RHT");
        var articles = book.Rules.Select(r => r.Article).ToHashSet();
        foreach (var a in Enumerable.Range(5, 30)) Assert.Contains(a.ToString(), articles);      // Articles 5 to 34
        Assert.Contains("34bis", articles);
        Assert.Equal(book.Rules.Count, book.Rules.Select(r => r.RuleID).Distinct().Count());
        var parameters = book.Json["Parameters"]!.AsObject().Select(p => p.Key).ToHashSet();
        foreach (var r in book.Rules) foreach (var p in r.Parameters) Assert.True(parameters.Contains(p), $"{r.RuleID} uses the parameter {p}, which no profile has");
        var ids = book.Rules.Select(r => r.RuleID).ToHashSet();
        foreach (var s in book.Signs.Where(s => s.Effect?.Rule is not null)) Assert.True(ids.Contains(s.Effect!.Rule!), $"{s.SignID} applies {s.Effect.Rule}, which is no rule");
        foreach (var (_, rules) in TrafficRuleBook.MotionConstraintRules) Assert.All(rules, id => Assert.Contains(id, ids));
        Assert.Equal("VC68-34bis", book.Rule("VC68-8-1").Text.Length > 0 ? (string?)book.Json["Rules"]!.AsArray().First(r => (string)r!["RuleID"]! == "VC68-8-1")!["SatisfiedBy"] : null);
    }

    [Fact]
    public void AProfileCompletesTheOneItIsBasedOn()
    {
        var it = TrafficRuleBook.Load("IT");
        Assert.Equal(TrafficSide.Right, it.Side);
        Assert.Equal(UnsignedPriority.FromRight, it.Priority);                           // from RHT: Article 18, paragraph 4 (a)
        Assert.Equal(50 / 3.6, it.SpeedLimit(RoadClass.BuiltUp)!.Value, 6);
        Assert.Equal(130 / 3.6, it.SpeedLimit(RoadClass.Motorway)!.Value, 6);
        Assert.True(it.StopForPedestriansOnCrossing);                                    // Italy's own, over the Convention's 'if necessary'
        Assert.Equal(3500, it.Parameter("HeavyVehicleMass")!.GetValue<int>());           // the Convention's figure, from RHT
        var gb = TrafficRuleBook.Load("GB");
        Assert.Equal(TrafficSide.Left, gb.Side);
        Assert.Equal(UnsignedPriority.NoGeneralRule, gb.Priority);                       // from LHT: Article 18, paragraph 4 (b)
        Assert.Equal(30 * 0.44704, gb.SpeedLimit(RoadClass.BuiltUp)!.Value, 6);          // mph
        Assert.Null(TrafficRuleBook.Load("RHT").SpeedLimit(RoadClass.Rural));            // the Convention sets none
    }

    // A T-junction at the origin: a road from the west (eastbound), one from the east (westbound), one from the south (northbound); each approach ends 8 m from the
    // centre, a Junction lane goes on from it.
    private static RoadMap TJunction()
    {
        var map = new RoadMap();
        map.Add(new Lane { Id = "FromWest", Centre = Polyline.Straight(new(-100, -1.75), new(-8, -1.75)) });
        map.Add(new Lane { Id = "FromEast", Centre = Polyline.Straight(new(100, 1.75), new(8, 1.75)) });
        map.Add(new Lane { Id = "FromSouth", Centre = Polyline.Straight(new(1.75, -100), new(1.75, -8)) });
        map.Add(new Lane { Id = "J1", Centre = Polyline.Straight(new(-8, -1.75), new(8, -1.75)), Kind = LaneKind.Junction });
        map.Add(new Lane { Id = "J2", Centre = Polyline.Straight(new(8, 1.75), new(-8, 1.75)), Kind = LaneKind.Junction });
        map.Add(new Lane { Id = "J3", Centre = Polyline.Straight(new(1.75, -8), new(1.75, 8)), Kind = LaneKind.Junction });
        map.Connect("FromWest", "J1"); map.Connect("FromEast", "J2"); map.Connect("FromSouth", "J3");
        return map;
    }

    [Fact]
    public void AtAnUnsignedIntersectionTheVehicleFromTheRightHasPriorityWhereTrafficKeepsToTheRight()
    {
        var map = TJunction();
        var decided = TrafficRuleBook.Load("RHT").ApplyTo(map);
        Assert.True(map.Lanes["FromWest"].YieldAtEnd);        // the northbound vehicle comes from its right
        Assert.True(map.Lanes["FromSouth"].YieldAtEnd);       // the westbound vehicle comes from its right
        Assert.False(map.Lanes["FromEast"].YieldAtEnd);       // nobody comes from its right
        Assert.All(decided.Values, why => Assert.Contains("VC68-18-4", why));
    }

    [Fact]
    public void ASignDecidesAndWhereTrafficKeepsToTheLeftWithoutADomesticRuleOnlySignsDo()
    {
        var map = TJunction();
        var signs = new Dictionary<string, string> { ["FromSouth"] = "STOP", ["FromWest"] = "PRIORITY ROAD", ["FromEast"] = "PRIORITY ROAD" };
        var decided = TrafficRuleBook.Load("RHT").ApplyTo(map, signs);
        Assert.True(map.Lanes["FromSouth"].YieldAtEnd); Assert.False(map.Lanes["FromWest"].YieldAtEnd); Assert.False(map.Lanes["FromEast"].YieldAtEnd);
        Assert.StartsWith("STOP", decided["FromSouth"]);
        var left = TJunction();
        TrafficRuleBook.Load("GB").ApplyTo(left);
        Assert.All(left.Lanes.Values.Where(l => l.Kind == LaneKind.Road), l => Assert.False(l.YieldAtEnd));   // NoGeneralRule: nothing without signs
        Assert.Equal("Right", TrafficRuleBook.Relative(0, Math.PI / 2));                  // heading east, a vehicle heading north comes from the right
        Assert.Equal("Left", TrafficRuleBook.Relative(0, -Math.PI / 2));
        Assert.Equal("Oncoming", TrafficRuleBook.Relative(0, Math.PI));
    }
}
