using System.Text.Json.Nodes;

using Mpai.Core;
using Mpai.Core.OSD;

using Mpai.Mmc.Jsi;

namespace Mpai.Aif.Tests;

// A QUESTION ABOUT THE ROOM, THE LISTENER OR THE ACOUSTIC PROFILES is answered about that, from what the application listed - never with the list of the sounds of the scene
// ("how big is the room" had been answered "Nothing is in the scene."). The Kind asked is kept with the command and the answer is told by it.
[Trait("Group", "Fast")]
[Trait("Blocks", "Yes")]
public class JsiKindListingTests
{
    private static JsonObject Answer(string listed) => JsonNode.Parse("{\"UserCommandRS\":{\"Outcome\":\"Done\",\"ListedObjects\":[" + listed + "]}}")!.AsObject();

    [Fact]
    public void TheRoomIsToldByItsSize()
    {
        var room = Answer("{\"ObjectID\":\"stage\",\"Kind\":\"Closed Space\",\"ClosedSpace\":{\"ClosedSpaceID\":\"room\",\"Shape\":[{\"SimpleLocationData\":[{\"RightParallelepiped\":{\"Dimensions\":\"3D\",\"CartPositions\":[[-3,-2,0],[3,-2,0],[3,2,0],[-3,2,0],[-3,-2,3],[3,-2,3],[3,2,3],[-3,2,3]]}}]}]}}");
        Assert.Equal("The room is 6 by 4 metres and 3 metres high.", JsiAimProcessor.TellKind("Closed Space", room));
        Assert.Equal("There is no room in the scene yet.", JsiAimProcessor.TellKind("Closed Space", Answer("{\"ObjectID\":\"stage\",\"Kind\":\"Closed Space\"}")));
    }

    [Fact]
    public void TheListenerIsToldByItsPlace()
    {
        var listener = Answer("{\"ObjectID\":\"stage\",\"Kind\":\"Listener\",\"UserPoV\":{\"CartPosition\":[1,2,0],\"Orientation\":[90,0,0]}}");
        Assert.Equal("The listener is at x 1, y 2, z 0, turned 90 degrees.", JsiAimProcessor.TellKind("Listener", listener));
        Assert.Equal("The scene has no listener position yet.", JsiAimProcessor.TellKind("Listener", Answer("{\"ObjectID\":\"stage\",\"Kind\":\"Listener\"}")));
    }

    [Fact]
    public void TheAcousticProfilesKeptAreNamed()
    {
        Assert.Equal("The Acoustic Profiles kept: warm, hall.", JsiAimProcessor.TellKind("Acoustic Profiles", Answer("{\"ObjectID\":\"warm\",\"Kind\":\"Acoustic Profile\"},{\"ObjectID\":\"hall\",\"Kind\":\"Acoustic Profile\"}")));
        Assert.Equal("The Module keeps no Acoustic Profile.", JsiAimProcessor.TellKind("Acoustic Profiles", Answer("")));
        Assert.Equal("The Acoustic Profile is shown in the panel.", JsiAimProcessor.TellKind("Acoustic Profile", Answer("{\"ObjectID\":\"stage\",\"Kind\":\"Acoustic Profile\"}")));
    }

    // What the Audio Scene Editing AIM really lists for the room and the listener (not a copy of the shape): told as it is.
    [Fact]
    public async Task TheRoomAndTheListenerTheModuleListsAreToldRight()
    {
        using var rig = new AsmRig();
        await rig.OpenAudio(AsmRig.Sound("violin"));
        const string room = "{\"Header\":\"OSD-CSP-V1.5\",\"ClosedSpaceID\":\"room\",\"Shape\":[{\"Header\":\"OSD-SLC-V1.5\",\"SimpleLocationData\":[{\"RightParallelepiped\":{\"Header\":\"OSD-RPP-V1.5\",\"Dimensions\":\"3D\",\"CartPositions\":[[-3,-2,0],[3,-2,0],[-3,2,0],[-3,-2,3]]}}]}]}";
        async Task<UserCommand> Run(UserCommandRQ data) { var c = AsmRig.Command(data); return (await rig.Ask(c)).Merged(c.UserCommandID); }
        await Run(new UserCommandRQ { AddedObjects = new ObjectPlacements { ObjectID = "stage", Objects = [AsmRig.Put("violin", 1)] } });
        await Run(new UserCommandRQ { ClosedSpace = new JsonArray(JsonNode.Parse(room)) });

        var listedRoom = await Run(new UserCommandRQ { ListedObjects = new ObjectListing { Kind = "Closed Space", ObjectID = "stage" } });
        var roomJson = JsonNode.Parse("{\"UserCommandRS\":{\"ListedObjects\":" + MpaiJson.ToJson(listedRoom.UserCommandRS!.ListedObjects!) + "}}")!.AsObject();
        Assert.Equal("The room is 6 by 4 metres and 3 metres high.", JsiAimProcessor.TellKind("Closed Space", roomJson));

        var listedListener = await Run(new UserCommandRQ { ListedObjects = new ObjectListing { Kind = "Listener", ObjectID = "stage" } });
        var listenerJson = JsonNode.Parse("{\"UserCommandRS\":{\"ListedObjects\":" + MpaiJson.ToJson(listedListener.UserCommandRS!.ListedObjects!) + "}}")!.AsObject();
        Assert.StartsWith("The listener is at x ", JsiAimProcessor.TellKind("Listener", listenerJson));
        Assert.DoesNotContain("no listener position", JsiAimProcessor.TellKind("Listener", listenerJson));
    }
}
