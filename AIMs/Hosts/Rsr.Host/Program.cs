using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Core.OSD;

namespace Rsr.Host;

// RSR speech-path test: feeds the machine's response Text and Personal Status
// (as Entity Dialogue Processing produces them) through the Controller
// (UAG-RSR-V1.0 -> PAF-PSD + MMC-TTS + MMC-SOD), and the machine SPEAKS the
// response aloud.
//
//   dotnet run --project Hosts\Rsr.Host -- "Of course, right this way."
internal static class Program
{
    private const string RsrAiw = "UAG-RSR-V1.0";

    private static void Main(string[] args)
    {
        AimLog.ToConsole();
        string text = args.Length > 0 ? string.Join(' ', args) : "Of course, right this way.";

        var store = new AmdStore(@"D:\AI\AIMs\AMDs");
        store.Scan();
        var settings = AimSettings.Load(@"D:\AI\AIMs\aim-settings.json");
        var ua       = new UserAgent(store);
        var provider = new RsrHostProvider(store);

        // The machine's Personal Status (as EDP produces it): calm + respectful.
        var machineEps = new EntityPersonalStatus
        {
            TextPersonalStatus = new TextPersonalStatus
            {
                TextEmotion        = Emotion.Of(FactorLabel.Of("CALMNESS", "calm", null, 0.8)),
                TextSocialAttitude = SocialAttitude.Of(FactorLabel.Of("SOCIAL RANK", "respectful", null, 0.8))
            }
        };

        Console.WriteLine($"CAV says: \"{text}\"");
        ua.MPAI_AIFU_Controller_Initialize();
        if (ua.MPAI_AIFU_AIW_Start(RsrAiw, provider, settings, out var aiwId) != AifError.OK)
        { Console.WriteLine($"could not start {RsrAiw}"); return; }

        try
        {
            var boundary = new Dictionary<string, string>
            {
                ["TextObject"]     = MpaiJson.ToJson(BasicTextObject.FromText(text)),
                ["PersonalStatus"] = MpaiJson.ToJson(machineEps)
            };
            var (error, outcome) = ua.RunAsync(aiwId, boundary).GetAwaiter().GetResult();
            if (error != AifError.OK || outcome?.Completed is null) { Console.WriteLine($"run failed: {error}"); return; }
            if (outcome.Completed.IsError) { Console.WriteLine($"{outcome.Completed.FailedAim}: {outcome.Completed.Payload}"); return; }
            Console.WriteLine("(spoken)");
        }
        finally { ua.MPAI_AIFU_AIW_Stop(aiwId); }
    }
}
