using System.Text.Json.Nodes;

using AIF.Controller;
using Mpai.Aif.Api;
using Mpai.Core;

namespace Mpai.Aif.Tests;

// THE APPS ON MPAI AS A SERVICE, ANSWERING. The Apps Thalia lists under "MPAI as a Service" (MTS, MCA, AMQ; NPR is in NprModuleTests) are started on a Service that is running (the one
// of MPAI_MAS_URL, else https://localhost:5105/), given the input a person gives them, and must answer: speech in a Portable Avatar, or - for AMQ - the words of the answer, and the answer
// to the red picture says red. Skipped where no Service answers there or the Service does not offer the App.
public class MasRunningServiceAppsTests
{
    private static string Url => Environment.GetEnvironmentVariable("MPAI_MAS_URL") is { Length: > 0 } u ? u : "https://localhost:5105/";

    private static Mpai.Mas.Client.RemoteControllerApi Service()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3), BaseAddress = new Uri(Url) };
        try { http.GetAsync("MPAI/AIFU/Apps").Wait(); } catch { Skip.If(true, $"no Service answers at {Url}"); }
        return new Mpai.Mas.Client.RemoteControllerApi(Url);
    }

    private static ControllerApi.Result Turn(Mpai.Mas.Client.RemoteControllerApi api, string module, params ControllerApi.Datum[] inputs)
    {
        var started = api.StartFlow(module, out var id);
        Skip.If(started != AifError.OK, $"the Service does not offer {module} ({started})");
        try { return api.Advance(id, inputs); }
        finally { api.StopFlow(id); }
    }

    private static bool Spoke(ControllerApi.Result r) =>
        r.Ok && AvatarUtterance.FromPortableAvatar(r.ByType("PAF-PAV-V1.6")) is { MachineSpeechWav.Length: > 200 };

    private static ControllerApi.Datum MachineId => new("string", 1, "\"MPAI as a Service\"");
    private static ControllerApi.Datum Avatar => new("OSD-B3O-V1.5", 1, AvatarUtterance.AvatarDatum("cav-avatar.glb"));
    private static ControllerApi.Datum Text(int port, string text) => new("OSD-BTO-V1.5", port, MpaiJson.ToJson(BasicTextObject.FromText(text)));

    [SkippableFact]
    public void MtsTranslatesATypedTurnOnTheService()
    {
        using var api = Service();
        var languages = new ControllerApi.Datum("OSD-SEL-V1.5", 1, MpaiJson.ToJson(BasicSelectorObject.Languages("en", "fr")));
        var r = Turn(api, "1MMC-MTS-V2.5-I01", languages, Text(1, "Good morning, how are you?"), Avatar, MachineId);
        Assert.True(Spoke(r), $"MTS on the Service gave no speech: {r.Error}");
    }

    [SkippableFact]
    public void McaAnswersATypedTurnOnTheService()
    {
        using var api = Service();
        var r = Turn(api, "1MMC-MCA-V2.5-I01", Text(1, "Name one fruit. Answer in five words."), Avatar, MachineId);
        Assert.True(Spoke(r), $"MCA on the Service gave no speech: {r.Error}");
    }

    [SkippableFact]
    public void AmqAnswersAboutThePictureOnTheService()
    {
        using var api = Service();
        var picture = new ControllerApi.Datum("OSD-BVO-V1.5", 1, MpaiJson.ToJson(BasicVisualObject.FromFile("red.jpg", File.ReadAllBytes(Path.Combine(Repository.Root, "Test", "Data", "red.jpg")), "Object")));
        var r = Turn(api, "1MMC-AMQ-V2.5-I01", picture, Text(1, "What color is the picture?"), MachineId);
        Assert.True(r.Ok, $"AMQ on the Service: {r.Error}");
        var said = AvatarUtterance.TextOfPortableAvatar(r.ByType("PAF-PAV-V1.6")) ?? "";
        Assert.Contains("red", said, StringComparison.OrdinalIgnoreCase);
    }
}
