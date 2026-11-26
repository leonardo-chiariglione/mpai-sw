using Mpai.Aif.Api;
using Mpai.Core;

namespace Mpai.Aif.Tests;

// THE CEC APP (Apps/HMC/CEC, N-50 e, 2026-11-25): Communicating Entities in Context (HMC-CEC V2.2) as an App of its own, with its own Controller (HmcController, port 5064)
// and its own client (CecApp, made from the App of CPS). Its workflow (HMC-CEC.orch): each turn the face, then what the person says (speech or text), then the Selector of the
// turn (the medium and the two languages); the Module is offered them with the machine's ID and gives the machine's Portable Avatar. The Module itself is run by
// HmcEndToEndTests (Slow); here the workflow, with simulated devices and Controller.
public class CecAppTests
{
    private const string Module = "1HMC-CEC-V2.2-I01";

    [Fact]
    public async Task TheCecWorkflowOffersTheFaceTheWordsTheSelectorAndTheMachineEachTurn()
    {
        var log = new List<string>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var api = new ScriptedController(log, (turn, _) => new[] { new ControllerApi.Datum("PAF-PAV-V1.6", 1, AppWorkflow.PortableAvatar("answer " + turn)) });
        var typed = 0;
        var devices = new Mpai.Ua.DeviceRegistry()
            .RegisterAcquire("OSD-BVO-V1.5", (_, _) => { lock (log) log.Add("face"); return Task.FromResult<string?>(MpaiJson.ToJson(BasicVisualObject.FromFile("f.jpg", new byte[] { 1, 2, 3 }, "Face"))); })
            .RegisterAcquire("OSD-BAO-V1.5", (_, _, abandon) => AppWorkflow.NothingUntil(abandon, stop.Token))
            .RegisterAcquire("OSD-BTO-V1.5", async (_, _, abandon) =>
            {
                var n = Interlocked.Increment(ref typed);
                if (n > 2) return await AppWorkflow.NothingUntil(abandon, stop.Token);
                lock (log) log.Add("typed " + n);
                return MpaiJson.ToJson(BasicTextObject.FromText("sentence " + n));
            })
            .RegisterAcquire("OSD-SEL-V1.5", (_, _) => { lock (log) log.Add("selector"); return Task.FromResult<string?>("{\"Header\":\"OSD-SEL-V1.5\",\"InputMedia\":\"Text\",\"InputLanguage\":\"en\",\"OutputLanguage\":\"it\"}"); })
            .RegisterPresent("screen", _ => Task.CompletedTask)
            .RegisterPresent("file", data =>
            {
                if (data.ContainsKey("PAF-PAV-V1.6")) { lock (log) log.Add("answer"); if (log.Count(l => l == "answer") == 2) stop.Cancel(); }
                return Task.CompletedTask;
            });

        var workflow = new Mpai.Wdl.WorkflowReader().Read(File.ReadAllText(Path.Combine(Repository.Root, "Apps", "HMC", "CEC", "HMC-CEC.orch")));
        var why = await AppWorkflow.RunAsync(workflow, api, devices, stop.Token);

        Assert.Equal(new[]
        {
            "start " + Module,
            "face", "typed 1", "selector", "advance " + Module, "answer",
            "face", "typed 2", "selector", "advance " + Module, "answer",
            "stop " + Module
        }, log);
        foreach (var turn in api.Turns)
        {
            Assert.Contains(turn, d => d.DataType == "OSD-BVO-V1.5" && d.PortNumber == 1);
            Assert.Contains(turn, d => d.DataType == "OSD-SEL-V1.5" && d.PortNumber == 1 && d.Json.Contains("\"OutputLanguage\":\"it\""));
            Assert.Contains(turn, d => d.DataType == "string" && d.Json == "\"MPAI as a Service\"");
        }
        Assert.Contains(api.Turns[1], d => d.DataType == "OSD-BTO-V1.5" && d.PortNumber == 1 && d.Json.Contains("sentence 2"));
        Assert.True(why.Length > 0);
    }
}
