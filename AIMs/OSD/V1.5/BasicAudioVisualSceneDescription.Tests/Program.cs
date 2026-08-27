using System;
using System.Collections.Generic;

using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Osd.BasicAudioVisualSceneDescription;

namespace Mpai.Osd.BasicAudioVisualSceneDescription.Test;

// Verifies the BMD AIM composes a BAS + a BVS into a well-formed BMS:
// two entries, each carrying its scene and a SpaceTime, common frame set,
// header OSD-BMS-V1.5, count correct. Pure logic - no model.
public static class Program
{
    public static int Main()
    {
        // --- Build a small audio scene (BAS) with 2 audio objects. ----------
        var bas = new BasicAudioSceneDescriptors
        {
            BasicAudioSceneDescriptorsID = "bas-1",
            AudioObjectCount = 2,
            BasicAudioSceneDescriptorsEntries = new List<BasicAudioSceneEntry>
            {
                new() { PointOfView = new PointOfView { PointOfViewID = "a0" } },
                new() { PointOfView = new PointOfView { PointOfViewID = "a1" } }
            }
        };

        // --- Build a small visual scene (BVS) with 1 face. ------------------
        var bvs = new BasicVisualSceneDescriptors
        {
            BasicVisualSceneDescriptorsID = "bvs-1",
            VisualObjectCount = 1,
            BasicVisualSceneDescriptorsEntries = new List<BasicVisualSceneEntry>
            {
                new() { PointOfView = new PointOfView { PointOfViewID = "v0" } }
            }
        };

        // --- Compose into a BMS. --------------------------------------------
        var aim = new BasicAudioVisualSceneDescriptionAim();
        var commonFrame = new SpaceTime();
        BasicAudioVisualSceneDescriptors bms =
            aim.Compose(bas, bvs, commonFrame, mInstanceID: "M-Instance-1");

        // --- Report. ---------------------------------------------------------
        Console.WriteLine("== BMS composition ==");
        Console.WriteLine($"Header:            {bms.Header}");
        Console.WriteLine($"MInstanceID:       {bms.MInstanceID}");
        Console.WriteLine($"BMS ID:            {bms.BasicAVSceneDescriptorsID}");
        Console.WriteLine($"Common frame set:  {bms.BAVSDescriptorsSpaceTime is not null}");
        Console.WriteLine($"AVObjectCount:     {bms.AVObjectCount}");
        Console.WriteLine($"Entries:           {bms.BasicAVSceneDescriptorsData.Count}");
        Console.WriteLine();

        int i = 0;
        foreach (var entry in bms.BasicAVSceneDescriptorsData)
        {
            string kind = entry.BXSOrBXSID switch
            {
                BasicAudioSceneDescriptors a  => $"BAS (id={a.BasicAudioSceneDescriptorsID}, {a.AudioObjectCount} audio obj)",
                BasicVisualSceneDescriptors v => $"BVS (id={v.BasicVisualSceneDescriptorsID}, {v.VisualObjectCount} visual obj)",
                BasicAudioVisualSceneDescriptors => "BMS (nested)",
                string s                        => $"ID ref: {s}",
                null                            => "(null)",
                _                               => entry.BXSOrBXSID.GetType().Name
            };
            bool aligned = entry.BXSSpaceTime is not null;
            Console.WriteLine($"  entry[{i++}]: {kind}  | SpaceTime present: {aligned}");
        }

        Console.WriteLine();
        bool ok = bms.Header == "OSD-BMS-V1.5"
               && bms.AVObjectCount == 2
               && bms.BasicAVSceneDescriptorsData.Count == 2
               && bms.BAVSDescriptorsSpaceTime is not null;
        Console.WriteLine(ok
            ? "PASS: BMS composes BAS + BVS, aligned, header/count correct."
            : "FAIL: BMS composition not as expected.");
        return ok ? 0 : 1;
    }
}
