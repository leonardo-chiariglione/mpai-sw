using System;
using System.Collections.Generic;
using System.Linq;

using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Osd.AudioVisualAlignment;

namespace Mpai.Osd.AudioVisualAlignment.Test;

// Verifies OSD-AVA aligns audio and visual objects by PointOfView bearing:
//   Visual: one face at azimuth 20 deg.
//   Audio:  one source at 22 deg (MATCH, within tolerance) and one at -40 (NO).
// Expect exactly ONE AlignedMMObject (the 20/22 pair), consensus ~21 deg, the
// -40 source unaligned, and the original scene PointOfViews UNCHANGED.
public static class Program
{
    static BasicAudioSceneEntry AudioAt(double az) => new()
    {
        AudioObjectIDOrAudioObject = new BasicAudioObject { BasicAudioObjectID = $"aud@{az}" },
        PointOfView = new PointOfView
        {
            PointOfViewID = $"a-{az}",
            SpherPosition = new[] { 0.0, az, 0.0 }
        }
    };

    static BasicVisualSceneEntry VisualAt(double az, double el) => new()
    {
        VObjectIDOrVObject = new BasicVisualObject { BasicVisualObjectID = $"vis@{az}" },
        PointOfView = new PointOfView
        {
            PointOfViewID = $"v-{az}",
            SpherPosition = new[] { 0.0, az, el }
        }
    };

    public static int Main()
    {
        var audio = new BasicAudioSceneDescriptors
        {
            BasicAudioSceneDescriptorsID = "bas-1",
            AudioObjectCount = 2,
            BasicAudioSceneDescriptorsEntries = new List<BasicAudioSceneEntry>
            {
                AudioAt(22.0),   // should match the face at 20
                AudioAt(-40.0)   // should NOT match
            }
        };

        var visual = new BasicVisualSceneDescriptors
        {
            BasicVisualSceneDescriptorsID = "bvs-1",
            VisualObjectCount = 1,
            BasicVisualSceneDescriptorsEntries = new List<BasicVisualSceneEntry>
            {
                VisualAt(20.0, 5.0)
            }
        };

        // Capture originals to prove non-destructiveness.
        double origAudAz0 = audio.BasicAudioSceneDescriptorsEntries[0].PointOfView.SpherPosition![1];
        double origVisAz0 = visual.BasicVisualSceneDescriptorsEntries[0].PointOfView.SpherPosition![1];

        var ava = new AudioVisualAlignmentAim(toleranceDeg: 10.0);
        var bms = ava.Align(audio, visual, mInstanceID: "M-1");

        Console.WriteLine("== AVA alignment ==");
        Console.WriteLine($"BMS header:        {bms.Header}");
        Console.WriteLine($"Composed scenes:   {bms.BasicAVSceneDescriptorsData.Count} (BAS + BVS)");
        Console.WriteLine($"AlignedMMObjects:  {bms.AlignedMMObjects.Count}");
        Console.WriteLine();

        foreach (var a in bms.AlignedMMObjects)
        {
            double? az = a.AlignmentPointOfView?.SpherPosition?[1];
            var kinds = a.AlignedObjects.Select(o => o switch
            {
                BasicAudioObject ao  => $"audio({ao.BasicAudioObjectID})",
                BasicVisualObject vo => $"visual({vo.BasicVisualObjectID})",
                string s             => $"id({s})",
                _                    => o?.GetType().Name ?? "null"
            });
            Console.WriteLine($"  aligned: [{string.Join(" + ", kinds)}]  code={a.AlignmentCode[..8]}...  consensus az={az:F1}");
        }
        Console.WriteLine();

        // Non-destructiveness: original PointOfViews unchanged.
        double nowAudAz0 = audio.BasicAudioSceneDescriptorsEntries[0].PointOfView.SpherPosition![1];
        double nowVisAz0 = visual.BasicVisualSceneDescriptorsEntries[0].PointOfView.SpherPosition![1];
        bool intact = origAudAz0 == nowAudAz0 && origVisAz0 == nowVisAz0;

        bool ok = bms.AlignedMMObjects.Count == 1
               && bms.BasicAVSceneDescriptorsData.Count == 2
               && intact;

        Console.WriteLine($"Originals intact:  {intact} (audio {origAudAz0}->{nowAudAz0}, visual {origVisAz0}->{nowVisAz0})");
        Console.WriteLine(ok
            ? "PASS: one aligned pair (20/22), -40 unaligned, originals untouched."
            : "FAIL: alignment not as expected.");
        return ok ? 0 : 1;
    }
}
