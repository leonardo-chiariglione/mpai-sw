using System;
using System.Collections.Generic;
using AIF.Controller;
using AIF.Store;
using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Aims.Speech;   // WinmmSpeechDelivery (the UA delivers the produced speech)
namespace Rsr.Host;
// RSR test: feeds the machine's response Text + Personal Status through the Controller
// (UAG-RSR-V1.0 -> PAF-PSD + MMC-TTS + PAF-GFD). RSR PRODUCES the Machine Speech and
// the Machine Face Descriptors (the facial animation timeline, with text-driven
// lip-sync). RSR produces; the User Agent delivers - so this host then speaks the
// produced speech aloud (SOD) and reports the produced face-descriptor timeline.
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

        var machineEps = new EntityPersonalStatus
        {
            TextPersonalStatus = new TextPersonalStatus
            {
                TextEmotion        = Emotion.Of(FactorLabel.Of("CALMNESS", "calm", null, 0.8)),
                TextSocialAttitude = SocialAttitude.Of(FactorLabel.Of("SOCIAL RANK", "respectful", null, 0.8))
            }
        };

        Console.WriteLine($"CAV response: \"{text}\"");
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

            var outputs = outcome.Completed.Ports;

            // RSR produced the Machine Face Descriptors (the animation timeline).
            if (outputs.TryGetValue("MachineFaceDescriptors", out var fdoJson) && !string.IsNullOrWhiteSpace(fdoJson))
            {
                var fdo = MpaiJson.FromJson<FaceDescriptorsObject>(fdoJson);
                int frames = fdo?.FaceDescriptorsData?.Count ?? 0;
                Console.WriteLine($"Machine Face Descriptors: {frames} animation frames (lip-sync + expression). {fdo?.DescrMetadata}");
            }
            else Console.WriteLine("Machine Face Descriptors: (none)");

            // RSR produced the Machine Speech; the UA delivers it (speaks it aloud).
            if (outputs.TryGetValue("MachineSpeech", out var speechJson) && !string.IsNullOrWhiteSpace(speechJson))
            {
                var speech = MpaiJson.FromJson<BasicSpeechObject>(speechJson);
                Console.WriteLine($"Machine Speech: {speech?.Data.Length ?? 0:N0} bytes - speaking...");
#if WINDOWS_DEVICES
                if (speech is not null && speech.Data.Length > 0)
                    new WinmmSpeechDelivery().DeliverAsync(speech).GetAwaiter().GetResult();
#endif
                Console.WriteLine("(spoken)");
            }
            else Console.WriteLine("Machine Speech: (none)");
        }
        finally { ua.MPAI_AIFU_AIW_Stop(aiwId); }
    }
}
