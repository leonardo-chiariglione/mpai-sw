using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Core.OSD;

namespace Vii.Host;

// OSD-VII end-to-end test: feeds an image as a Basic Visual Object through the
// Controller (UAG-VII-V1.0 -> OSD-VII) and prints the returned Visual Instance
// Identifier. Proves Visual Instance Identification runs as an AIW, not just the
// detector standalone.
//
//   dotnet run --project Hosts\Vii.Host -- [imagePath]
internal static class Program
{
    private const string ViiAiw = "UAG-VII-V1.0";

    private static void Main(string[] args)
    {
        AimLog.ToConsole();

        string imagePath = args.Length > 0 ? args[0] : @"D:\AI\TestData\Images\zebra.jpg";
        if (!File.Exists(imagePath)) { Console.WriteLine($"image not found: {imagePath}"); return; }

        var store = new AmdStore(@"D:\AI\AIMs\AMDs");
        store.Scan();
        var settings = AimSettings.Load(@"D:\AI\AIMs\aim-settings.json");

        var ua       = new UserAgent(store);
        var provider = new ViiHostProvider(store);

        Console.WriteLine($"Identifying the visual object in {imagePath}");
        var bvo = BasicVisualObject.FromFile(imagePath, File.ReadAllBytes(imagePath));

        ua.MPAI_AIFU_Controller_Initialize();
        if (ua.MPAI_AIFU_AIW_Start(ViiAiw, provider, settings, out var aiwId) != AifError.OK)
        { Console.WriteLine($"could not start {ViiAiw}"); return; }

        InstanceIdentifier? iid = null;
        try
        {
            var boundary = new Dictionary<string, string>
            {
                ["InputVisualObject"] = MpaiJson.ToJson(bvo)
            };
            var (error, outcome) = ua.RunAsync(aiwId, boundary).GetAwaiter().GetResult();
            if (error != AifError.OK || outcome?.Completed is null)
            { Console.WriteLine($"run failed: {error}"); return; }
            if (outcome.Completed.IsError)
            { Console.WriteLine($"{outcome.Completed.FailedAim}: {outcome.Completed.Payload}"); return; }

            var json = outcome.Completed.Ports.TryGetValue("VisualInstanceID", out var j)
                ? j : outcome.Completed.Ports.Values.FirstOrDefault();
            iid = string.IsNullOrWhiteSpace(json) ? null : MpaiJson.FromJson<InstanceIdentifier>(json);
        }
        finally { ua.MPAI_AIFU_AIW_Stop(aiwId); }

        Console.WriteLine();
        if (iid is null || iid.InstanceIdentifierData.Count == 0)
        { Console.WriteLine("=> no identification"); return; }

        var top = iid.InstanceIdentifierData[0];
        var taxonomy = top.Taxonomy?.TaxonomyLevelIDs is { } t ? string.Join(" / ", t) : "";
        Console.WriteLine($"=> IDENTIFIED: {top.InstanceLabel} ({top.LabelConfidenceLevel:F2})");
        Console.WriteLine($"   taxonomy: {taxonomy}");
    }
}
