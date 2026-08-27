using System;
using System.Collections.Generic;

using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Osd.BasicAudioVisualSceneDescription;

namespace Mpai.Osd.AudioVisualAlignment;

// ---------------------------------------------------------------------------
//  Audio-Visual Alignment AIM (OSD-AVA-V1.5).
//
//  Aligns the objects of the separately-computed Basic scenes that share the
//  same Spatial Attitude, marking them as the SAME entity. Input: BAS + BVS
//  (extensible to BSS/B3S). Output: a BasicAudioVisualSceneDescriptors (BMS).
//
//  Method: the scene is DYNAMIC, so alignment is by PointOfView (each object's
//  current bearing/DOA), not by any fixed identity. For each audio object (BAS
//  entry, PointOfView = DOA) and visual object (BVS entry, PointOfView = face
//  bearing), compare bearings; if within an angular tolerance, they are the same
//  entity.
//
//  NON-DESTRUCTIVE: the original BAS/BVS objects and their PointOfViews are left
//  exactly as the scene creation produced them (they are measurements; AVA's
//  match is an interpretation and must not overwrite them). AVA instead ADDS an
//  array of AlignedMMObjects to the BMS - each a combination of the matched
//  constituent objects, tied by a shared AlignmentCode, with a consensus
//  PointOfView. Both the raw scenes and AVA's interpretation are thus visible.
//
//  This is identity-free: whether the aligned entity is "person X" is FIR/SIR's
//  job (Instance Identifier), downstream. AVA only says "this audio object and
//  this visual object are the same thing, here".
// ---------------------------------------------------------------------------
public sealed class AudioVisualAlignmentAim
{
    private readonly double _toleranceDeg;

    public AudioVisualAlignmentAim(double toleranceDeg = 10.0)
        => _toleranceDeg = toleranceDeg;

    public BasicAudioVisualSceneDescriptors Align(
        BasicAudioSceneDescriptors audioScene,
        BasicVisualSceneDescriptors visualScene,
        SpaceTime? commonFrame = null,
        string? mInstanceID = null,
        string? uEnvironmentID = null)
    {
        var frame = commonFrame ?? new SpaceTime();

        // 1. Compose the BMS (the degenerate composition), scenes unchanged.
        var composer = new BasicAudioVisualSceneDescriptionAim();
        var bms = composer.Compose(audioScene, visualScene, frame, mInstanceID, uEnvironmentID);

        // 2. Match audio objects to visual objects by PointOfView bearing.
        var aligned = new List<AlignedMMObject>();
        if (audioScene is not null && visualScene is not null)
        {
            foreach (var aEntry in audioScene.BasicAudioSceneDescriptorsEntries)
            {
                foreach (var vEntry in visualScene.BasicVisualSceneDescriptorsEntries)
                {
                    if (BearingsMatch(aEntry.PointOfView, vEntry.PointOfView, _toleranceDeg))
                    {
                        aligned.Add(new AlignedMMObject
                        {
                            AlignmentCode = Guid.NewGuid().ToString(),
                            // Consensus attitude: midpoint bearing of the pair.
                            AlignmentPointOfView = ConsensusPov(aEntry.PointOfView, vEntry.PointOfView),
                            AlignedObjects = new List<object>
                            {
                                // The constituent objects (or their ids). Leave the
                                // originals in the scenes untouched; reference them here.
                                aEntry.AudioObjectIDOrAudioObject!,
                                vEntry.VObjectIDOrVObject!
                            }
                        });
                    }
                }
            }
        }

        // 3. Return the BMS with the alignment layer added (originals intact).
        return new BasicAudioVisualSceneDescriptors
        {
            Header = bms.Header,
            MInstanceID = bms.MInstanceID,
            UEnvironmentID = bms.UEnvironmentID,
            BasicAVSceneDescriptorsID = bms.BasicAVSceneDescriptorsID,
            BAVSDescriptorsTime = bms.BAVSDescriptorsTime,
            BAVSDescriptorsSpaceTime = bms.BAVSDescriptorsSpaceTime,
            GravityValue = bms.GravityValue,
            AVObjectCount = bms.AVObjectCount,
            BasicAVSceneDescriptorsData = bms.BasicAVSceneDescriptorsData,
            AlignedMMObjects = aligned,
            DataXMData = bms.DataXMData,
            DescrMetadata = bms.DescrMetadata
        };
    }

    // ---- bearing helpers ----------------------------------------------------
    // Bearing lives in PointOfView.SpherPosition = [r, phi(az), theta(el)] deg,
    // as the audio/visual pipelines populate it.
    private static bool BearingsMatch(PointOfView? a, PointOfView? b, double tolDeg)
    {
        if (a?.SpherPosition is null || b?.SpherPosition is null) return false;
        if (a.SpherPosition.Length < 3 || b.SpherPosition.Length < 3) return false;

        double dAz = AngleDiff(a.SpherPosition[1], b.SpherPosition[1]);
        double dEl = AngleDiff(a.SpherPosition[2], b.SpherPosition[2]);
        // Audio DOA is often azimuth-only (elevation may be 0/unknown); weight
        // azimuth as the primary cue, allow elevation a wider margin.
        return Math.Abs(dAz) <= tolDeg && Math.Abs(dEl) <= tolDeg * 2.0;
    }

    private static PointOfView ConsensusPov(PointOfView a, PointOfView b)
    {
        double az = (a.SpherPosition![1] + b.SpherPosition![1]) / 2.0;
        double el = (a.SpherPosition![2] + b.SpherPosition![2]) / 2.0;
        return new PointOfView
        {
            PointOfViewID = Guid.NewGuid().ToString(),
            SpherPosition = new[] { 0.0, az, el }
        };
    }

    private static double AngleDiff(double x, double y)
    {
        double d = (x - y) % 360.0;
        if (d > 180.0) d -= 360.0;
        if (d < -180.0) d += 360.0;
        return d;
    }
}
