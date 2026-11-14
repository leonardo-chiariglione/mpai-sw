using Mpai.Ua;
using Mpai.Wdl;

namespace Mpai.Aif.Tests;

// A VALUE WRITTEN IN A WORKFLOW IS AN INSTANCE OF ITS DATA TYPE (AIF V3.0, Workflow Description, 3, as amended on 2026-11-14): "A datum may be given a value = { ... },
// an instance of the JSON schema of its Data Type, or, for a Text Object, a literal text = "...". Where the datum is acquired, the instance may give only some of its
// elements - its Qualifier, for example - and the instance the device provides shall have them. An interpreter shall refuse a Workflow whose instance is not valid
// against the schema of the Data Type, naming the line; for an acquired datum, the elements given shall be elements of that schema."
[Trait("Group", "Fast")]
public class WdlInstanceValueTests
{
    private static async Task<(ScriptedController Api, List<string> Log)> Run(string orch, DeviceRegistry? devices = null)
    {
        var log = new List<string>();
        var api = new ScriptedController(log, (_, _) => []);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await new WorkflowInterpreter(api, devices ?? new DeviceRegistry(), line => { lock (log) log.Add(line); }).RunAsync(new WorkflowReader().Read(orch), stop.Token);
        return (api, log);
    }

    [Fact]
    public async Task AnInstanceWrittenOverSeveralLinesIsOfferedAsWritten()
    {
        var (api, _) = await Run("""
            workflow T over 1CAE-AES-V2.5-I01
            on Start:
                ask Controller to start
                offer ModeSelector (OSD-SEL-V1.5:1) = {
                    "Header": "OSD-SEL-V1.5",
                    "SelectorID": "neural"
                }
                offer Language (OSD-SEL-V1.5:2) = { "Header": "OSD-SEL-V1.5", "Language": { "InputLanguage": "en", "OutputLanguage": "en" } }
                ask Result (OSD-BSO-V1.5)
            """);
        var turn = Assert.Single(api.Turns);
        var mode = turn.Single(d => d.PortNumber == 1);
        Assert.Equal("neural", System.Text.Json.Nodes.JsonNode.Parse(mode.Json)!["SelectorID"]!.GetValue<string>());
        Assert.Equal("en", System.Text.Json.Nodes.JsonNode.Parse(turn.Single(d => d.PortNumber == 2).Json)!["Language"]!["OutputLanguage"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("""{ "Header": "OSD-SEL-V1.5", "Language": { "InputLanguage": "en" } }""", "not a valid instance")]   // a required element missing
    [InlineData("""{ "Header": "OSD-SEL-V1.5", "InputMedia": "Video" }""", "not a valid instance")]     // a value the schema does not allow
    [InlineData("""{ "Header": "OSD-BTO-V1.5", "SelectorID": "x" }""", "Header")]                       // the instance of another Data Type
    [InlineData("""{ "Header": "OSD-SEL-V1.5", """, "not JSON")]
    public async Task AWorkflowWithAnInvalidInstanceIsRefusedNamingTheLineAndNothingIsStarted(string value, string said)
    {
        var refused = await Assert.ThrowsAsync<WorkflowReader.WorkflowSyntaxError>(() => Run($"""
            workflow T over 1CAE-AES-V2.5-I01
            on Start:
                ask Controller to start
                offer ModeSelector (OSD-SEL-V1.5:1) = {value}
            """));
        Assert.StartsWith("line 4:", refused.Message);
        Assert.Contains(said, refused.Message);
    }

    [Fact]
    public async Task NothingIsStartedBeforeTheWorkflowIsChecked()
    {
        var log = new List<string>();
        var api = new ScriptedController(log, (_, _) => []);
        await Assert.ThrowsAsync<WorkflowReader.WorkflowSyntaxError>(() => new WorkflowInterpreter(api, new DeviceRegistry()).RunAsync(new WorkflowReader().Read("""
            workflow T over 1CAE-AES-V2.5-I01
            on Start:
                ask Controller to start
                loop until Stop:
                    offer ModeSelector (OSD-SEL-V1.5:1) = { "Header": "OSD-SEL-V1.5", "InputMedia": "Video" }
            """), CancellationToken.None));
        Assert.DoesNotContain(log, l => l.StartsWith("start"));
    }

    [Fact]
    public async Task AnAcquiredDatumIsGivenAPartOfItsInstanceWhichTheDeviceReceives()
    {
        string? asked = null;
        var devices = new DeviceRegistry().RegisterAcquire("OSD-BVO-V1.5", (_, wanted) => { asked = wanted; return Task.FromResult<string?>(null); });
        await Run("""
            workflow T over 1MMC-ACR-V2.5-I01
            on Start:
                acquire UserFace (OSD-BVO-V1.5) = { "VisualQualifier": { "Attributes": { "VisualObjectType": "Face" } } }
            """, devices);
        Assert.Contains("\"VisualQualifier\"", asked);
        Assert.Contains("\"Face\"", asked);
    }

    [Fact]
    public async Task APartNamingWhatTheDataTypeDoesNotHaveIsRefused()
    {
        var refused = await Assert.ThrowsAsync<WorkflowReader.WorkflowSyntaxError>(() => Run("""
            workflow T over 1MMC-ACR-V2.5-I01
            on Start:
                acquire UserFace (OSD-BVO-V1.5) = { "Attributes": { "VisualObjectType": "Face" } }
            """));
        Assert.StartsWith("line 3:", refused.Message);
        Assert.Contains("/Attributes", refused.Message);
    }

    // EVERY WORKFLOW OF THE APPS passes the check: their values are instances, and what they ask of a device names elements of its Data Type.
    [Fact]
    public void EveryWorkflowOfTheAppsPassesTheCheck()
    {
        var files = Directory.GetFiles(Path.Combine(Repository.Root, "Apps"), "*.orch", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(Repository.Root, "AIF", "UserAgent", "Orchestration"), "*.orch"))
            .Concat(Directory.GetFiles(Path.Combine(Repository.Root, "MPAIStore"), "*.orch", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.NotEmpty(files);
        var failed = new List<string>();
        foreach (var f in files)
        {
            try { WorkflowInterpreter.Verify(new WorkflowReader().Read(File.ReadAllText(f))); }
            catch (Exception ex) { failed.Add($"{Path.GetRelativePath(Repository.Root, f)}: {ex.Message}"); }
        }
        Assert.True(failed.Count == 0, string.Join("\n", failed));
    }
}
