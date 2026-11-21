using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

using AIF.Controller;
using AIF.Store;

namespace Mpai.Aif.Tests;

// ACCELERATOR (AIM Metadata AccessSources, AIF V3.0, 2026-11-20): the processing unit that the program or data of an entry is built
// to execute on - CPU, or CUDA (an NVIDIA graphics card). A program may have several entries with the same Name and Setting; of
// those that fit the operating system and architecture where the AIM executes, the Controller gives the AIM ONE: the CUDA entry
// where the AIM's ResourcePolicies request a GPU (GPU:Number Request 1 or more) and the machine has a card that meets
// GPU:CUDA:Class Minimum; otherwise the CPU entry, or the entry with no Accelerator. Where none fits and the entry is not
// IsOptional, the Module is refused. The card is passed explicitly here: no test depends on the card of the machine.
[Trait("Group", "Fast")]
[Trait("Blocks", "No")]
public class AccessAcceleratorTests : IDisposable
{
    private readonly string work = Path.Combine(Path.GetTempPath(), "mpai-accelerator-" + Guid.NewGuid().ToString("N"));

    public AccessAcceleratorTests() => Directory.CreateDirectory(work);

    public void Dispose() { try { Directory.Delete(work, true); } catch { } }

    private static readonly string ShaCuda = new('C', 64), ShaCpu = new('B', 64), ShaAll = new('A', 64);

    private static string Here => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" : "MacOS";
    private static string Other => Here == "Windows" ? "Linux" : "Windows";

    private static JsonObject Entry(string accelerator, string sha, string os = "", bool optional = false)
    {
        var e = new JsonObject { ["Name"] = "server", ["Uri"] = "https://example.org/" + sha[..4] + ".zip", ["Sha256"] = sha, ["Setting"] = "ServerPath",
                                 ["Terms"] = new JsonObject { ["Licence"] = "test", ["Uri"] = "https://example.org/terms" } };
        if (os.Length > 0) e["OperatingSystem"] = os;
        if (accelerator.Length > 0) e["Accelerator"] = accelerator;
        if (optional) e["IsOptional"] = true;
        return e;
    }

    // An AIM with these entries; request: GPU:Number Request (null: no GPU:Number), minimum: GPU:CUDA:Class Minimum (null: none).
    private static JsonElement Aim(int? request, string? minimum, params JsonObject[] entries)
    {
        var policies = new JsonArray(new JsonObject { ["Name"] = "Memory", ["Minimum"] = "0.5 GB", ["Request"] = "1 GB", ["Maximum"] = "2 GB" });
        if (request is { } r) policies.Add(new JsonObject { ["Name"] = "GPU:Number", ["Minimum"] = 0, ["Request"] = r, ["Maximum"] = 1 });
        if (minimum is { } m) policies.Add(new JsonObject { ["Name"] = "GPU:CUDA:Class", ["Minimum"] = m, ["Request"] = m, ["Maximum"] = "SM90" });
        var aim = new JsonObject { ["AccessSources"] = new JsonArray(entries.Select(e => (JsonNode)e).ToArray()), ["ResourcePolicies"] = policies };
        return JsonDocument.Parse(aim.ToJsonString()).RootElement.Clone();
    }

    private static readonly ExecutionEnvironment.CudaCard Sm61 = new(61, 4096L * 1024 * 1024);
    private static readonly ExecutionEnvironment.CudaCard Sm35 = new(35, 2048L * 1024 * 1024);

    private static List<AccessEntry> Given(JsonElement aim, ExecutionEnvironment.CudaCard? card, out List<AccessEntry> unmet) =>
        AccessEntry.ReadForThisMachine(aim, card, out unmet);

    [Fact]
    public void WithACardTheCudaEntryIsGiven()
    {
        var given = Given(Aim(1, "SM50", Entry("CUDA", ShaCuda), Entry("CPU", ShaCpu)), Sm61, out var unmet);
        Assert.Equal(ShaCuda, Assert.Single(given).Sha256);
        Assert.Empty(unmet);
    }

    [Fact]
    public void WithoutACardTheCpuEntryIsGiven()
    {
        var given = Given(Aim(1, "SM50", Entry("CUDA", ShaCuda), Entry("CPU", ShaCpu)), null, out var unmet);
        Assert.Equal(ShaCpu, Assert.Single(given).Sha256);
        Assert.Empty(unmet);
    }

    [Fact]
    public void ACardBelowTheMinimumClassGetsTheCpuEntry()
    {
        Assert.Equal(ShaCpu, Assert.Single(Given(Aim(1, "SM50", Entry("CUDA", ShaCuda), Entry("CPU", ShaCpu)), Sm35, out _)).Sha256);
        // the same card meets a lower Minimum; and where the AIM states no GPU:CUDA:Class, any card meets it
        Assert.Equal(ShaCuda, Assert.Single(Given(Aim(1, "SM30", Entry("CUDA", ShaCuda), Entry("CPU", ShaCpu)), Sm35, out _)).Sha256);
        Assert.Equal(ShaCuda, Assert.Single(Given(Aim(1, null, Entry("CUDA", ShaCuda), Entry("CPU", ShaCpu)), Sm35, out _)).Sha256);
    }

    [Fact]
    public void AnAimThatDoesNotRequestAGpuGetsTheCpuEntry()
    {
        Assert.Equal(ShaCpu, Assert.Single(Given(Aim(0, "SM50", Entry("CUDA", ShaCuda), Entry("CPU", ShaCpu)), Sm61, out _)).Sha256);
        Assert.Equal(ShaCpu, Assert.Single(Given(Aim(null, null, Entry("CUDA", ShaCuda), Entry("CPU", ShaCpu)), Sm61, out _)).Sha256);
    }

    [Fact]
    public void AnEntryWithNoAcceleratorIsGivenEverywhere()
    {
        foreach (var card in new[] { Sm61, Sm35, null })
            Assert.Equal(ShaAll, Assert.Single(Given(Aim(1, "SM50", Entry("", ShaAll)), card, out _)).Sha256);
        // beside a CUDA entry it is the one given where the CUDA entry is not
        Assert.Equal(ShaAll, Assert.Single(Given(Aim(1, "SM50", Entry("CUDA", ShaCuda), Entry("", ShaAll)), null, out _)).Sha256);
        Assert.Equal(ShaCuda, Assert.Single(Given(Aim(1, "SM50", Entry("CUDA", ShaCuda), Entry("", ShaAll)), Sm61, out _)).Sha256);
    }

    [Fact]
    public void TheEntriesOfAnotherOperatingSystemAreNotChosen()
    {
        var aim = Aim(1, "SM50", Entry("CUDA", ShaCuda, Other), Entry("CPU", ShaCpu, Here));
        Assert.Equal(ShaCpu, Assert.Single(Given(aim, Sm61, out var unmet)).Sha256);    // the CUDA build is for the other system
        Assert.Empty(unmet);
    }

    [Fact]
    public void NoFittingEntryIsUnmet()
    {
        Assert.Empty(Given(Aim(1, "SM50", Entry("CUDA", ShaCuda)), null, out var unmet));
        Assert.Equal(ShaCuda, Assert.Single(unmet).Sha256);
        Assert.Empty(Given(Aim(1, "SM50", Entry("CPU", ShaCpu, Other)), Sm61, out unmet));
        Assert.Single(unmet);
    }

    // CHECK: a Name may have one entry per processing unit; two for the same one are refused (no Accelerator counts as CPU).
    [Fact]
    public void OneEntryForEachProcessingUnitIsConsistent()
    {
        static List<AccessEntry> All(params JsonObject[] e) => AccessEntry.ReadFrom(Aim(1, null, e));
        Assert.Equal("", AccessEntry.Check(All(Entry("CUDA", ShaCuda), Entry("CPU", ShaCpu))));
        Assert.Equal("", AccessEntry.Check(All(Entry("CUDA", ShaCuda), Entry("", ShaAll))));
        Assert.Contains("more than one entry", AccessEntry.Check(All(Entry("CPU", ShaCpu), Entry("", ShaAll))));
        Assert.Contains("more than one entry", AccessEntry.Check(All(Entry("CUDA", ShaCuda), Entry("CUDA", ShaCpu))));
    }

    // THE DRIVER'S ANSWER (nvidia-smi --query-gpu=compute_cap,memory.total --format=csv,noheader).
    [Fact]
    public void TheDriversAnswerIsRead()
    {
        var card = ExecutionEnvironment.ParseQuery("6.1, 4096 MiB\r\n");
        Assert.NotNull(card);
        Assert.Equal("SM61", card!.Class);
        Assert.Equal(4096L * 1024 * 1024, card.MemoryBytes);
        Assert.Equal(86, ExecutionEnvironment.ParseQuery("8.6, 24576 MiB\n8.6, 24576 MiB\n")!.ComputeCapability);
        Assert.Null(ExecutionEnvironment.ParseQuery(""));
        Assert.Null(ExecutionEnvironment.ParseQuery("NVIDIA-SMI has failed because it couldn't communicate with the NVIDIA driver."));
    }

    // THE MODULE IS REFUSED where no entry of a required Name fits, as for an entry that cannot be obtained; an optional one does not stop it.
    // The AIM requests no GPU, so its CUDA-only entry fits on no machine whatever its card.
    [Fact]
    public void AModuleIsRefusedWhenNoEntryFits()
    {
        var file = Path.Combine(work, "server.zip");
        File.WriteAllText(file, "server");
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
        var amds = Path.Combine(work, "amds");
        Directory.CreateDirectory(amds);
        foreach (var f in Directory.EnumerateFiles(ControllerTests.Amds, "*.json")) File.Copy(f, Path.Combine(amds, Path.GetFileName(f)));
        var l3File = Path.Combine(amds, "1TST-NOP-V1.0-I01.json");

        void Declare(bool optional, params string[] accelerators)
        {
            var l3 = JsonNode.Parse(File.ReadAllText(Path.Combine(ControllerTests.Amds, "1TST-NOP-V1.0-I01.json")))!.AsObject();
            var entries = new JsonArray();
            foreach (var a in accelerators)
            {
                var e = new JsonObject { ["Name"] = "server", ["Accelerator"] = a, ["Uri"] = new Uri(file).AbsoluteUri, ["Sha256"] = sha,
                                         ["Terms"] = new JsonObject { ["Licence"] = "test", ["Uri"] = "https://a.example/terms" } };
                if (optional) e["IsOptional"] = true;
                entries.Add(e);
            }
            l3["AccessSources"] = entries;
            l3["ResourcePolicies"] = new JsonArray(new JsonObject { ["Name"] = "GPU:Number", ["Minimum"] = 0, ["Request"] = 0, ["Maximum"] = 1 });
            File.WriteAllText(l3File, l3.ToJsonString());
        }

        UserAgent Agent(string access)
        {
            var amdStore = new AmdStore(amds); amdStore.Scan();
            var ua = new UserAgent(amdStore) { Access = new AccessStore(access) };
            ua.MPAI_AIFU_Controller_Initialize();
            return ua;
        }

        Declare(false, "CUDA");
        var refused = Assert.ThrowsAny<Exception>(() => Agent(Path.Combine(work, "a1")).MPAI_AIFU_MODULE_Start("1TST-NOP-V1.0-I01", new TestAims(), AimSettings.Empty, out _));
        Assert.Contains("none of its entries is for this machine", refused.Message);

        Declare(true, "CUDA");                                                             // optional: the Module starts
        Assert.Equal(AifError.OK, Agent(Path.Combine(work, "a2")).MPAI_AIFU_MODULE_Start("1TST-NOP-V1.0-I01", new TestAims(), AimSettings.Empty, out _));

        Declare(false, "CPU");                                                             // the CPU entry fits: the Module starts
        Assert.Equal(AifError.OK, Agent(Path.Combine(work, "a3")).MPAI_AIFU_MODULE_Start("1TST-NOP-V1.0-I01", new TestAims(), AimSettings.Empty, out _));
    }

    // THE TWO ASR L3s: two builds of the server, both for Windows x86-64, one for CUDA and one for the processor; the AIM may run
    // without a GPU (Minimum 0) and asks for one (Request 1). One of the two is given, whatever the machine.
    [Fact]
    public void TheAsrServerHasACudaAndACpuBuild()
    {
        foreach (var l3 in new[] { "1MMC-ASR-V2.5-I01.json", "1MMC-ASR-V2.5-I02.json" })
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Amds, l3)));
            var servers = AccessEntry.ReadFrom(doc.RootElement).Where(e => e.Name == "asr-server").ToList();
            Assert.Equal(["CUDA", "CPU"], servers.Select(e => e.Accelerator));
            Assert.All(servers, e => { Assert.Equal("Windows", e.OperatingSystem); Assert.Equal("x86-64", e.Architecture); Assert.Equal("ServerPath", e.Setting); });
            Assert.Equal("C2A4B60EDB11F7E11A9191FFB50929535527D4D91C9903DBE3E554583BBBC63D", servers[1].Sha256);
            Assert.Equal("", AccessEntry.Check(AccessEntry.ReadFrom(doc.RootElement)));
            var policies = ResourcePolicy.ReadFrom(doc.RootElement);
            Assert.Equal("0", policies.Single(p => p.Name == "GPU:Number").Minimum);
            Assert.Equal("1", policies.Single(p => p.Name == "GPU:Number").Request);
            Assert.Equal("SM50", policies.Single(p => p.Name == "GPU:CUDA:Class").Minimum);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.ProcessArchitecture != Architecture.X64) continue;
            Assert.Equal(ShaOf(servers[0]), ShaOf(Assert.Single(AccessEntry.ReadForThisMachine(doc.RootElement, Sm61, out _), e => e.Name == "asr-server")));
            Assert.Equal(ShaOf(servers[1]), ShaOf(Assert.Single(AccessEntry.ReadForThisMachine(doc.RootElement, null, out _), e => e.Name == "asr-server")));
            Assert.Equal(ShaOf(servers[1]), ShaOf(Assert.Single(AccessEntry.ReadForThisMachine(doc.RootElement, Sm35, out _), e => e.Name == "asr-server")));
        }
        static string ShaOf(AccessEntry e) => e.Sha256;
    }

    // CONFORMANCE: the Accelerator of an L3 entry is among the options of its L2's entry of that Name, as Architecture and
    // OperatingSystem of the Implementations; an L2 that names none leaves it free.
    [Fact]
    public void TheAcceleratorFollowsTheL2()
    {
        var l2 = JsonNode.Parse(File.ReadAllText(Path.Combine(Repository.Schemas, "MMC", "V2.5", "AIMs", "AutomaticSpeechRecognition.json")))!.AsObject();
        using var l3 = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Amds, "1MMC-ASR-V2.5-I01.json")));

        IReadOnlyList<string> Against(string? template)
        {
            var copy = l2.DeepClone().AsObject();
            if (template is not null)
                foreach (var e in copy["AccessSources"]!.AsArray().Where(e => e!["Name"]!.GetValue<string>() == "asr-server")) e!["Accelerator"] = template;
            var root = Path.Combine(work, "schemas-" + Guid.NewGuid().ToString("N"), "MMC", "V2.5", "AIMs");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "AutomaticSpeechRecognition.json"), copy.ToJsonString());
            return new AIF.Metadata.L2Conformance(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(root)))!, _ => null).Check(l3.RootElement)
                       .Where(v => v.Contains("Accelerator")).ToList();
        }

        Assert.Empty(Against(null));
        Assert.Empty(Against("/* CPU | CUDA */"));
        Assert.Contains(Against("CPU"), v => v.Contains("Accelerator CUDA"));
        Assert.Contains(Against("/* CPU | ROCm */"), v => v.Contains("Accelerator CUDA"));
    }
}
