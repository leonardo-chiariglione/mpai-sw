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
using Mpai.Osd.Ava;

namespace Osd.Describers.Test;

// Front-end check: the three describers turn Basic Objects into Basic Scene
// Descriptors; Audio-Visual Alignment then fuses them, giving the visual object
// its point of view from the LiDAR object, and emits the two scene geometries.
public static class Program
{
    public static async Task<int> Main()
    {
        var store = new AmdStore(@"D:\AI\AIMs\AMDs");
        store.Scan();
        int fails = 0;

        // --- describers ---
        var bvsOut = await Run(new BvsAimProcessor("OSD-BVS-V1.5", AimPortReader.Load(store, "OSD-BVS-V1.5")),
                               "BasicVisualObject", MpaiJson.ToJson(new BasicVisualObject { BasicVisualObjectID = "vo-1", Data = new byte[]{1,2,3} }));
        var basOut = await Run(new BasAimProcessor("OSD-BAS-V1.5", AimPortReader.Load(store, "OSD-BAS-V1.5")),
                               "BasicAudioObject", MpaiJson.ToJson(new BasicAudioObject { BasicAudioObjectID = "ao-1" }));
        var blsIn  = new BasicLiDARObject { BasicLiDARObjectID = "lo-1",
                        BasicLiDARObjectSpaceTime = new SpaceTime { SpaceTimeID = "st-lidar-1" } };
        var blsOut = await Run(new BlsAimProcessor("OSD-BLS-V1.5", AimPortReader.Load(store, "OSD-BLS-V1.5")),
                               "BasicLiDARObject", MpaiJson.ToJson(blsIn));

        fails += Check("OSD-BVS", bvsOut, "OSD-BVS-V1.5");
        fails += Check("OSD-BAS", basOut, "OSD-BAS-V1.5");
        fails += Check("OSD-BLS", blsOut, "OSD-BLS-V1.5");

        // --- alignment: feed the three scene descriptors to AVA ---
        var ava = new OsdAvaAimProcessor("OSD-AVA-V1.5", AimPortReader.Load(store, "OSD-AVA-V1.5"));
        var avaMsg = new Message { MessageId = "ava", MessageType = "test", Ports = new Dictionary<string,string>
        {
            ["AudioSceneDescriptors"]  = First(basOut),
            ["VisualSceneDescriptors"] = First(bvsOut),
            ["LiDARSceneDescriptors"]  = First(blsOut)
        }};
        var avaOut = await ava.ProcessAsync(avaMsg);

        Console.WriteLine("\n[OSD-AVA] output ports: [" + string.Join(", ", avaOut.Ports.Keys) + "]");
        int avaFails = 0;
        avaFails += Contains("OSD-AVA aligned visual", avaOut, "OSD-BVS-V1.5");
        avaFails += Contains("OSD-AVA aligned audio",  avaOut, "OSD-BAS-V1.5");
        avaFails += Contains("OSD-AVA visual geometry", avaOut, "OSD-BVG-V1.5");
        avaFails += Contains("OSD-AVA audio geometry",  avaOut, "OSD-BAG-V1.5");
        // the visual object's SpaceTime must now be the LiDAR one (PoV resolved from LiDAR)
        var visualJson = FindPort(avaOut, "OSD-BVS-V1.5");
        bool poVResolved = visualJson != null && visualJson.Contains("st-lidar-1");
        Console.WriteLine("[OSD-AVA] visual PoV resolved from LiDAR (st-lidar-1 present in aligned visual): " + poVResolved);
        if (!poVResolved) avaFails++;
        fails += avaFails;

        Console.WriteLine(fails == 0 ? "\nFRONT END OK (3 describers + AVA)" : $"\n{fails} FAILED");
        return fails;
    }

    private static async Task<Message> Run(IAimProcessor proc, string portName, string json)
        => await proc.ProcessAsync(new Message { MessageId = "t", MessageType = "test",
               Ports = new Dictionary<string,string> { [portName] = json } });

    private static string First(Message m) { foreach (var kv in m.Ports) return kv.Value; return ""; }
    private static string? FindPort(Message m, string headerNeedle)
    { foreach (var kv in m.Ports) if (kv.Value != null && kv.Value.Contains(headerNeedle)) return kv.Value; return null; }

    private static int Check(string name, Message m, string header)
    {
        foreach (var kv in m.Ports) if (kv.Value != null && kv.Value.Contains(header))
            { Console.WriteLine($"[{name}] OK"); return 0; }
        Console.WriteLine($"[{name}] FAIL"); return 1;
    }
    private static int Contains(string label, Message m, string header)
    {
        foreach (var kv in m.Ports) if (kv.Value != null && kv.Value.Contains(header))
            { Console.WriteLine($"  {label}: OK"); return 0; }
        Console.WriteLine($"  {label}: FAIL (no {header})"); return 1;
    }
}
