using System.Net.Http;
using System.Text.Json.Nodes;

using AIF.Controller;
using Mpai.Aif.Api;
using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Providers;

namespace Mpai.Aif.Tests;

// THE MQA APP (Apps/MMC/MQA, N-50 e, 2026-11-25): Multimodal Question Answering (MMC-MQA-V2.5) as an App of its own - not CPS, not AMQ. The Module is the L3
// 1MMC-MQA-V2.5-I01: Visual Instance Identification (what the object is), ASR, NLU, Question Analysis (the Intention), Answer to Question (the Machine Text) and
// Portable Avatar Multiplexing; its leaves are MqaProvider's. The App's window is the AMQ window with another workflow (MMC-MQA.orch).
public class MqaAppTests
{
    private const string Module = "1MMC-MQA-V2.5-I01";

    private static string Tdo(string question)
    {
        // the Text Descriptors Object NLU gives for the question
        var store = new AIF.Store.AmdStore(Repository.Amds); store.Scan();
        var nlu = new Mpai.Mmc.Nlu.NluAimProcessor("1MMC-NLU-V2.5-I01", AimPortReader.Load(store, "1MMC-NLU-V2.5-I01"));
        var ports = AimPortReader.Load(store, "1MMC-NLU-V2.5-I01");
        var r = nlu.ProcessAsync(new Message { MessageId = "q", Ports = new() { [ports.Input("OSD-BTO-V1.5", 1)] = MpaiJson.ToJson(BasicTextObject.FromText(question)) } }).Result;
        return r.Ports[ports.Output("MMC-TDO-V2.5")];
    }

    [Theory]
    [InlineData("What colour is this animal?", "entity", "colour", "visual object")]
    [InlineData("Where does it live?", "location", "", "visual object")]
    [InlineData("How many legs does it have?", "quantity", "", "visual object")]
    [InlineData("Is it dangerous?", "yes/no", "", "visual object")]
    [InlineData("Who wrote Hamlet?", "person", "", "general")]
    public void QuestionAnalysisGivesTheIntention(string question, string sat, string lat, string domain)
    {
        var intention = Mpai.Mmc.Qam.QamAimProcessor.Analyse(Mpai.Mmc.Qam.QamAimProcessor.Tokens(Tdo(question)));
        var i = intention["Intention"]![0]!;
        Assert.Equal(sat, (string?)i["qSAT"]);
        Assert.Equal(lat, (string?)i["qLAT"]);
        Assert.Equal(domain, (string?)i["qdomain"]);
        Assert.Equal("MMC-INT-V2.5", (string?)intention["Header"]);
        var check = new Mpai.Mmc.Jsi.SchemaCheck("https://schemas.mpai.community/MMC/V2.5/data/Intention.json", Path.Combine(Repository.Schemas, "MMC", "V2.5", "data", "Intention.json"), Repository.Schemas);
        Assert.Empty(check.Violations(intention.ToJsonString())!);
    }

    [Fact]
    public void AnswerToQuestionTellsTheModelWhatTheObjectIsAndWhatIsAsked()
    {
        const string iid = "{\"Header\":\"OSD-IID-V1.5\",\"InstanceLabel\":\"zebra\",\"LabelConfidenceLevel\":0.91}";
        var intention = Mpai.Mmc.Qam.QamAimProcessor.Analyse(Mpai.Mmc.Qam.QamAimProcessor.Tokens(Tdo("Where does it live?"))).ToJsonString();
        var prompt = Mpai.Mmc.Aqm.AqmAimProcessor.Prompt("Where does it live?", iid, intention);
        Assert.Contains("The object shown is: zebra (identified with confidence 0.91).", prompt);
        Assert.Contains("kind of answer: location", prompt);
        Assert.EndsWith("Question: Where does it live?", prompt);
        Assert.Contains("No object was identified.", Mpai.Mmc.Aqm.AqmAimProcessor.Prompt("What is it?", null, null));
    }

    [Fact]
    public void NaturalLanguageUnderstandingPassesTheInstanceIdentifierOn()
    {
        var store = new AIF.Store.AmdStore(Repository.Amds); store.Scan();
        var ports = AimPortReader.Load(store, "1MMC-NLU-V2.5-I01");
        var nlu = new Mpai.Mmc.Nlu.NluAimProcessor("1MMC-NLU-V2.5-I01", ports);
        const string iid = "{\"Header\":\"OSD-IID-V1.5\",\"InstanceLabel\":\"zebra\"}";
        var r = nlu.ProcessAsync(new Message { MessageId = "q", Ports = new()
        {
            [ports.Input("OSD-BTO-V1.5", 1)] = MpaiJson.ToJson(BasicTextObject.FromText("What is it?")),
            [ports.Input("OSD-IID-V1.5")] = iid
        } }).Result;
        Assert.Equal(iid, r.Ports[ports.Output("OSD-IID-V1.5")]);
    }

    // The workflow, with simulated devices and Controller: the picture, then the question, then the Module is given the picture, the question and the machine's ID.
    [Fact]
    public async Task TheMqaWorkflowOffersThePictureTheQuestionAndTheMachineAndPresentsTheAnswer()
    {
        var log = new List<string>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var api = new ScriptedController(log, (turn, _) => new[] { new ControllerApi.Datum("PAF-PAV-V1.6", 1, AppWorkflow.PortableAvatar("answer " + turn)) });
        var yes = 0;
        var devices = new Mpai.Ua.DeviceRegistry()
            .RegisterAcquire("OSD-BVO-V1.5", (_, _) => { lock (log) log.Add("picture"); return Task.FromResult<string?>(MpaiJson.ToJson(BasicVisualObject.FromFile("p.jpg", new byte[] { 1, 2, 3 }, "Visual Object"))); })
            .RegisterAcquire("OSD-BSO-V1.5", (_, _, abandon) => AppWorkflow.NothingUntil(abandon, stop.Token))
            .RegisterAcquire("OSD-BTO-V1.5", (_, wanted, _) =>
            {
                if ((wanted ?? "").Contains("YesNo")) { var n = Interlocked.Increment(ref yes); lock (log) log.Add("yes/no " + n); return Task.FromResult<string?>(MpaiJson.ToJson(BasicTextObject.FromText(n == 1 ? "yes" : "no"))); }
                lock (log) log.Add("question");
                return Task.FromResult<string?>(MpaiJson.ToJson(BasicTextObject.FromText("What is it?")));
            })
            .RegisterPresent("screen", _ => Task.CompletedTask)
            .RegisterPresent("stage", _ => Task.CompletedTask)
            .RegisterPresent("avatar", data => { if (data.ContainsKey("PAF-PAV-V1.6")) lock (log) log.Add("answer"); return Task.CompletedTask; });

        var why = await AppWorkflow.RunAsync(AppWorkflow.Load("MQA"), api, devices, stop.Token);

        Assert.Equal(new[] { "start " + Module, "yes/no 1", "picture", "question", "advance " + Module, "answer", "yes/no 2", "stop " + Module }, log);
        var turn = api.Turns.Single();
        Assert.Contains(turn, d => d.DataType == "OSD-BVO-V1.5");
        Assert.Contains(turn, d => d.DataType == "OSD-BTO-V1.5" && d.Json.Contains("What is it?"));
        Assert.Contains(turn, d => d.DataType == "string" && d.Json == "\"MPAI as a Service\"");
        Assert.True(why.Length > 0);
    }

    private static bool Ollama()
    {
        try { using var h = new HttpClient { Timeout = TimeSpan.FromSeconds(3) }; return h.GetStringAsync("http://127.0.0.1:11434/api/tags").Result.Contains("\"llama3.2:3b\""); }
        catch { return false; }
    }

    // The real Module through the Controller API: a picture of a zebra and a question; the Portable Avatar's text names what the object is.
    [SkippableFact]
    [Trait("Group", "Models")]
    public void TheModuleSaysWhatTheObjectIsAndAnswers()
    {
        Skip.IfNot(Ollama(), "Ollama is not running with llama3.2:3b.");
        var folder = Path.Combine(MpaiPaths.TestData, "Images", "Animals", "zebra");
        Skip.IfNot(Directory.Exists(folder), "no zebra picture in " + folder);
        var photo = Directory.GetFiles(folder).OrderBy(f => f, StringComparer.Ordinal).First();
        var store = new AIF.Store.AmdStore(Repository.Amds); store.Scan();
        using var api = new ControllerApi(Repository.Amds, Path.Combine(Repository.Root, "AIMs", "aim-settings.json"), _ => new MqaProvider(store));
        Assert.Equal(AifError.OK, api.StartFlow(Module, out var id));
        try
        {
            var r = api.Advance(id, new[]
            {
                new ControllerApi.Datum("OSD-BVO-V1.5", MpaiJson.ToJson(BasicVisualObject.FromFile(Path.GetFileName(photo), File.ReadAllBytes(photo), "Visual Object"))),
                new ControllerApi.Datum("OSD-BTO-V1.5", MpaiJson.ToJson(BasicTextObject.FromText("What animal is this, and where does it live?"))),
                new ControllerApi.Datum("string", 1, "\"MPAI as a Service\"")
            });
            var status = string.Join("; ", api.Status(id).Aims.Select(a => $"{a.Aim} {a.Status} {a.Reason}"));
            Assert.True(r.Ok, $"{Module}: {r.Error} | {status}");
            var answer = AvatarUtterance.TextOfPortableAvatar(r.ByType("PAF-PAV-V1.6")) ?? "";
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "mqa-answer.txt"), answer);
            Assert.Contains("zebra", answer, StringComparison.OrdinalIgnoreCase);
        }
        finally { api.StopFlow(id); }
    }
}
