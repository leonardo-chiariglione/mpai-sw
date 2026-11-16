using System.Text.Json.Nodes;
using AIF.Controller;
using AIF.Store;
using Mpai.Aih.Hfl;
using Mpai.Nnw.Watermarking;
using Xunit.Abstractions;

namespace Mpai.Aif.Tests;

// THE THREE NNW V2.0 AIMs with NCT (No-Constraint Traceability, IMT): During-Training Watermark Insertion, Post-Training Watermark Insertion, Watermark Extraction. Through
// the AIMs only (their Ports and the Locator), on the network of the AIH-HSP demo and its synthetic chest-like images.
[Trait("Group", "Slow")]
public class NnwWatermarkTests(ITestOutputHelper output)
{
    private const string Payload = "1011001011010001";
    private static readonly int[] Shape = [1, FrontEndTrainer.ImageSide, FrontEndTrainer.ImageSide];
    private static string NetworkFile => Path.Combine(Repository.Root, "Test", "Data", "Fl", "frozen-backbone.onnx");
    private static readonly JsonObject Locator = NnwJson.LocatorObject("l", NnwJson.NctMethod(Payload.Length, 16), "key:back-end");

    private static AmdStore Amds() { var s = new AmdStore(Repository.Amds); s.Scan(); return s; }

    private static readonly Dictionary<string, string> Quick = new() { ["NetworkEpochs"] = "3", ["HeadEpochs"] = "60" };

    private static Message Msg(params (string Port, JsonObject Data)[] ports) =>
        new() { MessageId = Guid.NewGuid().ToString("N"), Ports = ports.ToDictionary(p => p.Port, p => p.Data.ToJsonString()) };

    private static JsonObject Dataset(int count, int seed) =>
        NnwJson.DatasetObject("ds", Shape, AihFederatedTrainingTests.Cases(AihFederatedTrainingTests.Neutral, count, seed).Select(c => (FrontEndTrainer.Standardise(c.Image), c.Finding)));

    private static FrontEndTrainer Trainer(out string folder)
    {
        folder = Path.Combine(Path.GetTempPath(), "nnw-" + Guid.NewGuid().ToString("N"));
        return new FrontEndTrainer(NetworkFile, new AIF.SharedStorage.FileSharedStorage(folder, "t", "t"));
    }

    private static JsonObject Untrained(FrontEndTrainer t) => NnwJson.UntrainedModel("untrained", File.ReadAllBytes(NetworkFile), t.FeatureCount);

    private static async Task<JsonObject> Dwi(AmdStore amds, JsonObject model, JsonObject dataset, JsonObject key, string payload = Payload)
    {
        var ports = AimPortReader.Load(amds, NnwProvider.Dwi);
        var dwi = new NnwProvider(amds).Create(NnwProvider.Dwi, Quick, null);
        var r = await dwi.ProcessAsync(Msg((ports.Input(NnwJson.Model), model), (ports.Input(NnwJson.Dataset), dataset), (ports.Input(NnwJson.Payload), NnwJson.PayloadObject("p", payload)),
                                           (ports.Input(NnwJson.Key), key), (ports.Input(NnwJson.Locator), Locator)));
        Assert.False(r.IsError, r.Payload);
        return JsonNode.Parse(r.Payload)!.AsObject();
    }

    private static async Task<JsonObject> Wex(AmdStore amds, JsonObject model, JsonObject key)
    {
        var ports = AimPortReader.Load(amds, NnwProvider.Wex);
        var wex = new NnwProvider(amds).Create(NnwProvider.Wex, new Dictionary<string, string>(), null);
        var r = await wex.ProcessAsync(Msg((ports.Input(NnwJson.Model), model), (ports.Input(NnwJson.Key), key), (ports.Input(NnwJson.Locator), Locator)));
        Assert.False(r.IsError, r.Payload);
        return JsonNode.Parse(r.Payload)!.AsObject();
    }

    // DWI then WEX: the Payload is read back with the Key, not with another; the marked model learns its task.
    [Fact]
    public async Task DuringTrainingInsertionThenExtraction()
    {
        var amds = Amds();
        using var t = Trainer(out var folder);
        var key = NnwJson.KeyObject("k", [0.21, 0.63, 0.47]);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var marked = await Dwi(amds, Untrained(t), Dataset(60, 1), key);
        var seconds = clock.Elapsed.TotalSeconds;
        var read = await Wex(amds, marked, key);
        var other = await Wex(amds, marked, NnwJson.KeyObject("k2", [0.9, 0.1]));
        var accuracy = t.Score(marked, AihFederatedTrainingTests.CheckSet()).Accuracy;
        output.WriteLine($"DWI {seconds:F1} s; read {read["PayloadData"]} ({read["DescrMetadata"]}); another key: {other["DescrMetadata"]}; accuracy on the check set {accuracy:P0}");
        Assert.Equal(Payload, (string?)read["PayloadData"]);
        Assert.StartsWith("mark present", (string?)read["DescrMetadata"]);
        Assert.StartsWith("mark NOT present", (string?)other["DescrMetadata"]);
        Assert.True(accuracy > 0.55, $"the marked model does not learn its task: {accuracy:P0}");
        try { Directory.Delete(folder, true); } catch { }
    }

    // PWI then WEX on a trained model.
    [Fact]
    public async Task PostTrainingInsertionThenExtraction()
    {
        var amds = Amds();
        using var t = Trainer(out var folder);
        var trained = await Dwi(amds, Untrained(t), Dataset(40, 2), NnwJson.KeyObject("first", [0.5]));
        var key = NnwJson.KeyObject("k", [0.31, 0.77]);
        var ports = AimPortReader.Load(amds, NnwProvider.Pwi);
        var pwi = new NnwProvider(amds).Create(NnwProvider.Pwi, new Dictionary<string, string>(), null);
        var r = await pwi.ProcessAsync(Msg((ports.Input(NnwJson.Model), trained), (ports.Input(NnwJson.Payload), NnwJson.PayloadObject("p", Payload)), (ports.Input(NnwJson.Key), key), (ports.Input(NnwJson.Locator), Locator)));
        Assert.False(r.IsError, r.Payload);
        var read = await Wex(amds, JsonNode.Parse(r.Payload)!.AsObject(), key);
        output.WriteLine($"PWI: read {read["PayloadData"]} ({read["DescrMetadata"]})");
        Assert.Equal(Payload, (string?)read["PayloadData"]);
        try { Directory.Delete(folder, true); } catch { }
    }

    // TWO MARKS, EACH FOR ITS OWN PARTY: the Back End marks its model with DWI and its key; a Health Front End trains that model on its End User's cases with DWI and
    // ITS OWN key and payload (it does not know the Back End's). The Back End's WEX still finds the Back End's mark; the Front End's WEX finds the Front End's: the Back End
    // is transparent to the Front End's mark, and the other way round.
    [Fact]
    public async Task TheFrontEndsOwnMarkLeavesTheBackEndsMark()
    {
        const string FrontEndPayload = "0110100111000110";
        var amds = Amds();
        using var t = Trainer(out var folder);
        var backEndKey = NnwJson.KeyObject("hbe", [0.12, 0.34, 0.56]); var frontEndKey = NnwJson.KeyObject("hfe", [0.77, 0.05, 0.91]);
        var published = await Dwi(amds, Untrained(t), Dataset(60, 3), backEndKey);
        var endUserCases = NnwJson.DatasetObject("eu", Shape, AihFederatedTrainingTests.Cases(AihFederatedTrainingTests.EndUsers[0], 60, 4).Select(c => (FrontEndTrainer.Standardise(c.Image), c.Finding)));
        var returned = await Dwi(amds, published, endUserCases, frontEndKey, FrontEndPayload);
        var byBackEnd = await Wex(amds, returned, backEndKey);
        var byFrontEnd = await Wex(amds, returned, frontEndKey);
        output.WriteLine($"Back End's WEX: {byBackEnd["PayloadData"]} ({byBackEnd["DescrMetadata"]})");
        output.WriteLine($"Front End's WEX: {byFrontEnd["PayloadData"]} ({byFrontEnd["DescrMetadata"]})");
        Assert.Equal(Payload, (string?)byBackEnd["PayloadData"]);
        Assert.StartsWith("mark present", (string?)byBackEnd["DescrMetadata"]);
        Assert.Equal(FrontEndPayload, (string?)byFrontEnd["PayloadData"]);
        Assert.StartsWith("mark present", (string?)byFrontEnd["DescrMetadata"]);
        try { Directory.Delete(folder, true); } catch { }
    }

    // FEDERATED LEARNING: the Back End's mark inserted with DWI; an End User trains the model on its own cases (network and final layer, no mark); the Back End's WEX
    // still finds the mark.
    [Fact]
    public async Task TheMarkSurvivesAnEndUsersTraining()
    {
        var amds = Amds();
        using var t = Trainer(out var folder);
        var key = NnwJson.KeyObject("k", [0.12, 0.34, 0.56]);
        var marked = await Dwi(amds, Untrained(t), Dataset(60, 3), key);
        var k = 0; foreach (var (img, f) in AihFederatedTrainingTests.Cases(AihFederatedTrainingTests.EndUsers[0], 60, 4)) t.KeepCase("c" + k++, img, f);
        var trained = t.Train(marked);
        var read = await Wex(amds, trained, key);
        output.WriteLine($"after an End User's training: read {read["PayloadData"]} ({read["DescrMetadata"]})");
        Assert.Equal(Payload, (string?)read["PayloadData"]);
        Assert.StartsWith("mark present", (string?)read["DescrMetadata"]);
        try { Directory.Delete(folder, true); } catch { }
    }
}
