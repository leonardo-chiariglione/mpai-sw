using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AIF.Controller;
using AIF.Store;
using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Osd.Bas;
using Mpai.Osd.Bvs;
using Mpai.Osd.Bls;

namespace Osd.Describers.Test;

// Standalone check for the three describer processors: feed one Basic Object,
// confirm a Basic Scene Descriptors comes back containing it. Runs each describer
// through the real AmdStore/AimPortReader path - no full MMC-HCI needed.
public static class Program
{
    public static async Task<int> Main()
    {
        var store = new AmdStore(@"D:\AI\AIMs\AMDs");
        store.Scan();
        int fails = 0;

        {
            var proc = new BvsAimProcessor("OSD-BVS-V1.5", AimPortReader.Load(store, "OSD-BVS-V1.5"));
            var obj  = new BasicVisualObject { BasicVisualObjectID = "vo-1", Data = new byte[] { 1, 2, 3 } };
            var msg  = new Message { MessageId = "t1", MessageType = "test",
                       Ports = new Dictionary<string,string> { ["BasicVisualObject"] = MpaiJson.ToJson(obj) } };
            fails += Report("OSD-BVS", await proc.ProcessAsync(msg), "OSD-BVS-V1.5");
        }
        {
            var proc = new BasAimProcessor("OSD-BAS-V1.5", AimPortReader.Load(store, "OSD-BAS-V1.5"));
            var obj  = new BasicAudioObject { BasicAudioObjectID = "ao-1" };
            var msg  = new Message { MessageId = "t2", MessageType = "test",
                       Ports = new Dictionary<string,string> { ["BasicAudioObject"] = MpaiJson.ToJson(obj) } };
            fails += Report("OSD-BAS", await proc.ProcessAsync(msg), "OSD-BAS-V1.5");
        }
        {
            var proc = new BlsAimProcessor("OSD-BLS-V1.5", AimPortReader.Load(store, "OSD-BLS-V1.5"));
            var obj  = new BasicLiDARObject { BasicLiDARObjectID = "lo-1" };
            var msg  = new Message { MessageId = "t3", MessageType = "test",
                       Ports = new Dictionary<string,string> { ["BasicLiDARObject"] = MpaiJson.ToJson(obj) } };
            fails += Report("OSD-BLS", await proc.ProcessAsync(msg), "OSD-BLS-V1.5");
        }

        Console.WriteLine(fails == 0 ? "\nALL THREE DESCRIBERS OK" : $"\n{fails} FAILED");
        return fails;
    }

    // Success = an output port whose payload carries the expected scene-descriptors
    // Header. An error Message carries no such output port.
    private static int Report(string name, Message outMsg, string expectHeader)
    {
        foreach (var kv in outMsg.Ports)
        {
            if (kv.Value != null && kv.Value.Contains(expectHeader))
            {
                Console.WriteLine($"[{name}] OK - out port '{kv.Key}' carries {expectHeader}");
                Console.WriteLine("   " + kv.Value.Substring(0, Math.Min(160, kv.Value.Length)));
                return 0;
            }
        }
        Console.WriteLine($"[{name}] FAIL - no output port carrying {expectHeader}. Ports: [" +
                          string.Join(", ", outMsg.Ports.Keys) + "]");
        return 1;
    }
}
