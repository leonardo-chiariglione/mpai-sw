using AIF.Controller;
using Mpai.Aif.Api;
using Mpai.Core;
using Mpai.Providers;

namespace Mpai.Aif.Tests;

// THE TST APP (Apps/MMC/TST, N-50 e, 2026-11-25): Text and Speech Translation (MMC-TST-V2.5) as an App of its own - not CPS, not MTS. Its workflow (MMC-TST.orch)
// takes the Language Selector once; each turn the person speaks or types, the Module is offered the Selector (OSD-SEL #1) and the words, and gives the translated
// Text Object (shown) and the translated Speech Object (played). The Module is the L3 1MMC-TST-V2.5-I01 (ASR, TTT, TTS), whose AIMs MtsProvider builds.
public class TstAppTests
{
    private const string Module = "1MMC-TST-V2.5-I01";

    [Fact]
    public async Task TheTstWorkflowTakesTheLanguagesOnceThenShowsAndPlaysEachTranslation()
    {
        var log = new List<string>();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var api = new ScriptedController(log, (turn, _) => new[]
        {
            new ControllerApi.Datum("OSD-BTO-V1.5", 1, MpaiJson.ToJson(BasicTextObject.FromText("traduzione " + turn))),
            new ControllerApi.Datum("OSD-BSO-V1.5", 1, MpaiJson.ToJson(new BasicSpeechObject { BasicSpeechObjectID = "translation " + turn, Data = new byte[400] }))
        });
        var typed = 0;
        var devices = new Mpai.Ua.DeviceRegistry()
            .RegisterAcquire("OSD-SEL-V1.5", (_, _) => { lock (log) log.Add("languages"); return Task.FromResult<string?>(MpaiJson.ToJson(BasicSelectorObject.Languages("en", "it"))); })
            .RegisterAcquire("OSD-BSO-V1.5", (_, _, abandon) => AppWorkflow.NothingUntil(abandon, stop.Token))
            .RegisterAcquire("OSD-BTO-V1.5", async (_, _, abandon) =>
            {
                var n = Interlocked.Increment(ref typed);
                if (n > 2) return await AppWorkflow.NothingUntil(abandon, stop.Token);
                lock (log) log.Add("typed " + n);
                return MpaiJson.ToJson(BasicTextObject.FromText("sentence " + n));
            })
            .RegisterPresent("screen", data =>
            {
                foreach (var kv in data) if (kv.Key.StartsWith("Display:OSD-BTO")) lock (log) log.Add("shown " + MpaiJson.FromJson<BasicTextObject>(kv.Value)!.GetText());
                return Task.CompletedTask;
            })
            .RegisterPresent("speech", data =>
            {
                if (data.ContainsKey("OSD-BSO-V1.5")) { lock (log) log.Add("played"); if (log.Count(l => l == "played") == 2) stop.Cancel(); }
                return Task.CompletedTask;
            });

        var why = await AppWorkflow.RunAsync(AppWorkflow.Load("TST"), api, devices, stop.Token);

        Assert.Equal(new[]
        {
            "start " + Module, "languages",
            "typed 1", "advance " + Module, "shown traduzione 1", "played",
            "typed 2", "advance " + Module, "shown traduzione 2", "played",
            "stop " + Module
        }, log);
        foreach (var turn in api.Turns) Assert.Contains(turn, d => d.DataType == "OSD-SEL-V1.5" && d.PortNumber == 1);
        Assert.Contains(api.Turns[0], d => d.DataType == "OSD-BTO-V1.5" && d.Json.Contains("sentence 1"));
        Assert.DoesNotContain(api.Turns[0], d => d.DataType is "OSD-B3O-V1.5" or "string");          // no avatar, no machine name: TST gives no Portable Avatar
        Assert.True(why.Length > 0);
    }

    // The real Module: a typed and a spoken English sentence, translated into Italian as text and as speech.
    [SkippableFact]
    [Trait("Group", "Models")]
    public void TheModuleTranslatesATypedAndASpokenSentenceIntoTextAndSpeech()
    {
        Skip.IfNot(TestModels.Can("ggml-small.bin", "tts-program"), "The speech models cannot be had.");
        Skip.IfNot(TestModels.Can("ttt-encoder", "voice-it"), "The translation or the Italian voice cannot be had.");
        var store = new AIF.Store.AmdStore(Repository.Amds); store.Scan();
        using var api = new ControllerApi(Repository.Amds, Path.Combine(Repository.Root, "AIMs", "aim-settings.json"), _ => new MtsProvider(store));
        var english = BasicTextObject.FromText("Good morning. The concert starts at eight.", new TextQualifier
            { TextQualifierID = Guid.NewGuid().ToString(), Attributes = new TextAttributes { Language = new Language { LanguageCode = "en", LanguageFormat = LanguageFormat.Iso639_1 } } });
        var languages = new ControllerApi.Datum("OSD-SEL-V1.5", 1, MpaiJson.ToJson(BasicSelectorObject.Languages("en", "it")));
        Assert.Equal(AifError.OK, api.StartFlow(Module, out var id));
        try
        {
            var typed = api.Advance(id, new[] { languages, new ControllerApi.Datum("OSD-BTO-V1.5", 1, MpaiJson.ToJson(english)) });
            Assert.True(typed.Ok, $"{Module}: {typed.Error}");
            var translation = MpaiJson.FromJson<BasicTextObject>(typed.ByType("OSD-BTO-V1.5")!)!.GetText();
            Assert.Contains("concert", translation, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(english.GetText(), translation);
            Assert.True(MpaiJson.FromJson<BasicSpeechObject>(typed.ByType("OSD-BSO-V1.5")!)!.Data.Length > 200, "the translation is not spoken");

            var spoken = Mpai.Aims.Tts.TtsFactory.Create(TestModels.Settings("1MMC-TTS-V2.5-I01")).ProcessAsync(english).GetAwaiter().GetResult();
            var heard = api.Advance(id, new[] { languages, new ControllerApi.Datum("OSD-BSO-V1.5", MpaiJson.ToJson(spoken)) });
            Assert.True(heard.Ok, $"{Module}: {heard.Error}");
            Assert.Contains("concert", MpaiJson.FromJson<BasicTextObject>(heard.ByType("OSD-BTO-V1.5")!)!.GetText(), StringComparison.OrdinalIgnoreCase);
            Assert.True(MpaiJson.FromJson<BasicSpeechObject>(heard.ByType("OSD-BSO-V1.5")!)!.Data.Length > 200, "the translation of the spoken sentence is not spoken");
        }
        finally { api.StopFlow(id); }
    }
}
