using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Aif.Tests;

// A SCENE THE MODULE KEEPS CAN BE DELETED ("delete the scene party"): the Scene goes, the sounds that were in it stay kept.
[Trait("Group", "Fast")]
[Trait("Blocks", "Yes")]
public class AsmDeleteSceneTests
{
    private static async Task<UserCommand> Run(AsmRig rig, UserCommandRQ data)
    {
        var command = AsmRig.Command(data);
        return (await rig.Ask(command)).Merged(command.UserCommandID);
    }

    [Fact]
    public async Task ASceneIsDeletedAndItsSoundsStayKept()
    {
        using var rig = new AsmRig();
        await rig.OpenAudio(AsmRig.Sound("violin"));
        await Run(rig, new UserCommandRQ { AddedObjects = new ObjectPlacements { ObjectID = "party", Objects = [AsmRig.Put("violin", 1)] } });
        var before = await Run(rig, new UserCommandRQ { ListedObjects = new ObjectListing { Kind = "Audio Scene" } });
        Assert.Contains(before.UserCommandRS!.ListedObjects!, l => l.ObjectID == "party");

        var gone = await Run(rig, new UserCommandRQ { DeletedObjects = new ObjectPlacements { Objects = [new ObjectPlacement { ObjectID = new ManagedObject { ObjectID = "party" } }] } });
        Assert.True(gone.UserCommandRS!.Actions!.Any(a => a.Action == "DeletedObjects" && a.Outcome == "Done"), string.Join("; ", gone.UserCommandRS.Actions!.Select(a => a.Action + " " + a.Outcome + " " + a.Reason)));

        var after = await Run(rig, new UserCommandRQ { ListedObjects = new ObjectListing { Kind = "Audio Scene" } });
        Assert.DoesNotContain(after.UserCommandRS!.ListedObjects ?? [], l => l.ObjectID == "party");
    }

    // N-50 (b), the author 2026-11-24: deleting a sound that is in a saved scene deletes it and takes it out of the scene, and the answer WARNS that it went from the scene too.
    [Fact]
    public async Task ASoundInASceneIsDeletedAndTheAnswerWarns()
    {
        using var rig = new AsmRig();
        await rig.OpenAudio(AsmRig.Sound("violin"));
        await rig.OpenAudio(AsmRig.Sound("cello", 1, 2));
        await Run(rig, new UserCommandRQ { AddedObjects = new ObjectPlacements { ObjectID = "party", Objects = [AsmRig.Put("violin", 1), AsmRig.Put("cello", 2)] } });

        var gone = await Run(rig, new UserCommandRQ { DeletedObjects = new ObjectPlacements { Objects = [new ObjectPlacement { ObjectID = new ManagedObject { ObjectID = "violin" } }] } });
        Assert.Equal("Done", gone.UserCommandRS!.Outcome);
        var guide = Mpai.Mmc.Jsi.ApplicationContext.Parse(File.ReadAllText(Path.Combine(Repository.Root, "Apps", "CAE", "ASM", "Access", "AsmApplicationContext.json")));
        var said = Mpai.Mmc.Jsi.ResponseFacts.Plain(System.Text.Json.Nodes.JsonNode.Parse(MpaiJson.ToJson(gone))!, guide);
        Assert.Equal("Done: deleted violin; it was in the scene party and is gone from it too.", said);

        // The scene is still there, with the cello only; the violin is no longer kept.
        var scene = await Run(rig, new UserCommandRQ { ListedObjects = new ObjectListing { Kind = "Audio Scene", ObjectID = "party" } });
        var members = scene.UserCommandRS!.ListedObjects!.Single(l => l.ObjectID == "party").Members!.Select(m => m.ObjectID).ToList();
        Assert.Equal(["cello"], members);
        var sounds = await Run(rig, new UserCommandRQ { ListedObjects = new ObjectListing { Kind = "Basic Audio Object" } });
        Assert.DoesNotContain(sounds.UserCommandRS!.ListedObjects ?? [], l => l.ObjectID == "violin");
    }
}
