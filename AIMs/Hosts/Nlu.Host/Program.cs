using System;
using System.Collections.Generic;
using System.Linq;
using AIF.Controller;
using AIF.Store;
using Mpai.Core;
using Mpai.Core.OSD;
namespace Nlu.Host;
// MMC-NLU end-to-end test: feeds a text as a Basic Text Object through the
// Controller (UAG-NLU-V1.0 -> MMC-NLU) and prints the returned Text Descriptors
// (the four taggings), the Refined Text, and the Text Personal Status (the three
// Personal Status Factors). Proves Natural Language Understanding runs as an AIW,
// emitting a spec-conformant Text Descriptors Object (MMC-TDO) and Text Personal
// Status (MMC-TPS).
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
            if (outcome.Completed.Ports.TryGetValue("TextDescriptors", out var tdoJson) && !string.IsNullOrWhiteSpace(tdoJson))
            {
                var tdo = MpaiJson.FromJson<TextDescriptorsObject>(tdoJson);
                var basic = tdo?.Basic();
                var t = basic?.TextDescriptorsData;
                Console.WriteLine("Text Descriptors:");
                Console.WriteLine($"  POS:  {t?.POS_tagging?.Result ?? "(null)"}");
                Console.WriteLine($"  NE:   {t?.NE_tagging?.Result ?? "(null)"}");
                Console.WriteLine($"  dep:  {t?.dependency_tagging?.Result ?? "(null)"}");
                Console.WriteLine($"  SRL:  {t?.SRL_tagging?.Result ?? "(null)"}");
            }
            if (outcome.Completed.Ports.TryGetValue("TextPersonalStatus", out var tpsJson) && !string.IsNullOrWhiteSpace(tpsJson))
            {
                var tps = MpaiJson.FromJson<TextPersonalStatus>(tpsJson);
                Console.WriteLine("Text Personal Status:");
                Console.WriteLine($"  Cognitive State: {Describe(tps?.TextCognitiveState?.Category, tps?.TextCognitiveState?.GeneralAdjectival, tps?.TextCognitiveState?.SpecificAdjectival, tps?.TextCognitiveState?.Degree)}");
                Console.WriteLine($"  Emotion:         {Describe(tps?.TextEmotion?.Category, tps?.TextEmotion?.GeneralAdjectival, tps?.TextEmotion?.SpecificAdjectival, tps?.TextEmotion?.Degree)}");
                Console.WriteLine($"  Social Attitude: {Describe(tps?.TextSocialAttitude?.Category, tps?.TextSocialAttitude?.GeneralAdjectival, tps?.TextSocialAttitude?.SpecificAdjectival, tps?.TextSocialAttitude?.Degree)}");
            }
        }
        finally { ua.MPAI_AIFU_AIW_Stop(aiwId); }
    }

    // Render a Factor label as "CATEGORY / general / specific (Degree 0.70)", omitting
    // absent levels; "(none)" when the Factor was not present.
    private static string Describe(string? category, string? general, string? specific, double? degree)
    {
        if (category is null && general is null && specific is null) return "(none)";
        var parts = new System.Collections.Generic.List<string>();
        if (category is not null) parts.Add(category);
        if (general  is not null) parts.Add(general);
        if (specific is not null) parts.Add(specific);
        var label = string.Join(" / ", parts);
        return degree is null ? label : $"{label} (Degree {degree:F2})";
    }
}
