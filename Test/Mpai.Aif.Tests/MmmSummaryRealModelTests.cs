using System.Text;
using System.Text.Json.Nodes;
using Mpai.Mmm;
using Mpai.Mmm.Client;
using Mpai.Mmm.Server;

namespace Mpai.Aif.Tests;

// THE SUMMARY OF AN MMM MEETING ON THE REAL MODELS (2026-11-19): the meeting server's own SummaryServices - the language model of the meeting (llama3.2:3b on Ollama, as
// Run-Meeting.ps1 starts it) for the English summary and the translation, Piper for the spoken summary. A scripted meeting of Anna and Bruno goes through the Secretary as
// in the meeting (the words are what the ASR Service would have recognised; one line is speech-recognition noise). Asked: English written, French written, Italian spoken.
// What the summaries say is written to Test\Results\<date>\mmm-summary-real-model.md and the spoken one to a .wav next to it, for a person to judge; the test checks
// only what can be checked without judging: an answer in each form, the two speakers named, the decision and the action reported, nothing of the noise, no made-up project.
[Trait("Group", "Models")]
[Collection("MmmMeeting")]
[Trait("Blocks", "Yes")]
public class MmmSummaryRealModelTests
{
    private static readonly (string Who, string Words)[] Script =
    [
        ("Anna", "Good afternoon Bruno, today we should decide the date of the webinar on federated learning."),
        ("Bruno", "I think the twenty third of November works, Leonor has her results ready by then."),
        ("Anna", "Fine, then we decide that the webinar is on the twenty third of November at sixteen hours."),
        ("Bruno", "the the window blue seven"),
        ("Bruno", "I will prepare the slides on the fall detection results and send them to you by Friday."),
        ("Anna", "Good. We still do not know if Carlos can attend, I will ask him."),
        ("Bruno", "Should we also show the chest X-ray experiment?"),
        ("Anna", "Let us talk about that next time, it is not ready."),
    ];

    private static JsonObject Profile(string name) => new()
    {
        ["Header"] = "MMM-PPR-V2.2", ["MInstanceID"] = "MI1", ["humanID"] = $"{name}-human", ["PersonalProfileID"] = $"Profile-{name}-human",
        ["PersonalProfile"] = new JsonObject
        {
            ["Data"] = new JsonArray(new JsonObject { ["FirstName"] = name }),
            ["Processes"] = new JsonArray(new JsonObject
            {
                ["ProcessIDOrProcess"] = new JsonArray(new JsonObject { ["ProcessID"] = name }),
                ["PersonaIDOrPersona"] = new JsonArray(new JsonObject { ["PersonaID"] = $"{name}-Persona" })
            })
        }
    };

    private static string Root()
    {
        var d = AppContext.BaseDirectory;
        while (d is not null && !Directory.Exists(Path.Combine(d, "AllApps", "MMM", "Viewer"))) d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar));
        return d ?? throw new DirectoryNotFoundException("AllApps\\MMM\\Viewer");
    }

    [Fact]
    public async Task TheSummaryOfAScriptedMeetingOnLlama32AndPiper()
    {
        var root = Root();
        var m = new MInstance(Repository.Schemas);
        Uc2.Setup(m); Meeting.Setup(m);
        var app = MmmServer.Build(m, "http://127.0.0.1:0");
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.First()), Timeout = TimeSpan.FromMinutes(10) };
        var words = "";
        var services = new MeetingServices(http, m, _ => Task.FromResult(words)) { TranscribeAlways = true };
        var model = Environment.GetEnvironmentVariable("MMM_SUMMARY_MODEL") ?? "llama3.2:3b";                          // the meeting's model; another to compare
        using var summary = new SummaryServices(root, model);
        services.Summarise = summary.SummariseAsync; services.Translate = summary.TranslateAsync; services.Synthesise = summary.SynthesiseAsync;
        try
        {
            services.Preinvite(["Anna", "Bruno"]);
            async Task<MmmClient> Register(string name) => new(http, name, (await new MmmClient(http, $"{name}-human").PerformAsync("Register", new Complements { With = [Profile(name)] })).Tokens[name]);
            var people = new Dictionary<string, MmmClient> { ["Anna"] = await Register("Anna"), ["Bruno"] = await Register("Bruno") };
            await services.StartAsync();
            foreach (var u in people.Values)
                await u.PerformAsync("MM-Send", Uc2.C($"Join-{u.ProcessID}", to: Meeting.BuildingService, with: Uc2.Message($"Join-{u.ProcessID}", Meeting.JoinText($"{u.ProcessID}-Persona"))));
            async Task Settle() { for (var i = 0; i < 4; i++) await services.PumpAsync(); }
            await Settle();
            var n = 0;
            foreach (var (who, said) in Script)
            {
                words = said;
                var id = $"Speech-{who}-{++n}";
                Assert.Equal(200, (await people[who].PerformAsync("MM-Send", Uc2.C(id, to: Meeting.Secretary, with: Speech.Make(id, new byte[3 * Speech.Rate * 2], 1_767_254_400_000)))).Http);
                await services.PumpAsync(); await services.PumpAsrAsync(); await Settle();
            }

            async Task<(JsonNode Reply, double Seconds)> Ask(MmmClient who, string request)
            {
                var id = $"Ask-{who.ProcessID}-{++n}";
                Assert.Equal(200, (await who.PerformAsync("MM-Send", Uc2.C(id, to: Meeting.Secretary, with: Uc2.Message(id, request)))).Http);
                await Settle();
                var clock = System.Diagnostics.Stopwatch.StartNew();
                await services.PumpSummaryAsync();
                var answer = (await who.InboxAsync()).Where(i => i.ID.StartsWith("Summary-for-", StringComparison.Ordinal)).Last();
                return (JsonNode.Parse(Meeting.TextOf((await who.ReadAsync(answer.ID)).Item)!)!, clock.Elapsed.TotalSeconds);
            }
            static string Data(JsonNode r, string key) => (string?)r[key]?["SummaryData"]?[0]?["Data"] ?? "";

            var (en, enS) = await Ask(people["Bruno"], "summary;lang=en;mode=written");
            var (fr, frS) = await Ask(people["Anna"], "summary;lang=fr;mode=written");
            var (it, itS) = await Ask(people["Bruno"], "summary;lang=it;mode=spoken");

            var english = Data(en, "Summary");
            var dir = Path.Combine(root, "Test", "Results", DateTime.Now.ToString("yyyy-MM-dd"));
            Directory.CreateDirectory(dir);
            var wavPath = Path.Combine(dir, "mmm-summary-real-model-it.wav");
            var pcm = Convert.FromBase64String((string?)it["Speech"] ?? "");
            if (pcm.Length > 0) File.WriteAllBytes(wavPath, Wav(pcm, Speech.Rate));
            var md = new StringBuilder();
            md.AppendLine($"# MMM summary on the real models ({model} on Ollama, Piper) - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm")).AppendLine();
            md.AppendLine("Test: Test\\Mpai.Aif.Tests\\MmmSummaryRealModelTests.cs. The minutes (what the ASR Service would have recognised; line 4 is noise):").AppendLine();
            foreach (var (who, said) in Script) md.AppendLine($"- {who}: {said}");
            md.AppendLine().AppendLine($"## English, written ({enS:F1} s)").AppendLine().AppendLine(english).AppendLine();
            md.AppendLine($"## French, written ({frS:F1} s)").AppendLine().AppendLine(Data(fr, "TranslatedSummary")).AppendLine();
            md.AppendLine("The English it was translated from:").AppendLine().AppendLine(Data(fr, "Summary")).AppendLine();
            md.AppendLine($"## Italian, spoken ({itS:F1} s)").AppendLine().AppendLine(Data(it, "TranslatedSummary")).AppendLine()
              .AppendLine("The English it was translated from:").AppendLine().AppendLine(Data(it, "Summary")).AppendLine()
              .AppendLine(pcm.Length > 0 ? $"Spoken: {Path.GetFileName(wavPath)}, {pcm.Length / 2.0 / Speech.Rate:F1} s." : "Spoken: NO SPEECH.");
            File.WriteAllText(Path.Combine(dir, $"mmm-summary-real-model-{model.Replace(':', '-')}.md"), md.ToString(), new UTF8Encoding(false));

            Assert.False(string.IsNullOrWhiteSpace(english), "no English summary");
            Assert.Contains("Anna", english); Assert.Contains("Bruno", english);
            Assert.Contains("23", english.Replace("twenty third", "23", StringComparison.OrdinalIgnoreCase).Replace("twenty-third", "23", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("slides", english, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("window", english, StringComparison.OrdinalIgnoreCase);       // the noise
            Assert.DoesNotContain("project", english, StringComparison.OrdinalIgnoreCase);      // nothing made up
            foreach (var part in new[] { "Discussed:", "Decisions:", "Actions:", "Open:" }) Assert.Contains(part, english);   // the four parts
            Assert.True(english.Length < string.Join("\n", Script.Select(s => $"{s.Who}: {s.Words}")).Length, "the summary is not shorter than the minutes");
            Assert.False(string.IsNullOrWhiteSpace(Data(fr, "TranslatedSummary")), "no French translation");
            Assert.NotEqual(english, Data(fr, "TranslatedSummary"));
            Assert.True(pcm.Length > Speech.Rate * 2, "the Italian summary was not spoken");
        }
        finally { await app.StopAsync(); }
    }

    private static byte[] Wav(byte[] pcm, int rate)
    {
        using var s = new MemoryStream(); using var w = new BinaryWriter(s);
        w.Write("RIFF"u8); w.Write(36 + pcm.Length); w.Write("WAVEfmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(pcm.Length); w.Write(pcm); return s.ToArray();
    }
}
