using System.Text.Json.Nodes;
using Mpai.Mmc.Dkr;

namespace Mpai.Aif.Tests;

// DOMAIN KNOWLEDGE RETRIEVAL (MMC-DKR-V2.5, 2026-11-27): of what an application knows of its domain (MMC-DKN), the items relevant to a request, with their Relevance - what the
// dialogue of the machine is told of the domain. The knowledge of a CAV on the Proving Ground: its places, each with the Way Point it is, and what it can do.
[Trait("Group", "Fast")]
public class DomainKnowledgeTests
{
    private static JsonObject Cav() => new()
    {
        ["Header"] = "MMC-DKN-V2.5", ["DomainKnowledgeID"] = "CAV-PG", ["Domain"] = "An autonomous car that drives its passenger to places of the CAV Proving Ground.",
        ["Items"] = new JsonArray(
            Place("Entrance Plaza", "J-275_0", "the paved plaza at the west end, by the Gate to the Metaverse Square"),
            Place("Roundabout", "J23_0", "the roundabout east of the Town, with four arms"),
            Place("Park", "J-110_80", "the junction by the park in the Town"),
            Place("North-West Corner", "J-250_160", "the north-west corner of the Town"),
            new JsonObject { ["ItemID"] = "stop", ["Text"] = "The passenger may ask the car to stop, to wait (suspend) and to go on (resume)." })
    };
    private static JsonObject Place(string name, string wayPoint, string what) =>
        new() { ["ItemID"] = wayPoint, ["Name"] = name, ["Reference"] = wayPoint, ["Text"] = $"{name}: {what}. The car can take you there.", ["Source"] = "CAV-OfflineMap" };

    [Fact]
    public void ThePlaceNamedComesFirstWithItsReference()
    {
        var found = DkrAimProcessor.Retrieve(Cav(), "Please take me to the roundabout.");
        var first = found["Items"]!.AsArray()[0]!;
        Assert.Equal("Roundabout", (string?)first["Name"]);
        Assert.Equal("J23_0", (string?)first["Reference"]);
        Assert.Equal(1.0, (double)first["Relevance"]!);
        Assert.Equal("MMC-DKN-V2.5", (string?)found["Header"]);
        Assert.NotNull(found["Domain"]);
    }

    [Fact]
    public void WordsOfAnItemFindItAndNothingIsFoundForWordsOfNoItem()
    {
        Assert.Equal("Entrance Plaza", (string?)DkrAimProcessor.Retrieve(Cav(), "Where is the gate to the square?")["Items"]!.AsArray()[0]!["Name"]);
        Assert.Equal("stop", (string?)DkrAimProcessor.Retrieve(Cav(), "Can you wait here a moment?")["Items"]!.AsArray()[0]!["ItemID"]);
        Assert.Empty(DkrAimProcessor.Retrieve(Cav(), "Banana")["Items"]!.AsArray());
    }

    // THE TWO LAYERS OF A CAV (2026-11-28): the Domain Type, from CAV-TEC V2.0 (the file of the Access of DKR), and the Domain Instance, the places of the Proving Ground.
    private static JsonObject CavTec() => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Root, "Apps", "CAV", "CAO", "Access", "CavTecDomainKnowledge.json")))!;
    private static List<string?> Found(string request) =>
        DkrAimProcessor.Retrieve(DkrAimProcessor.Merge(CavTec(), Cav()), request)["Items"]!.AsArray().Select(i => (string?)i!["ItemID"]).ToList();

    [Theory]
    [InlineData("Please take me to the Roundabout", "ask-routes", "J23_0")]
    [InlineData("Could you drive me to the park?", "ask-routes", "J-110_80")]
    [InlineData("Pull over, please", "cmd-suspend", null)]
    [InlineData("OK, keep going", "cmd-resume", null)]
    [InlineData("Forget it, I want to get out", "cmd-stop", null)]
    [InlineData("I'll take the shorter one", "select-route", null)]
    [InlineData("Are we there yet?", "status-arrived", null)]
    [InlineData("Is the road icy?", "env-ice", null)]
    [InlineData("What is a waypoint?", "waypoint", null)]
    [InlineData("What can you do?", "hci", null)]
    public void APassengerIsUnderstoodWithTheVocabularyOfCavTecAndThePlacesOfTheMap(string request, string action, string? place)
    {
        var found = Found(request);
        Assert.Contains(action, found);
        if (place is not null) Assert.Contains(place, found);
    }

    // WHAT THE REQUEST SELECTS (M3328 3.7): the items it names have Relevance 1 - their References are what the AMS acts on; the others at most 0.9.
    [Theory]
    [InlineData("Please take me to the Roundabout.", "HCIMessage.RequestedRoutes,J23_0")]
    [InlineData("Yes, let's go.", "HCIMessage.RouteCommand:Execute")]
    [InlineData("Pull over, please.", "HCIMessage.RouteCommand:Suspend")]
    [InlineData("Banana", "")]
    public void TheItemsARequestNamesAreWhatItSelects(string request, string references)
    {
        var items = DkrAimProcessor.Retrieve(DkrAimProcessor.Merge(CavTec(), Cav()), request)["Items"]!.AsArray();
        var selected = items.Where(i => (double)i!["Relevance"]! >= 1).Select(i => (string?)i!["Reference"]).OfType<string>().OrderBy(r => r, StringComparer.Ordinal);
        Assert.Equal(references.Split(',', StringSplitOptions.RemoveEmptyEntries).OrderBy(r => r, StringComparer.Ordinal), selected);
        Assert.All(items.Where(i => (double)i!["Relevance"]! < 1), i => Assert.True((double)i!["Relevance"]! <= 0.9));
    }

    [Fact]
    public void TheCavTecKnowledgeComesFromTheStandardAndSatisfiesItsSchema()
    {
        var tec = CavTec();
        Assert.Null(new Mpai.Mmm.Schemas(Repository.Schemas).Check(tec));
        Assert.All(tec["Items"]!.AsArray(), i => Assert.StartsWith("https://mpai.community/standards/mpai-cav/tec/v2-0/", (string?)i!["Source"]));
        Assert.Equal("HCIMessage.RouteCommand:Suspend", (string?)tec["Items"]!.AsArray().First(i => (string?)i!["ItemID"] == "cmd-suspend")!["Reference"]);
    }

    [Fact]
    public void TheRetrievedKnowledgeSatisfiesItsSchema()
    {
        var schemas = new Mpai.Mmm.Schemas(Repository.Schemas);
        Assert.True(schemas.Knows("MMC-DKN-V2.5"));
        Assert.Null(schemas.Check(Cav()));
        Assert.Null(schemas.Check(DkrAimProcessor.Retrieve(Cav(), "take me to the park")));
    }
}
