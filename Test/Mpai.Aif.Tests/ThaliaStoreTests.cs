using System.IO.Compression;
using System.Text;
using Mpai.StoreService;

namespace Mpai.Aif.Tests;

// THE APPS SHELF OF THE MPAI STORE (M3323, App life cycle): what Thalia obtains. An App is a Workflow written against one Module, with its descriptor and icon; the Store keeps every version it is given and
// never overwrites one, refuses a package that is not an App, and says the latest version by the numbers of the version.
public class ThaliaStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "thalia-shelf-" + Guid.NewGuid().ToString("N"));
    private AppShelf Shelf() => new(_root);
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private static byte[] Package(string? appJson, bool workflow = true, params (string Name, string Text)[] more)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string name, string text) { var e = zip.CreateEntry(name); using var w = new StreamWriter(e.Open(), new UTF8Encoding(false)); w.Write(text); }
            if (appJson is not null) Add("app.json", appJson);
            if (workflow) Add("MMC-X.orch", "workflow X over 1MMC-X-V2.5-I01\n");
            foreach (var (n, t) in more) Add(n, t);
        }
        return ms.ToArray();
    }

    private static string Descriptor(string name = "Talk", string version = "1.0", string module = "1MMC-X-V2.5-I01", string description = "Talks.", string keywords = "\"talk\", \"avatar\"") =>
        $$"""{ "Name": "{{name}}", "Description": "{{description}}", "Version": "{{version}}", "Module": "{{module}}", "Standard": "MPAI-MMC V2.5", "Keywords": [{{keywords}}] }""";

    [Fact, Trait("Group", "Fast")]
    public void AnAppIsPublishedAndFound()
    {
        var shelf = Shelf();
        Assert.Null(shelf.Publish("TLK", Package(Descriptor()), out var version));
        Assert.Equal("1.0", version);
        var entry = shelf.Get("TLK");
        Assert.NotNull(entry);
        Assert.Equal("Talk", entry!.Name); Assert.Equal("1MMC-X-V2.5-I01", entry.Module); Assert.Equal("MPAI-MMC V2.5", entry.Standard);
        Assert.Single(shelf.Latest(null));
        Assert.NotNull(shelf.Package("TLK"));
    }

    [Fact, Trait("Group", "Fast")]
    public void APackageThatIsNotAnAppIsRefused()
    {
        var shelf = Shelf();
        Assert.Contains("no app.json", shelf.Publish("A", Package(null), out _));
        Assert.Contains("Name", shelf.Publish("B", Package("""{ "Version": "1.0", "Module": "M" }"""), out _));
        Assert.Contains("Version", shelf.Publish("C", Package("""{ "Name": "c", "Module": "M" }"""), out _));
        Assert.Contains("Module", shelf.Publish("D", Package("""{ "Name": "d", "Version": "1.0" }"""), out _));
        Assert.Contains("Workflow", shelf.Publish("E", Package(Descriptor(), workflow: false), out _));
        Assert.Contains("not a valid", shelf.Publish("F", Encoding.UTF8.GetBytes("not a zip"), out _));
        Assert.Empty(shelf.Latest(null));
    }

    [Fact, Trait("Group", "Fast")]
    public void AVersionIsNeverOverwritten()
    {
        var shelf = Shelf();
        Assert.Null(shelf.Publish("TLK", Package(Descriptor(description: "first")), out _));
        Assert.Contains("never overwritten", shelf.Publish("TLK", Package(Descriptor(description: "second")), out _));
        Assert.Equal("first", shelf.Get("TLK", "1.0")!.Description);
    }

    [Fact, Trait("Group", "Fast")]
    public void TheLatestVersionIsFoundByTheNumbersOfTheVersion()
    {
        var shelf = Shelf();
        foreach (var v in new[] { "1.2", "1.10", "1.9", "2.0" }.Where(v => v != "2.0").Append("1.10.1"))
            Assert.Null(shelf.Publish("TLK", Package(Descriptor(version: v)), out _));
        Assert.Equal(new[] { "1.2", "1.9", "1.10", "1.10.1" }, shelf.Versions("TLK"));
        Assert.Equal("1.10.1", shelf.Get("TLK")!.Version);
        Assert.Equal("1.9", shelf.Get("TLK", "1.9")!.Version);
    }

    [Fact, Trait("Group", "Fast")]
    public void AnIdentifierCannotLeaveTheShelf()
    {
        var shelf = Shelf();
        Assert.Contains("identifier", shelf.Publish("..\\evil", Package(Descriptor()), out _));
        Assert.Contains("identifier", shelf.Publish("", Package(Descriptor()), out _));
        Assert.Contains("Version", shelf.Publish("OK", Package(Descriptor(version: "1/../..")), out _));
        Assert.Null(shelf.Get("..\\evil"));
    }

    [Fact, Trait("Group", "Fast")]
    public void AppsAreSearchedByNameAndByWhatTheyDo()
    {
        var shelf = Shelf();
        Assert.Null(shelf.Publish("TLK", Package(Descriptor("Talk to your avatar", description: "A spoken conversation")), out _));
        Assert.Null(shelf.Publish("NWS", Package(Descriptor("Read newspapers", module: "1MMC-N-V2.5-I01", description: "Reads the news aloud", keywords: "\"news\"")), out _));
        Assert.Equal(["Read newspapers"], shelf.Latest("news").Select(e => e.Name));
        Assert.Equal(["Talk to your avatar"], shelf.Latest("conversation").Select(e => e.Name));
        Assert.Equal(["Talk to your avatar"], shelf.Latest("talk avatar").Select(e => e.Name));
        Assert.Empty(shelf.Latest("nothing like it"));
        Assert.Equal(2, shelf.Latest("").Count);
    }
}
