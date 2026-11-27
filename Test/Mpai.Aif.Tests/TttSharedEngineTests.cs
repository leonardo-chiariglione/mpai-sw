using Mmc.Ttt.Onnx;
using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Aif.Tests;

// ONE TRANSLATION MODEL FOR EVERY TTT AIM (N-51, 2026-11-27). Each Module Instance on the MAS instance has a Controller of its own and so a TTT AIM of its own; the AIMs made
// from the same model files share one engine (M2M-100 loaded once), which is closed when the last AIM is disposed. Two AIMs translate at once, the first goes, the second still
// translates, the second goes: the engine is closed.
// The model files are named here with a "." folder in the encoder's path, so that this engine is not the one other tests running at the same time open with the settings' path.
public class TttSharedEngineTests
{
    [SkippableFact]
    [Trait("Group", "Models")]
    public void TwoTttAimsShareOneModelAndTheLastClosesIt()
    {
        Skip.IfNot(TestModels.Can("ttt-encoder"), "The translation model cannot be had.");
        var settings = TestModels.Settings("1MMC-TTT-V2.5-I01");
        string P(string key) => MpaiPaths.Resolve(settings[key]);
        var encoder = P("TttEncoderModel");
        var configuration = new OnnxTttConfiguration
        {
            EncoderModelPath      = Path.Combine(Path.GetDirectoryName(encoder)!, ".", Path.GetFileName(encoder)),
            DecoderFirstModelPath = P("TttDecoderFirstModel"),
            DecoderPastModelPath  = P("TttDecoderPastModel"),
            SpmModelPath          = P("TttSpmModel"),
            VocabPath             = P("TttVocab"),
            SpecialTokensPath     = settings.TryGetValue("TttSpecialTokens", out var s) && s.Length > 0 ? MpaiPaths.Resolve(s) : null
        };

        var first = new OnnxTttAim(configuration);
        var second = new OnnxTttAim(configuration);
        Assert.Equal(2, first.EngineHolders);
        Assert.True(first.SharesEngineWith(second));

        var languages = BasicSelectorObject.Languages("en", "it");
        Task<BasicTextObject> Say(OnnxTttAim aim, string name) => aim.ProcessAsync(BasicTextObject.FromText($"My name is {name} and the concert starts at eight."), languages);
        var both = Task.WhenAll(Say(first, "Marco"), Say(second, "Giulia")).GetAwaiter().GetResult();
        Assert.Contains("Marco", both[0].GetText());
        Assert.Contains("Giulia", both[1].GetText());

        first.Dispose();
        first.Dispose();      // twice: lets go once
        Assert.Equal(1, second.EngineHolders);
        Assert.Contains("Paolo", Say(second, "Paolo").GetAwaiter().GetResult().GetText());

        second.Dispose();
        Assert.Equal(0, second.EngineHolders);
    }
}
