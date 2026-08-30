using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Core.OSD;

namespace Pse.Host;

// PSE end-to-end test: produces each modality's Personal Status through the
// Controller (Text via UAG-NLU, Speech via UAG-ESI, Face via UAG-EFI), then
// assembles them into the Entity Personal Status via UAG-PSM, and prints it.
//
//   dotnet run --project Hosts\Pse.Host -- "Open the door for Leonardo please"
internal static class Program
{
    private static void Main(string[] args)
    {
        AimLog.ToConsole();
        string text = args.Length > 0 ? string.Join(' ', args) : "Open the door for Leonardo please";
        string wav  = @"D:\AI\TestData\Audio\leonardo.wav";
        string img  = @"D:\AI\TestData\Images\leonardo.jpg";

        var store = new AmdStore(@"D:\AI\AIMs\AMDs");
        store.Scan();
        var settings = AimSettings.Load(@"D:\AI\AIMs\aim-settings.json");
        var ua       = new UserAgent(store);
        var provider = new PseHostProvider(store);
        ua.MPAI_AIFU_Controller_Initialize();

        // 1. Text -> TPS (via UAG-NLU)
        string? tps = RunOne(ua, provider, settings, "UAG-NLU-V1.0",
            new() { ["InputText"] = MpaiJson.ToJson(BasicTextObject.FromText(text)) },
            "TextPersonalStatus");

        // 2. Speech -> SPS (via UAG-ESI)
        string? sps = File.Exists(wav)
            ? RunOne(ua, provider, settings, "UAG-ESI-V1.0",
                new() { ["InputSpeech"] = MpaiJson.ToJson(BasicSpeechObject.FromData(File.ReadAllBytes(wav))) },
                "SpeechPersonalStatus")
            : null;

        // 3. Face -> FPS (via UAG-EFI)
        string? fps = File.Exists(img)
            ? RunOne(ua, provider, settings, "UAG-EFI-V1.0",
                new() { ["InputVisual"] = MpaiJson.ToJson(BasicVisualObject.FromFile(img, File.ReadAllBytes(img))) },
                "FacePersonalStatus")
            : null;

        // 4. Assemble -> EPS (via UAG-PSM)
        var psmBoundary = new Dictionary<string, string>();
        if (tps is not null) psmBoundary["TextPersonalStatus"]   = tps;
        if (sps is not null) psmBoundary["SpeechPersonalStatus"] = sps;
        if (fps is not null) psmBoundary["FacePersonalStatus"]   = fps;

        string? epsJson = RunOne(ua, provider, settings, "UAG-PSM-V1.0", psmBoundary, "EntityPersonalStatus");

        Console.WriteLine();
        Console.WriteLine("=== Entity Personal Status ===");
        var eps = epsJson is null ? null : MpaiJson.FromJson<EntityPersonalStatus>(epsJson);
        if (eps is null) { Console.WriteLine("(none)"); return; }
        PrintModality("Text",    eps.TextPersonalStatus?.TextCognitiveState, eps.TextPersonalStatus?.TextEmotion, eps.TextPersonalStatus?.TextSocialAttitude);
        PrintModality("Speech",  eps.SpeechPersonalStatus?.SpeechCognitiveState, eps.SpeechPersonalStatus?.SpeechEmotion, eps.SpeechPersonalStatus?.SpeechSocialAttitude);
        PrintModality("Face",    eps.FacePersonalStatus?.FaceCognitiveState, eps.FacePersonalStatus?.FaceEmotion, eps.FacePersonalStatus?.FaceSocialAttitude);
    }

    private static string? RunOne(UserAgent ua, IAimProvider provider, AimSettings settings,
                                  string aiw, Dictionary<string,string> boundary, string outPort)
    {
        if (ua.MPAI_AIFU_AIW_Start(aiw, provider, settings, out var aiwId) != AifError.OK)
        { Console.WriteLine($"could not start {aiw}"); return null; }
        try
        {
            var (error, outcome) = ua.RunAsync(aiwId, boundary).GetAwaiter().GetResult();
            if (error != AifError.OK || outcome?.Completed is null) { Console.WriteLine($"{aiw} run failed: {error}"); return null; }
            if (outcome.Completed.IsError) { Console.WriteLine($"{aiw}: {outcome.Completed.FailedAim}: {outcome.Completed.Payload}"); return null; }
            return outcome.Completed.Ports.TryGetValue(outPort, out var j) ? j : outcome.Completed.Ports.Values.FirstOrDefault();
        }
        finally { ua.MPAI_AIFU_AIW_Stop(aiwId); }
    }

    private static void PrintModality(string name, CognitiveState? cs, Emotion? em, SocialAttitude? sa)
    {
        if (cs is null && em is null && sa is null) { Console.WriteLine($"  {name}: (absent)"); return; }
        Console.WriteLine($"  {name}:");
        Console.WriteLine($"    Cognitive State: {Describe(cs?.Category, cs?.GeneralAdjectival, cs?.SpecificAdjectival, cs?.Degree)}");
        Console.WriteLine($"    Emotion:         {Describe(em?.Category, em?.GeneralAdjectival, em?.SpecificAdjectival, em?.Degree)}");
        Console.WriteLine($"    Social Attitude: {Describe(sa?.Category, sa?.GeneralAdjectival, sa?.SpecificAdjectival, sa?.Degree)}");
    }

    private static string Describe(string? category, string? general, string? specific, double? degree)
    {
        if (category is null && general is null && specific is null) return "(none)";
        var parts = new List<string>();
        if (category is not null) parts.Add(category);
        if (general  is not null) parts.Add(general);
        if (specific is not null) parts.Add(specific);
        var label = string.Join(" / ", parts);
        return degree is null ? label : $"{label} (Degree {degree:F2})";
    }
}
