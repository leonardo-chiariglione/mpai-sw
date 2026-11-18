using System.Text.Json.Nodes;

using Mpai.Pgm;

namespace Mpai.Aif.Tests;

// "OPEN THE DOOR IF A BELL RINGS AND GREET WHOEVER IS AT THE DOOR" (M3313) in the A-User Module, with a language model of the test's. Basic Knowledge finds the Condition and
// does nothing; A-User Control keeps the goal pending and asks Context Description to report the Audio Event; a knock is not reported, a bell is; Basic Knowledge is asked again,
// the Condition held; the Process Actions leave one at a time - MM-Animate of the Persona At the Doorway With the Portable Avatar of the walk, then, when it is answered,
// the Property Change of the Door, then MM-Animate With the Portable Avatar that says "Welcome". The Persona is never moved with MM-Move. All within the schemas of
// PGM-AUA V1.0 (the Condition is in the Constraints of the Goal).
[Trait("Group", "Fast")]
public class PgmConditionTests
{
    private const string Command = "Open the door if a bell rings and greet whoever is at the door.";

    // Answers as a good model should: the Condition while it does not hold; the walk, the door and the greeting when it holds.
    private sealed class ConditionalModel : ILanguageModel
    {
        public List<string> Systems { get; } = new();
        public Task<string> AnswerAsync(string system, string user, JsonObject schema)
        {
            Systems.Add(system);
            // Basic Knowledge's first question: is the command conditional?
            if (schema["properties"]?["condition"] is not null && schema["properties"]?["reply"] is null)
                return Task.FromResult(new JsonObject { ["condition"] = "if a bell rings", ["sound"] = "Bell" }.ToJsonString());
            var held = system.Contains("it has happened now");
            var answer = held
                ? new JsonObject
                {
                    ["reply"] = "Welcome!",
                    ["worldActions"] = new JsonArray(new JsonObject { ["verb"] = "Property Change", ["target"] = "door", ["property"] = "OpenState", ["value"] = "Open" }),
                    ["personaMoves"] = new JsonArray("Doorway")
                }
                : new JsonObject
                {
                    ["reply"] = "I will open the door when the bell rings.",
                    ["worldActions"] = new JsonArray(), ["personaMoves"] = new JsonArray(),
                    ["condition"] = new JsonObject { ["conditional"] = true, ["event"] = "a bell rings", ["sound"] = "Bell" }
                };
            if (schema["properties"]?["functions"] is not null) answer["functions"] = new JsonArray();
            return Task.FromResult(answer.ToJsonString());
        }
    }

    private static JsonObject Sound(string id, byte[] pcm) => new()
    {
        ["Header"] = "OSD-BAO-V1.5", ["BasicAudioObjectID"] = id,
        ["BasicAudioObjectData"] = new JsonArray(new JsonObject { ["Data"] = Convert.ToBase64String(pcm) })
    };

    private static void AssertAnimates(JsonNode pac, string stream, string? at)
    {
        Assert.Equal("MM-Animate", (string?)pac["DescrMetadata"]);
        var c = pac["RQ-Complements"]!.AsArray().OfType<JsonObject>().ToList();
        Assert.Contains(c, x => (string?)x["NilItem"] == "HostPersona");
        Assert.Contains(c, x => (string?)x["WithItem"] == stream);
        if (at is null) Assert.DoesNotContain(c, x => x.ContainsKey("AtItem"));
        else Assert.Contains(c, x => (string?)x["AtItem"] == at);
    }

    private static JsonObject Answer(JsonNode request)
    {
        var rs = (JsonObject)request.DeepClone();
        rs["PAStatus"] = new JsonArray(new JsonObject { ["Ack"] = "Ack" });
        return rs;
    }

    [Fact]
    public async Task TheDoorOpensWhenTheBellRings()
    {
        var model = new ConditionalModel();
        var rig = new PgmRig(model, settings: new Dictionary<string, string>
        {
            [PrcAim.ItemsSetting] = "Door", [PrcAim.MLocationsSetting] = "Doorway", [BknAim.PersonaSetting] = "HostPersona"
        });
        var auc = (AucAim)rig.Aims["1PGM-AUC-V1.0-I01"];

        // The command: accepted, pending; the A-User says what it will do - its Persona animated With the Portable Avatar of the answer; nothing else is asked of the M-Instance.
        rig.Write("PGM-HCM-V1.0", PgmRig.HumanCommand());
        rig.Write("OSD-BTO-V1.5", PgmAim.Text(Command));
        await rig.RunAsync();
        Assert.Equal("accepted", (string?)rig.At("PGM-HCM-V1.0").Single()["ExecutionResult"]);
        Assert.NotNull(auc.PendingGoal);
        Assert.Contains(BknAim.ConditionPrefix + "a bell rings", auc.PendingGoal!.ToJsonString());
        var answer = rig.At("PAF-PAV-V1.6").Single();
        var say = rig.At("MMM-PAC-V2.2").Single();
        AssertAnimates(say, (string)answer["PortableAvatarID"]!, at: null);
        rig.Write("MMM-PAC-V2.2", Answer(say));
        await rig.RunAsync();

        // A knock: heard, not the event.
        rig.Write("OSD-BAO-V1.5", Sound("knock-1", Mpai.Mmm.Server.DoorUseCase.Knock()));
        await rig.RunAsync();
        Assert.NotNull(auc.PendingGoal);
        Assert.Single(rig.At("MMM-PAC-V2.2"));
        // Heard by the Sub-AIM Audio Scene Description of Context Description: Basic Audio Scene Descriptors, valid, the Audio Event in their Descriptive Metadata.
        var cxt = (CxtAim)rig.Aims["1PGM-CXT-V1.0-I01"];
        Assert.NotNull(cxt.Asd);
        Assert.Equal("valid", PgmRig.Check(cxt.LastAudioScene!));
        Assert.Equal("Knock", AudioEvents.EventOf(cxt.LastAudioScene)?.Class);

        // The bell: the Condition holds; Basic Knowledge is asked again; the Persona walks to the Doorway - MM-Animate At the Doorway, With the Portable Avatar of the walk
        // (no speech: the motion is formed first).
        rig.Write("OSD-BAO-V1.5", Sound("bell-1", Mpai.Mmm.Server.DoorUseCase.Bell()));
        await rig.RunAsync();
        Assert.Equal("Bell", AudioEvents.EventOf(cxt.LastAudioScene)?.Class);
        Assert.Equal("valid", PgmRig.Check(cxt.LastAudioScene!));
        Assert.Null(auc.PendingGoal);
        Assert.Contains(model.Systems, x => x.Contains("it has happened now"));
        var walkAvatar = rig.At("PAF-PAV-V1.6").Last();
        Assert.Null(walkAvatar["SPOOrSPOID"]);
        Assert.Contains("Walk Body to Doorway", ((AufAim)rig.Aims["1PGM-AUF-V1.0-I01"]).LastBodyAnimation!.ToJsonString());
        var walk = rig.At("MMM-PAC-V2.2").Last();
        AssertAnimates(walk, (string)walkAvatar["PortableAvatarID"]!, at: "Doorway");
        Assert.DoesNotContain(rig.At("MMM-PAC-V2.2"), p => (string?)p["DescrMetadata"] == "MM-Move");

        // The walk over (the M-Instance answers when the Persona has reached the Doorway): the Property Change of the Door, OpenState Open.
        rig.Write("MMM-PAC-V2.2", Answer(walk));
        await rig.RunAsync();
        var change = rig.At("MMM-PAC-V2.2").Last();
        Assert.Equal("Property Change", (string?)change["DescrMetadata"]);
        Assert.Equal("Door", (string?)change["RQ-Complements"]![0]!["NilItem"]);
        Assert.Equal("Open", (string?)change["RQ-Complements"]![1]!["Item"]!["Properties"]![0]!["Value"]);
        Assert.Equal(2, rig.At("PAF-PAV-V1.6").Count());           // "Welcome" is not formed yet

        // The door open: A-User Formation forms the speech, and the Persona is animated With it.
        rig.Write("MMM-PAC-V2.2", Answer(change));
        await rig.RunAsync();
        var welcome = rig.At("PAF-PAV-V1.6").Last();
        Assert.NotNull(welcome["SPOOrSPOID"]);
        var speak = rig.At("MMM-PAC-V2.2").Last();
        AssertAnimates(speak, (string)welcome["PortableAvatarID"]!, at: null);
        rig.Write("MMM-PAC-V2.2", Answer(speak));
        await rig.RunAsync();
        Assert.Equal(["MM-Animate", "MM-Animate", "Property Change", "MM-Animate"], auc.Requested);
        var bad = rig.Seen.Where(x => x.Verdict != "valid").Select(x => $"{x.Aim} {x.Direction} {x.DataType}: {x.Verdict}").Distinct().ToList();
        Assert.True(bad.Count == 0, "Data not valid against its schema:\n" + string.Join("\n", bad));
    }
}
