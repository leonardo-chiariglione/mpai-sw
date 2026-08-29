using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Core.OSD;

namespace Ebd.Host;

// PAF-EBD end-to-end test: feeds an image as a Body Visual Object through the
// Controller (UAG-EBD-V1.0 -> PAF-EBD) and prints the returned Body Descriptors
// (PAF-BDO) - its content format and the first lines of the BVH skeleton.
//
//   dotnet run --project Hosts\Ebd.Host -- [imagePath]
internal static class Program
{
    private const string EbdAiw = "UAG-EBD-V1.0";

    private static void Main(string[] args)
    {
        AimLog.ToConsole();

        string imagePath = args.Length > 0 ? args[0] : @"D:\AI\TestData\Images\leonardo.jpg";
        if (!File.Exists(imagePath)) { Console.WriteLine($"image not found: {imagePath}"); return; }

        var store = new AmdStore(@"D:\AI\AIMs\AMDs");
        store.Scan();
        var settings = AimSettings.Load(@"D:\AI\AIMs\aim-settings.json");

        var ua       = new UserAgent(store);
        var provider = new EbdHostProvider(store);

        Console.WriteLine($"Describing the body in {imagePath}");
        var bvo = BasicVisualObject.FromFile(imagePath, File.ReadAllBytes(imagePath));

        ua.MPAI_AIFU_Controller_Initialize();
        if (ua.MPAI_AIFU_AIW_Start(EbdAiw, provider, settings, out var aiwId) != AifError.OK)
        { Console.WriteLine($"could not start {EbdAiw}"); return; }

        try
        {
            var boundary = new Dictionary<string, string> { ["InputVisual"] = MpaiJson.ToJson(bvo) };
            var (error, outcome) = ua.RunAsync(aiwId, boundary).GetAwaiter().GetResult();
            if (error != AifError.OK || outcome?.Completed is null)
            { Console.WriteLine($"run failed: {error}"); return; }
            if (outcome.Completed.IsError)
            { Console.WriteLine($"{outcome.Completed.FailedAim}: {outcome.Completed.Payload}"); return; }

            var json = outcome.Completed.Ports.TryGetValue("BodyDescriptors", out var j)
                ? j : outcome.Completed.Ports.Values.FirstOrDefault();
            var bdo = string.IsNullOrWhiteSpace(json) ? null : MpaiJson.FromJson<BodyDescriptorsObject>(json);

            Console.WriteLine();
            if (bdo is null) { Console.WriteLine("=> no body descriptors"); return; }
            Console.WriteLine($"=> Body Descriptors: {bdo.Header}, ContentFormat={bdo.GetContentFormat()}");
            var bvh = bdo.Content() ?? "";
            var head = string.Join("\n", bvh.Split('\n').Take(12));
            Console.WriteLine("   BVH (first lines):");
            Console.WriteLine(head);
        }
        finally { ua.MPAI_AIFU_AIW_Stop(aiwId); }
    }
}
