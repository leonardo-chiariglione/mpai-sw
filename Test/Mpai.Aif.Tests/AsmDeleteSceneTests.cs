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
}
