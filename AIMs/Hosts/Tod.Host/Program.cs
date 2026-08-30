using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;
using Mpai.Core.OSD;

namespace Tod.Host;

// 3OD end-to-end test: feeds a 3D Model Object + a Face animation (Face Descriptors)
// through the Controller (UAG-3OD-V1.0 -> OSD-3OD), which delivers the scene (model
// + animation) to the (headless) renderer device.
//
//   dotnet run --project Hosts\Tod.Host
internal static class Program
{
    private const string TodAiw = "UAG-3OD-V1.0";

    private static void Main()
    {
        AimLog.ToConsole();
        var store = new AmdStore(@"D:\AI\AIMs\AMDs");
        store.Scan();
        var settings = AimSettings.Load(@"D:\AI\AIMs\aim-settings.json");
        var ua       = new UserAgent(store);
        var provider = new TodHostProvider(store);

        var model = Basic3DModelObject.FromData(new byte[] { 1, 2, 3, 4 });
        var faceAnim = new FaceDescriptorsObject();   // a (stand-in) face animation stream

        Console.WriteLine("Delivering a 3D scene (model + face animation)...");
        ua.MPAI_AIFU_Controller_Initialize();
        if (ua.MPAI_AIFU_AIW_Start(TodAiw, provider, settings, out var aiwId) != AifError.OK)
        { Console.WriteLine($"could not start {TodAiw}"); return; }
        try
        {
            var boundary = new Dictionary<string, string>
            {
                ["ModelObject"]   = MpaiJson.ToJson(model),
                ["FaceAnimation"] = MpaiJson.ToJson(faceAnim)
            };
            var (error, outcome) = ua.RunAsync(aiwId, boundary).GetAwaiter().GetResult();
            if (error != AifError.OK || outcome?.Completed is null) { Console.WriteLine($"run failed: {error}"); return; }
            if (outcome.Completed.IsError) { Console.WriteLine($"{outcome.Completed.FailedAim}: {outcome.Completed.Payload}"); return; }
            Console.WriteLine("(delivered)");
        }
        finally { ua.MPAI_AIFU_AIW_Stop(aiwId); }
    }
}
