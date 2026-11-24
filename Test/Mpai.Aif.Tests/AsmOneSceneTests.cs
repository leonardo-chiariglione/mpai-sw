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
}
