using System.Text.Json.Nodes;

using AIF.Controller;
using AIF.Store;

using Mpai.Pgm;

namespace Mpai.Aif.Tests;

// PGM-AUA: THE LEVELS OF BASIC KNOWLEDGE (M3308 3.5). Level 0: no Communicative Functions; Level 1: functions; Level 2: functions that may carry their Behaviour. A-User Control requests
// a Level no higher than Basic Knowledge declares; A-User Formation realises every Level, choosing and timing a Behaviour for each function that carries none, and reports, for each
// FunctionID, Realised, Modified or Not Realised. Every Data validates against its schema.
[Trait("Group", "Fast")]
[Trait("Blocks", "Yes")]
public class PgmLevelsTests
{
    private const string Doors = "go and open the doors";

    internal static async Task<PgmRig> RunAsync(ILanguageModel model, string words, int aucLevel, int bknLevel, string? personaBehaviours = null)
    {
        var settings = new Dictionary<string, string> { [AucAim.LevelSetting] = aucLevel.ToString(), [BknAim.LevelSetting] = bknLevel.ToString() };
        if (personaBehaviours is not null) settings[BplAim.BehavioursSetting] = personaBehaviours;
        var rig = new PgmRig(model, settings: settings);
        rig.Write("PGM-HCM-V1.0", PgmRig.HumanCommand());
        rig.Write("OSD-BTO-V1.5", PgmAim.Text(words));
        await rig.RunAsync();
        return rig;
    }

    private static void AssertAllValid(PgmRig rig)
    {
        var bad = rig.Seen.Where(s => s.Verdict != "valid").Select(s => $"{s.Aim} {s.Direction} {s.DataType}: {s.Verdict}").Distinct().ToList();
        Assert.True(bad.Count == 0, "Data not valid against its schema:\n" + string.Join("\n", bad));
    }

    internal static JsonObject Final(PgmRig rig) => (JsonObject)JsonNode.Parse(rig.Latest["PGM-FRS-V1.0"])!;
    internal static AucAim Auc(PgmRig rig) => (AucAim)rig.Aims["1PGM-AUC-V1.0-I01"];
    internal static AufAim Auf(PgmRig rig) => (AufAim)rig.Aims["1PGM-AUF-V1.0-I01"];
    private static string BodyOf(PgmRig rig) => rig.At("PAF-PAV-V1.6").Single()["BodyDescriptorsObject"]?.ToJsonString() ?? "";

    // Level 0: the Final Response has no functions; A-User Formation moves the avatar from the speech and the Personal Status only; nothing to report per function.
    [Fact]
    public async Task Level0()
    {
        var rig = await RunAsync(new ScriptedModel(), Doors, aucLevel: 0, bknLevel: 1);
        AssertAllValid(rig);
        var final = Final(rig);
        Assert.False(final.ContainsKey("CommunicativeFunctions"));
        Assert.Equal(0, (int?)Auc(rig).FunctionsOutcome?["LevelProduced"]);
        Assert.Null(Auc(rig).FunctionStatus);
        Assert.Null(Auf(rig).LastPlan);
        Assert.DoesNotContain("Point", BodyOf(rig));
    }

    // Level 1: functions without Behaviour; Behaviour Planning chooses and times one for each; all are Realised.
    [Fact]
    public async Task Level1()
    {
        var rig = await RunAsync(new ScriptedModel(), Doors, aucLevel: 1, bknLevel: 1);
        AssertAllValid(rig);
        var final = Final(rig);
        Assert.Equal(1, (int?)final["FunctionLevel"]);
        var functions = final["CommunicativeFunctions"]!.AsArray();
        Assert.Equal(["Deictic", "Affirmation"], functions.Select(f => (string)f!["Function"]!).ToArray());
        Assert.All(functions, f => Assert.False(f!.AsObject().ContainsKey("Behaviour")));
        // "the doors" are words 5 and 6 of "I am going to open the doors."
        Assert.Equal(5, (int?)functions[0]!["Anchor"]!["FirstWord"]);
        Assert.Equal(6, (int?)functions[0]!["Anchor"]!["LastWord"]);
        Assert.Equal("doors", (string?)functions[0]!["Target"]!["ID"]);

        var status = Auc(rig).FunctionStatus!;
        Assert.Equal(["Realised", "Realised"], status.Select(s => (string)s!["Status"]!).ToArray());
        Assert.Equal("Point", (string?)status[0]!["Behaviour"]);
        Assert.Equal("Nod", (string?)status[1]!["Behaviour"]);

        // Timed on the words: the pointing starts a little before "the" and ends after "doors.", within the speech.
        var plan = Auf(rig).LastPlan!["Functions"]!.AsArray();
        var times = plan[0]!["Times"]!;
        var seconds = Auf(rig).LastSpeechSeconds!.Value;
        Assert.True((double)times["Start"]! < (double)times["Peak"]! && (double)times["Peak"]! < (double)times["End"]!);
        var words = Communicative.WordTimes("I am going to open the doors.", seconds);
        Assert.InRange((double)times["Start"]!, words[5].Start - 0.21, words[5].Start - 0.19);
        Assert.InRange((double)times["End"]!, words[6].End + 0.19, words[6].End + 0.21);
        Assert.Contains("Point RightHand", BodyOf(rig));
        Assert.Contains("MM-Animate", BodyOf(rig));
    }

    // Level 2: the functions carry their Behaviour; A-User Formation realises it as given.
    [Fact]
    public async Task Level2()
    {
        var rig = await RunAsync(new ScriptedModel(), Doors, aucLevel: 2, bknLevel: 2);
        AssertAllValid(rig);
        var final = Final(rig);
        Assert.Equal(2, (int?)final["FunctionLevel"]);
        Assert.Equal(["Point", "Nod"], final["CommunicativeFunctions"]!.AsArray().Select(f => (string)f!["Behaviour"]!["Name"]!).ToArray());
        Assert.All(Auc(rig).FunctionStatus!, s => Assert.Equal("Realised", (string?)s!["Status"]));
    }

    // A-User Control asks for Level 2 of a Basic Knowledge that declares Level 1: the Final Response is Level 1, the AUC-BKN Response says why, and A-User Control never asks
    // above Level 1 again.
    [Fact]
    public async Task ALowerLevelThanRequested()
    {
        var rig = await RunAsync(new ScriptedModel(), Doors, aucLevel: 2, bknLevel: 1);
        AssertAllValid(rig);
        Assert.Equal(1, (int?)Final(rig)["FunctionLevel"]);
        var outcome = Auc(rig).FunctionsOutcome!;
        Assert.Equal(2, (int?)outcome["LevelRequested"]);
        Assert.Equal(1, (int?)outcome["LevelProduced"]);
        Assert.Contains("up to Level 1", (string?)outcome["Reason"]);
        Assert.Equal(1, Auc(rig).BknLevelDeclared);

        rig.Write("PGM-HCM-V1.0", PgmRig.HumanCommand("hc-2"));
        rig.Write("OSD-BTO-V1.5", PgmAim.Text(Doors));
        await rig.RunAsync();
        Assert.Equal(1, Auc(rig).LevelRequested);
    }

    // A Required function that the Persona cannot perform is Not Realised, with the reason; A-User Formation says it achieved the formation differently.
    [Fact]
    public async Task ARequiredFunctionNotRealised()
    {
        var rig = await RunAsync(new ScriptedModel(), Doors, aucLevel: 1, bknLevel: 1, personaBehaviours: "Nod,Smile");
        AssertAllValid(rig);
        var status = Auc(rig).FunctionStatus!;
        Assert.Equal("NotRealised", (string?)status[0]!["Status"]);
        Assert.Contains("Point", (string?)status[0]!["Reason"]);
        Assert.Equal("Realised", (string?)status[1]!["Status"]);
        var aufResponse = rig.Seen.Any(s => s.Aim == "1PGM-AUF-V1.0-I01" && s.DataType == "PGM-UCF-V1.0" && s.Direction == "Output");
        Assert.True(aufResponse);
        Assert.DoesNotContain("Point", BodyOf(rig));
    }

    // Behaviour Planning on a Level 2 Communicative Functions written by hand: the Behaviour given is realised when the Persona has it, replaced by another of its Function
    // (Modified) when it has not, and an Optional function the Persona cannot perform is left out.
    [Fact]
    public async Task BehaviourPlanningOfALevel2FinalResponse()
    {
        var store = new AmdStore(Repository.Amds); store.Scan();
        var bpl = (BplAim)new PgmProvider(store, new ScriptedModel()).Make("1PGM-BPL-V1.0-I01", new Dictionary<string, string> { [BplAim.BehavioursSetting] = "HeadPoint,LookAt,Nod,Smile" });
        JsonObject F(string id, string function, string? behaviour, string requirement, int first, int last)
        {
            var f = new JsonObject
            {
                ["FunctionID"] = id, ["Function"] = function, ["Anchor"] = new JsonObject { ["Position"] = "During", ["FirstWord"] = first, ["LastWord"] = last },
                ["Intensity"] = 0.7, ["Requirement"] = requirement
            };
            if (behaviour is not null) f["Behaviour"] = new JsonObject { ["Name"] = behaviour };
            return f;
        }
        var cfn = new JsonObject
        {
            ["Header"] = "PGM-CFN-V1.0", ["CommunicativeFunctionsID"] = "c", ["CommunicativeFunctionsTime"] = PgmAim.Time(), ["Level"] = 2,
            ["Functions"] = new JsonArray(F("f1", "Affirmation", "Nod", "Required", 0, 0), F("f2", "Deictic", "Point", "Required", 5, 6), F("f3", "Iconic", "Trace", "Optional", 6, 6))
        };
        Assert.Equal("valid", PgmRig.Check(cfn));
        var request = Mpai.Pgm.Auc.Make(Mpai.Pgm.Auc.Auf, Mpai.Pgm.Auc.Request, new PgmAim.Session("m", "u", "s"));
        var result = await bpl.ProcessAsync(new Message
        {
            Ports = new Dictionary<string, string>
            {
                [bpl.InputKey("PGM-CFN-V1.0")] = cfn.ToJsonString(), [bpl.InputKey("OSD-BTO-V1.5")] = PgmAim.Text("Yes, I am going to open the doors.").ToJsonString(),
                [bpl.InputKey("PGM-UCF-V1.0")] = request.ToJsonString()
            }
        });
        foreach (var (_, json) in result.Ports) Assert.Equal("valid", PgmRig.Check(JsonNode.Parse(json)!));
        var status = JsonNode.Parse(result.Ports[bpl.OutputKey("PGM-UCF-V1.0")])!["FunctionStatus"]!.AsArray();
        Assert.Equal(["Realised", "Modified", "NotRealised"], status.Select(s => (string)s!["Status"]!).ToArray());
        Assert.Equal("HeadPoint", (string?)status[1]!["Behaviour"]);
        Assert.StartsWith("optional", (string?)status[2]!["Reason"]);
        Assert.Equal(2, JsonNode.Parse(result.Ports[bpl.OutputKey("PGM-CFN-V1.0")])!["Functions"]!.AsArray().Count);
    }

    // CONFORMANCE of a Final Response: the schema refuses a Behaviour at Level 1 and functions without a Level; the checks of M3308 3.5 refuse a Level above the one requested, an
    // Anchor outside the Text, a Target that the PC Prompt does not give, a Function not allowed and a Behaviour of another Function.
    [Fact]
    public void ConformanceOfAFinalResponse()
    {
        JsonObject Fr(int level, params JsonObject[] functions) => new()
        {
            ["Header"] = "PGM-FRS-V1.0", ["MInstance"] = "m", ["UEnvironment"] = "u", ["SessionID"] = "s", ["FinalResponseID"] = "f", ["FinalResponseTime"] = PgmAim.Time(),
            ["Text"] = PgmAim.Text("Look at the doors."), ["FunctionLevel"] = level, ["CommunicativeFunctions"] = new JsonArray(functions)
        };
        JsonObject F(string function, int first, int last, string? target = null, string? behaviour = null)
        {
            var f = new JsonObject
            {
                ["FunctionID"] = "f" + function, ["Function"] = function, ["Anchor"] = new JsonObject { ["Position"] = "During", ["FirstWord"] = first, ["LastWord"] = last },
                ["Intensity"] = 0.5, ["Requirement"] = "Required"
            };
            if (target is not null) f["Target"] = new JsonObject { ["Kind"] = "Item", ["ID"] = target };
            if (behaviour is not null) f["Behaviour"] = new JsonObject { ["Name"] = behaviour };
            return f;
        }
        string[] targets = ["doors"];

        var good = Fr(1, F("Deictic", 2, 3, "doors"));
        Assert.Equal("valid", PgmRig.Check(good));
        Assert.Empty(Communicative.Check(good, 1, Communicative.Functions, targets));

        Assert.NotEqual("valid", PgmRig.Check(Fr(1, F("Deictic", 2, 3, "doors", "Point"))));                        // a Behaviour at Level 1
        var noLevel = Fr(1, F("Deictic", 2, 3, "doors")); noLevel.Remove("FunctionLevel");
        Assert.NotEqual("valid", PgmRig.Check(noLevel));                                                                // functions without a Level
        Assert.NotEqual("valid", PgmRig.Check(Fr(2, F("Deictic", 2, 3, "doors", "Wave"))));                            // Wave is not a Deictic Behaviour (schema)

        Assert.Contains(Communicative.Check(Fr(2, F("Deictic", 2, 3, "doors", "Point")), 1, Communicative.Functions, targets), p => p.Contains("higher than the Level requested"));
        Assert.Contains(Communicative.Check(Fr(1, F("Deictic", 2, 9, "doors")), 1, Communicative.Functions, targets), p => p.Contains("not within"));
        Assert.Contains(Communicative.Check(Fr(1, F("Deictic", 2, 3, "window")), 1, Communicative.Functions, targets), p => p.Contains("not in the PC Prompt"));
        Assert.Contains(Communicative.Check(Fr(1, F("Deictic", 2, 3, "doors")), 1, ["Gaze"], targets), p => p.Contains("not allowed"));
    }

    // Basic Knowledge's own Final Responses conform, at every Level (with the model of the test).
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task BasicKnowledgeConforms(int level)
    {
        var rig = await RunAsync(new ScriptedModel(), Doors, aucLevel: level, bknLevel: 2);
        var final = Final(rig);
        Assert.True(Communicative.LevelOf(final) <= level);
        Assert.Empty(Communicative.Check(final, level, Communicative.Functions, ["doors", "window", "table", "lamp"]));
    }

    // The closed lists of the code are those of the schema.
    [Fact]
    public void TheClosedListsAreThoseOfTheSchema()
    {
        var schema = JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Schemas, "PGM1", "V1.0", "data", "CommunicativeFunctions.json")))!;
        string[] Enum(string def) => schema["$defs"]![def]!["enum"]!.AsArray().Select(x => (string)x!).ToArray();
        Assert.Equal(Enum("FunctionName"), Communicative.Functions);
        Assert.Equal(Enum("BehaviourName"), Communicative.Behaviours);
        foreach (var rule in schema["$defs"]!["Function"]!["allOf"]!.AsArray().Skip(1))
        {
            var function = (string)rule!["if"]!["properties"]!["Function"]!["const"]!;
            var allowed = rule["then"]!["properties"]!["Behaviour"]!["properties"]!["Name"]!["enum"]!.AsArray().Select(x => (string)x!).ToArray();
            Assert.Equal(allowed, Communicative.BehavioursOf[function]);
        }
    }

    // The times of the words fill the speech, in order.
    [Fact]
    public void WordTimesFillTheSpeech()
    {
        var times = Communicative.WordTimes("I am going to open the doors.", 2.0);
        Assert.Equal(7, times.Length);
        Assert.Equal(0, times[0].Start);
        Assert.Equal(2.0, times[^1].End, 2);
        for (var i = 1; i < times.Length; i++) Assert.Equal(times[i - 1].End, times[i].Start, 3);
    }
}
