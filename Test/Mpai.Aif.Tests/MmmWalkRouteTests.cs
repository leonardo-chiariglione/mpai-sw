using Mpai.Mmm;

namespace Mpai.Aif.Tests;

// THE WALK GOES ROUND THE FURNITURE (N-37 9): the M-Instance computes the way of an MM-Animate At an M-Location round the Items that have a footprint - the door use case's
// table between the Host's Persona and the door - and the Persona faces where it goes along it.
[Trait("Group", "Fast")]
public class MmmWalkRouteTests
{
    private static readonly (double[] At, double[] Size)[] Table = [([0.8, -0.4], [1.8, 0.7])];

    private static bool Clear(List<double[]> way)
    {
        // every stretch keeps out of the table grown by the clearance (sampled every centimetre)
        double x0 = 0.8 - 0.9 - MInstance.Clearance + 0.005, x1 = 0.8 + 0.9 + MInstance.Clearance - 0.005, y0 = -0.4 - 0.35 - MInstance.Clearance + 0.005, y1 = -0.4 + 0.35 + MInstance.Clearance - 0.005;
        for (var k = 1; k < way.Count; k++)
            for (var f = 0.0; f <= 1; f += 0.002)
            {
                var x = way[k - 1][0] + (way[k][0] - way[k - 1][0]) * f; var y = way[k - 1][1] + (way[k][1] - way[k - 1][1]) * f;
                if (x > x0 && x < x1 && y > y0 && y < y1) return false;
            }
        return true;
    }

    [Fact]
    public void TheWayToTheDoorGoesRoundTheTable()
    {
        var way = MInstance.Route([1.6, 1.4], [0, -2.2], Table);
        Assert.True(way.Count > 2, "the straight line crosses the table: the way must turn");
        Assert.True(Clear(way));
        Assert.Equal([1.6, 1.4], way[0]); Assert.Equal([0, -2.2], way[^1]);
        var length = Enumerable.Range(1, way.Count - 1).Sum(k => Math.Sqrt(Math.Pow(way[k][0] - way[k - 1][0], 2) + Math.Pow(way[k][1] - way[k - 1][1], 2)));
        Assert.InRange(length, 3.9, 5.5);   // a little longer than the straight 3.94 m, not a long detour
    }

    [Fact]
    public void AFreeWayIsStraight()
    {
        Assert.Equal(2, MInstance.Route([0, -3.8], [0.6, -1.6], Table).Count);   // the Visitor, from the porch into the hall
        Assert.Equal(2, MInstance.Route([1.6, 1.4], [0, -2.2], []).Count);       // no furniture
    }

    [Fact]
    public async Task ThePersonaWalksRoundTheTableAndFacesTheWayItGoes()
    {
        var m = new MInstance(Repository.Schemas);
        Mpai.Mmm.Server.DoorUseCase.Setup(m);
        await using var app = Mpai.Mmm.Server.MmmServer.Build(m, "http://127.0.0.1:0");
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()) };
        var host = new Mpai.Mmm.Client.MmmClient(http, "Host", m.TokenOf("Host"));
        var thing = (string id) => m.Snapshot()["Things"]!.AsArray().OfType<System.Text.Json.Nodes.JsonObject>().First(t => (string?)t["ID"] == id);
        Assert.NotNull(thing("Table")["Footprint"]);
        var stream = new System.Text.Json.Nodes.JsonObject { ["Header"] = "PAF-PAV-V1.6", ["MInstanceID"] = m.ID, ["PortableAvatarID"] = "pav-walk" };
        var r = await host.PerformAsync("MM-Animate", Mpai.Mmm.Client.Uc2.C("HostPersona", at: "Doorway", with: [stream]));
        Assert.True(r.Http is >= 200 and < 300, $"{r.Http} {r.Response.PaStatus.Code} {r.Response.PaStatus.Detail}");
        var seen = new List<(double X, double Y, double Yaw)>();
        for (var i = 0; i < 80 && (bool?)thing("HostPersona")["Walking"] == true; i++)
        {
            var t = thing("HostPersona"); seen.Add(((double)t["X"]!, (double)t["Y"]!, (double)t["Yaw"]!));
            await Task.Delay(100);
        }
        await app.StopAsync();
        Assert.True(seen.Count > 10, "the walk takes several seconds");
        // every place it was seen at keeps clear of the table and of the clearance round it (points, not the chords between them, which cut the corners)
        Assert.All(seen, s => Assert.True(Clear([[s.X, s.Y]]) && Clear([[s.X, s.Y], [s.X, s.Y]]), $"the Persona at {s.X:0.00}, {s.Y:0.00} is too near the table"));
        Assert.True(seen.Select(s => Math.Round(s.Yaw)).Distinct().Count() >= 2, "the Persona turns at a corner");
    }
}
