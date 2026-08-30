using System;
using System.Collections.Generic;
using System.Linq;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Core.OSD;

namespace Edp.Host;

// EDP end-to-end test: feeds the user's text (and an illustrative Personal Status)
// through the Controller (UAG-EDP-V1.0 -> MMC-EDP), which calls the local Ollama
// LLM, and prints the Machine's response Text and Personal Status.
//
//   dotnet run --project Hosts\Edp.Host -- "Hello, can you open the door for me?"
internal static class Program
{
    private const string EdpAiw = "UAG-EDP-V1.0";

    private static void Main(string[] args)
    {
        AimLog.ToConsole();
        string text = args.Length > 0 ? string.Join(' ', args) : "Hello, can you open the door for me?";

        var store = new AmdStore(@"D:\AI\AIMs\AMDs");
        store.Scan();
        var settings = AimSettings.Load(@"D:\AI\AIMs\aim-settings.json");
        var ua       = new UserAgent(store);
        var provider = new EdpHostProvider(store);

        // Illustrative user Personal Status: respectful (words), calm (voice), happy (face).
        var eps = new EntityPersonalStatus
        {
            TextPersonalStatus   = new TextPersonalStatus { TextSocialAttitude = SocialAttitude.Of(FactorLabel.Of("SOCIAL RANK", "respectful", null, 0.7)) },
            SpeechPersonalStatus = new SpeechPersonalStatus { SpeechEmotion = Emotion.Of(FactorLabel.Of("CALMNESS", "calm", null, 0.6)) },
            FacePersonalStatus   = new FacePersonalStatus { FaceEmotion = Emotion.Of(FactorLabel.Of("HAPPINESS", "happy", null, 0.84)) }
        };

        Console.WriteLine($"User: \"{text}\"");
        ua.MPAI_AIFU_Controller_Initialize();
        if (ua.MPAI_AIFU_AIW_Start(EdpAiw, provider, settings, out var aiwId) != AifError.OK)
        { Console.WriteLine($"could not start {EdpAiw}"); return; }

        try
        {
            var boundary = new Dictionary<string, string>
            {
                ["TextObject"]     = MpaiJson.ToJson(BasicTextObject.FromText(text)),
                ["PersonalStatus"] = MpaiJson.ToJson(eps)
            };
            var (error, outcome) = ua.RunAsync(aiwId, boundary).GetAwaiter().GetResult();
            if (error != AifError.OK || outcome?.Completed is null) { Console.WriteLine($"run failed: {error}"); return; }
            if (outcome.Completed.IsError) { Console.WriteLine($"{outcome.Completed.FailedAim}: {outcome.Completed.Payload}"); return; }

            Console.WriteLine();
            if (outcome.Completed.Ports.TryGetValue("MachineTextObject", out var mtJson))
                Console.WriteLine($"CAV: \"{MpaiJson.FromJson<BasicTextObject>(mtJson)?.GetText()}\"");

            if (outcome.Completed.Ports.TryGetValue("MachinePersonalStatus", out var mpsJson))
            {
                var mps = MpaiJson.FromJson<EntityPersonalStatus>(mpsJson);
                var em = mps?.TextPersonalStatus?.TextEmotion;
                var at = mps?.TextPersonalStatus?.TextSocialAttitude;
                Console.WriteLine($"CAV Personal Status: emotion {em?.Category}/{em?.GeneralAdjectival}, attitude {at?.Category}/{at?.GeneralAdjectival}");
            }
            if (outcome.Completed.Ports.TryGetValue("EditedSummary", out var sumJson))
            {
                var sum = MpaiJson.FromJson<Mpai.Mmc.Edp.Summary>(sumJson);
                Console.WriteLine($"Summary: {sum?.Text()}");
            }
        }
        finally { ua.MPAI_AIFU_AIW_Stop(aiwId); }
    }
}
