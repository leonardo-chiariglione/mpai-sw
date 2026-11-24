using System.Text.Json.Nodes;

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
}
