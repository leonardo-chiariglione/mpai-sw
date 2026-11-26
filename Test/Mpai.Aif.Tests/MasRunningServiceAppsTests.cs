using System.Text.Json.Nodes;

using AIF.Controller;
using Mpai.Aif.Api;
using Mpai.Core;
using Mpai.Core.OSD;

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
        var picture = new ControllerApi.Datum("OSD-BVO-V1.5", 1, MpaiJson.ToJson(BasicVisualObject.FromFile("red.jpg", File.ReadAllBytes(Path.Combine(Repository.Root, "Test", "Data", "red.jpg")), "Visual Object")));
        var r = Turn(api, "1MMC-AMQ-V2.5-I01", picture, Text(1, "What color is the picture?"), MachineId);
        Assert.True(r.Ok, $"AMQ on the Service: {r.Error}");
        var said = AvatarUtterance.TextOfPortableAvatar(r.ByType("PAF-PAV-V1.6")) ?? "";
        Assert.Contains("red", said, StringComparison.OrdinalIgnoreCase);
    }

    // N-50 (e), 2026-11-25: TST and MQA on the Service (their MAS versions: the Module on the instance, the client of the App on this machine with --controller).
    [SkippableFact]
    public void TstTranslatesATypedTurnIntoTextAndSpeechOnTheService()
    {
        using var api = Service();
        var languages = new ControllerApi.Datum("OSD-SEL-V1.5", 1, MpaiJson.ToJson(BasicSelectorObject.Languages("en", "it")));
        var r = Turn(api, "1MMC-TST-V2.5-I01", languages, new ControllerApi.Datum("OSD-BTO-V1.5", MpaiJson.ToJson(BasicTextObject.FromText("Good morning. The concert starts at eight."))));
        Assert.True(r.Ok, $"TST on the Service: {r.Error}");
        Assert.Contains("concert", MpaiJson.FromJson<BasicTextObject>(r.ByType("OSD-BTO-V1.5")!)!.GetText(), StringComparison.OrdinalIgnoreCase);
        Assert.True(MpaiJson.FromJson<BasicSpeechObject>(r.ByType("OSD-BSO-V1.5")!)!.Data.Length > 200, "TST on the Service gave no speech");
    }

    [SkippableFact]
    public void MqaSaysWhatTheObjectIsOnTheService()
    {
        using var api = Service();
        var photo = Directory.GetFiles(Path.Combine(Repository.Root, "Test", "Data", "Images", "Animals", "zebra")).OrderBy(f => f, StringComparer.Ordinal).First();
        var picture = new ControllerApi.Datum("OSD-BVO-V1.5", MpaiJson.ToJson(BasicVisualObject.FromFile(Path.GetFileName(photo), File.ReadAllBytes(photo), "Visual Object")));
        var r = Turn(api, "1MMC-MQA-V2.5-I01", picture, new ControllerApi.Datum("OSD-BTO-V1.5", MpaiJson.ToJson(BasicTextObject.FromText("What animal is this?"))), MachineId);
        Assert.True(r.Ok, $"MQA on the Service: {r.Error}");
        Assert.Contains("zebra", AvatarUtterance.TextOfPortableAvatar(r.ByType("PAF-PAV-V1.6")) ?? "", StringComparison.OrdinalIgnoreCase);
    }

    // N-50 (l), 2026-11-25: the MAS version of HCI - the Human-CAV Interaction Module on the instance; the cabin (camera, microphone) is the client's. One turn: what a
    // passenger says (a Piper voice, as the cabin's microphone gives it), a face and the cabin's LiDAR frame (empty); the Module recognises the words and answers.
    [SkippableFact]
    public void HciHearsThePassengerOnTheService()
    {
        Skip.IfNot(TestModels.Can("tts-program"), "the voice of the passenger cannot be made");
        using var api = Service();
        var face = Directory.GetFiles(Path.Combine(Repository.Root, "Test", "Data", "Images", "Faces", "happy"), "*.jpg").OrderBy(f => f, StringComparer.Ordinal).First();
        var started = api.StartFlow("1CAV-HCI-V2.0-I01", out var id);
        Skip.If(started != AifError.OK, $"the Service does not offer 1CAV-HCI-V2.0-I01 ({started})");
        try
        {
            api.SharedStorageInit(id, Path.Combine(Path.GetTempPath(), "mas-hci-" + Guid.NewGuid().ToString("N")));
            var r = api.Advance(id, new[]
            {
                new ControllerApi.Datum("OSD-BAO-V1.5", 1, HciSpeechTests.Passenger("Good morning. Please take me to the station.", "en_GB-alan-medium")),
                new ControllerApi.Datum("OSD-BVO-V1.5", MpaiJson.ToJson(BasicVisualObject.FromFile(Path.GetFileName(face), File.ReadAllBytes(face), "Face"))),
                new ControllerApi.Datum("OSD-BLO-V1.5", 1, MpaiJson.ToJson(new BasicLiDARObject { BasicLiDARObjectID = Guid.NewGuid().ToString("N"), BasicLiDARData = [] })),
                TestAvatar.Datum
            });
            Assert.True(r.Ok, $"HCI on the Service: {r.Error}");
            var heard = r.ByType("OSD-BTO-V1.5", 2) is { } t ? MpaiJson.FromJson<BasicTextObject>(t)?.GetText() ?? "" : "";
            Assert.Contains("station", heard, StringComparison.OrdinalIgnoreCase);
        }
        finally { api.StopFlow(id); }
    }
}
