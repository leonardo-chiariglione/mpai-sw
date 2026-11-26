using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Aif.Tests;

// PLACING SOUNDS IN A SCENE MAKES ONE SCENE: the second sound is put in the scene "stage", and no other Scene or Audio Object ("stage 2") appears.
[Trait("Group", "Fast")]
[Trait("Blocks", "Yes")]
public class AsmOneSceneTests
{
    private static async Task<UserCommand> Run(AsmRig rig, UserCommandRQ data)
    {
        var command = AsmRig.Command(data);
        return (await rig.Ask(command)).Merged(command.UserCommandID);
    }

    [Fact]
    public async Task TheSecondSoundGoesIntoTheSameSceneAndNothingIsMadeOfTheName()
    {
        using var rig = new AsmRig();
        await rig.OpenAudio(AsmRig.Sound("violin"));
        await rig.OpenAudio(AsmRig.Sound("cello"));
        var first = await Run(rig, new UserCommandRQ { AddedObjects = new ObjectPlacements { ObjectID = "stage", Objects = [AsmRig.Put("violin", 1)] } });
        var second = await Run(rig, new UserCommandRQ { AddedObjects = new ObjectPlacements { ObjectID = "stage", Objects = [AsmRig.Put("cello", -1)] } });
        Assert.Equal("Done", first.UserCommandRS!.Outcome);
        Assert.Equal("Done", second.UserCommandRS!.Outcome);

        var all = await Run(rig, new UserCommandRQ { ListedObjects = new ObjectListing { Kind = "All" } });
        var names = all.UserCommandRS!.ListedObjects!.Select(o => o.ObjectID + " (" + o.Kind + ")").ToList();
        Assert.DoesNotContain(names, n => n.StartsWith("stage 2"));
        Assert.Contains(names, n => n.StartsWith("stage ("));
        Assert.DoesNotContain(second.UserCommandRS.Actions!, a => (a.ObjectID ?? "").StartsWith("stage ") || (a.ResultID ?? "").StartsWith("stage "));
    }

    // N-50 (o): the first add into a scene that does not exist yet, as the JSON Interface now sends it (NewScene with the name, AddedObjects with none): ONE Audio Scene "stage"
    // holding the violin, and no Audio Object "stage".
    [Fact]
    public async Task TheFirstAddMakesTheSceneAndNoAudioObjectOfItsName()
    {
        using var rig = new AsmRig();
        await rig.OpenAudio(AsmRig.Sound("violin"));
        var first = await Run(rig, new UserCommandRQ { NewScene = "stage", AddedObjects = new ObjectPlacements { Objects = [AsmRig.Put("violin", 1)] } });
        Assert.Equal("Done", first.UserCommandRS!.Outcome);
        var all = await Run(rig, new UserCommandRQ { ListedObjects = new ObjectListing { Kind = "All" } });
        var listed = all.UserCommandRS!.ListedObjects!;
        Assert.Contains(listed, o => o.ObjectID == "stage" && o.Kind == "Audio Scene");
        Assert.DoesNotContain(listed, o => o.ObjectID.StartsWith("stage") && o.Kind != "Audio Scene");
        var scene = await Run(rig, new UserCommandRQ { ListedObjects = new ObjectListing { Kind = "Audio Scene", ObjectID = "stage" } });
        Assert.Equal(["violin"], scene.UserCommandRS!.ListedObjects!.Single(l => l.ObjectID == "stage").Members!.Select(m => m.ObjectID).ToList());
    }

    // N-50 (night run of the window): a sound put in the scene again, while it is already a member, is placed where the add says - ONE member "cello", not two.
    [Fact]
    public async Task ASoundAddedAgainIsPlacedNotDoubled()
    {
        using var rig = new AsmRig();
        await rig.OpenAudio(AsmRig.Sound("cello"));
        await Run(rig, new UserCommandRQ { AddedObjects = new ObjectPlacements { ObjectID = "stage", Objects = [AsmRig.Put("cello", 0)] } });
        var again = await Run(rig, new UserCommandRQ { AddedObjects = new ObjectPlacements { Objects = [AsmRig.Put("cello", 2)] } });
        Assert.Equal("Done", again.UserCommandRS!.Outcome);
        var scene = await Run(rig, new UserCommandRQ { ListedObjects = new ObjectListing { Kind = "Audio Scene", ObjectID = "stage" } });
        var members = scene.UserCommandRS!.ListedObjects!.Single(l => l.ObjectID == "stage").Members!;
        Assert.Single(members);
        Assert.Equal(2, members[0].PoV!.Position!.CartPosition![0]);
    }
}
