using System.Text.Json.Nodes;

using AIF.Controller;
using Mpai.Aif.Api;
using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Aif.Tests;

// ONE MODULE FOR EACH USER, AT THE SAME TIME (N-51, 2026-11-27). From 2026-11-26 the MAS instance gives every Module Instance a client starts a Controller of its own (its own AIMs,
// sessions and storage; the engines shared). Here several clients use one App at the same moment on a Service that is running (MPAI_MAS_URL, else https://localhost:5105/): each
// starts its own Module Instance of TST and gives its own sentence, all at once; each must get back its own sentence translated, and none the sentence of another.
// Skipped where no Service answers or it does not offer TST.
public class MasOneModuleEachTests
{
    private static string Url => Environment.GetEnvironmentVariable("MPAI_MAS_URL") is { Length: > 0 } u ? u : "https://localhost:5105/";

    private static readonly string[] Names = { "Marco", "Giulia", "Paolo", "Anna" };

    [SkippableFact]
    public void FourClientsOfOneAppAtOnceEachGetTheirOwnAnswer()
    {
        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3), BaseAddress = new Uri(Url) })
        {
            try { http.GetAsync("MPAI/AIFU/Apps").Wait(); } catch { Skip.If(true, $"no Service answers at {Url}"); }
        }

        var apis = Names.Select(_ => new Mpai.Mas.Client.RemoteControllerApi(Url)).ToArray();
        var ids = new int[Names.Length];
        try
        {
            for (var i = 0; i < Names.Length; i++)
            {
                var started = apis[i].StartFlow("1MMC-TST-V2.5-I01", out ids[i]);
                Skip.If(started != AifError.OK, $"the Service does not offer 1MMC-TST-V2.5-I01 ({started})");
            }

            // all four turns at the same time, each on its own Module Instance
            var results = new ControllerApi.Result[Names.Length];
            Parallel.For(0, Names.Length, new ParallelOptions { MaxDegreeOfParallelism = Names.Length }, i =>
            {
                var languages = new ControllerApi.Datum("OSD-SEL-V1.5", 1, MpaiJson.ToJson(BasicSelectorObject.Languages("en", "it")));
                var text = new ControllerApi.Datum("OSD-BTO-V1.5", MpaiJson.ToJson(BasicTextObject.FromText($"Good morning. My name is {Names[i]} and the concert starts at eight.")));
                results[i] = apis[i].Advance(ids[i], new[] { languages, text });
            });

            for (var i = 0; i < Names.Length; i++)
            {
                var r = results[i];
                Assert.True(r.Ok, $"client {i} ({Names[i]}): {r.Error}");
                var said = MpaiJson.FromJson<BasicTextObject>(r.ByType("OSD-BTO-V1.5")!)!.GetText();
                Assert.Contains(Names[i], said, StringComparison.OrdinalIgnoreCase);
                foreach (var other in Names.Where(n => n != Names[i]))
                    Assert.DoesNotContain(other, said, StringComparison.OrdinalIgnoreCase);
                Assert.True(MpaiJson.FromJson<BasicSpeechObject>(r.ByType("OSD-BSO-V1.5")!)!.Data.Length > 200, $"client {i} ({Names[i]}) got no speech");
            }
        }
        finally
        {
            for (var i = 0; i < Names.Length; i++)
            {
                try { apis[i].StopFlow(ids[i]); } catch { }
                apis[i].Dispose();
            }
        }
    }

    // AMQ at once for two clients: one shows a red picture, the other a blue one; the TIQ AIMs of the two Module Instances share BLIP's sessions (N-51) but each keeps its own picture.
    [SkippableFact]
    public void TwoClientsOfAmqAtOnceEachHearAboutTheirOwnPicture()
    {
        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3), BaseAddress = new Uri(Url) })
        {
            try { http.GetAsync("MPAI/AIFU/Apps").Wait(); } catch { Skip.If(true, $"no Service answers at {Url}"); }
        }
        string[] colours = { "red", "blue" };
        var apis = colours.Select(_ => new Mpai.Mas.Client.RemoteControllerApi(Url)).ToArray();
        var ids = new int[colours.Length];
        try
        {
            for (var i = 0; i < colours.Length; i++)
            {
                var started = apis[i].StartFlow("1MMC-AMQ-V2.5-I01", out ids[i]);
                Skip.If(started != AifError.OK, $"the Service does not offer 1MMC-AMQ-V2.5-I01 ({started})");
            }
            string Ask(int i)
            {
                var file = colours[i] + ".jpg";
                var picture = new ControllerApi.Datum("OSD-BVO-V1.5", 1, MpaiJson.ToJson(BasicVisualObject.FromFile(file, File.ReadAllBytes(Path.Combine(Repository.Root, "Test", "Data", file)), "Visual Object")));
                var question = new ControllerApi.Datum("OSD-BTO-V1.5", 1, MpaiJson.ToJson(BasicTextObject.FromText("What color is the picture?")));
                var r = apis[i].Advance(ids[i], new[] { picture, question, new ControllerApi.Datum("string", 1, "\"MPAI as a Service\"") });
                Assert.True(r.Ok, $"client {i} ({colours[i]}): {r.Error}");
                return AvatarUtterance.TextOfPortableAvatar(r.ByType("PAF-PAV-V1.6")) ?? "";
            }
            // what each picture gets when asked alone (the model says "black" of blue.jpg: the test is that the answer is the picture's own, as ServiceTests.TwoClientsPictures)
            var alone = new[] { Ask(0), Ask(1) };
            Assert.Contains("red", alone[0], StringComparison.OrdinalIgnoreCase);
            var wrong = new List<string>();
            for (var round = 1; round <= 4; round++)
            {
                var both = new string[2];
                Parallel.For(0, 2, new ParallelOptions { MaxDegreeOfParallelism = 2 }, i => both[i] = Ask(i));
                for (var i = 0; i < 2; i++)
                    if (both[i] != alone[i]) wrong.Add($"round {round}: {colours[i]} answered \"{both[i]}\" (alone: \"{alone[i]}\")");
            }
            Assert.True(wrong.Count == 0, string.Join("\n", wrong));
        }
        finally
        {
            for (var i = 0; i < colours.Length; i++)
            {
                try { apis[i].StopFlow(ids[i]); } catch { }
                apis[i].Dispose();
            }
        }
    }
}
