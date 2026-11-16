using System.Text.Json.Nodes;

using Mpai.Pgm;

namespace Mpai.Aif.Tests;

// The door use case of PgmConditionTests with the language model of the reference software (Ollama; PGM_LLM_MODEL, default llama3.2:3b): the command, the bell, the walk, the
// door, the greeting. It records what the model answered at each step.
[Trait("Group", "Models")]
public class PgmConditionOllamaTests
{
    [SkippableFact]
    public async Task TheDoorOpensWhenTheBellRingsWithOllama()
    {
        var model = new OllamaModel(Environment.GetEnvironmentVariable("PGM_LLM_MODEL") is { Length: > 0 } m ? m : "llama3.2:3b");
        try { await model.AnswerAsync("Answer in JSON.", "ping", new JsonObject { ["type"] = "object" }); }
        catch (Exception e) { Skip.If(true, "Ollama is not available: " + e.Message); }
        var rig = new PgmRig(model, settings: new Dictionary<string, string>
        {
            [PrcAim.ItemsSetting] = "Door", [PrcAim.MLocationsSetting] = "Doorway", [BknAim.PersonaSetting] = "HostPersona"
        });
        rig.AnswerProcessActions = true;
        var auc = (AucAim)rig.Aims["1PGM-AUC-V1.0-I01"];
        rig.Write("PGM-HCM-V1.0", PgmRig.HumanCommand());
        rig.Write("OSD-BTO-V1.5", PgmAim.Text("Open the door if a bell rings and greet whoever is at the door."));
        await rig.RunAsync();
        Assert.NotNull(auc.PendingGoal);
        rig.Write("OSD-BAO-V1.5", new JsonObject
        {
            ["Header"] = "OSD-BAO-V1.5", ["BasicAudioObjectID"] = "bell-1",
            ["BasicAudioObjectData"] = new JsonArray(new JsonObject { ["Data"] = Convert.ToBase64String(Mpai.Mmm.Server.DoorUseCase.Bell()) })
        });
        await rig.RunAsync();
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "pgm-door-final.json"), rig.Latest.GetValueOrDefault("PGM-FRS-V1.0") ?? "");
        Assert.Equal(["MM-Animate", "MM-Animate", "Property Change", "MM-Animate"], auc.Requested);
        Assert.NotNull(rig.At("PAF-PAV-V1.6").Last()["SPOOrSPOID"]);
    }
}
