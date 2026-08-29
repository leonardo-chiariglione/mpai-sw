using System;
using System.Collections.Generic;
using System.Linq;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Core.OSD;

namespace Nlu.Host;

// MMC-NLU end-to-end test: feeds a text as a Basic Text Object through the
// Controller (UAG-NLU-V1.0 -> MMC-NLU) and prints the returned Meaning (the four
// taggings) and Refined Text. Proves Natural Language Understanding runs as an
// AIW, emitting a spec-conformant Text Descriptors Object (MMC-TDO).
//
//   dotnet run --project Hosts\Nlu.Host -- "Open the door for Leonardo"
internal static class Program
{
    private const string NluAiw = "UAG-NLU-V1.0";

    private static void Main(string[] args)
    {
        AimLog.ToConsole();

        string text = args.Length > 0 ? string.Join(' ', args) : "Open the door for Leonardo please";

        var store = new AmdStore(@"D:\AI\AIMs\AMDs");
        store.Scan();
        var settings = AimSettings.Load(@"D:\AI\AIMs\aim-settings.json");

        var ua       = new UserAgent(store);
        var provider = new NluHostProvider(store);

        Console.WriteLine($"Understanding: \"{text}\"");
        var bto = BasicTextObject.FromText(text);

        ua.MPAI_AIFU_Controller_Initialize();
        if (ua.MPAI_AIFU_AIW_Start(NluAiw, provider, settings, out var aiwId) != AifError.OK)
        { Console.WriteLine($"could not start {NluAiw}"); return; }

        try
        {
            // Feed as Input Text (PortNumber 1, the default) - simplest first proof.
            var boundary = new Dictionary<string, string>
            {
                ["InputText"] = MpaiJson.ToJson(bto)
            };
            var (error, outcome) = ua.RunAsync(aiwId, boundary).GetAwaiter().GetResult();
            if (error != AifError.OK || outcome?.Completed is null)
            { Console.WriteLine($"run failed: {error}"); return; }
            if (outcome.Completed.IsError)
            { Console.WriteLine($"{outcome.Completed.FailedAim}: {outcome.Completed.Payload}"); return; }

            Console.WriteLine();

            if (outcome.Completed.Ports.TryGetValue("RefinedText", out var refinedJson) && !string.IsNullOrWhiteSpace(refinedJson))
            {
                var refined = MpaiJson.FromJson<BasicTextObject>(refinedJson);
                Console.WriteLine($"Refined Text: {refined?.GetText()}");
            }

            if (outcome.Completed.Ports.TryGetValue("Meaning", out var meaningJson) && !string.IsNullOrWhiteSpace(meaningJson))
            {
                var meaning = MpaiJson.FromJson<TextDescriptorsObject>(meaningJson);
                var basic = meaning?.Basic();
                var t = basic?.TextDescriptorsData;
                Console.WriteLine("Meaning (Text Descriptors):");
                Console.WriteLine($"  POS:  {t?.POS_tagging?.Result ?? "(null)"}");
                Console.WriteLine($"  NE:   {t?.NE_tagging?.Result ?? "(null)"}");
                Console.WriteLine($"  dep:  {t?.dependency_tagging?.Result ?? "(null)"}");
                Console.WriteLine($"  SRL:  {t?.SRL_tagging?.Result ?? "(null)"}");
            }
        }
        finally { ua.MPAI_AIFU_AIW_Stop(aiwId); }
    }
}
