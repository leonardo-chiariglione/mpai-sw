using System;
using System.Collections.Generic;

using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Osd.BasicAudioVisualSceneDescription;

// ---------------------------------------------------------------------------
//  Basic Audio-Visual Scene Description AIM (AIMName OSD-BMD-V1.5).
//  Output data type: BasicAudioVisualSceneDescriptors (OSD-BMS-V1.5).
//
//  COMPOSES, does not ASSOCIATE. This AIM takes the per-modality Basic Scene
//  Descriptors that the modality describers produce - a BasicAudioSceneDescriptors
//  (BAS), a BasicVisualSceneDescriptors (BVS), and in principle any other BXS -
//  and composes them into one BasicAudioVisualSceneDescriptors (BMS): each BXS
//  becomes an entry carrying its own SpaceTime, all registered to the BMS's
//  common reference frame (BAVSDescriptorsSpaceTime). The result is the modality
//  scenes ALIGNED in a common frame.
//
//  It does NOT do object-level cross-modal association ("this face IS this
//  voice"). That is the job of OSD-AVA (Audio-Visual Alignment), a separate,
//  downstream AIM - the same clean split as describe (AVS) vs identify (FIR).
//  BMS composition here just places the scenes together, aligned; AVA later
//  links objects across them.
//
//  NOTE (schema): the AIM's port metadata
//  (OSD/V1.5/AIMs/BasicAudioVisualSceneDescription.json) currently declares a
//  stale "AudioVisualObject" (OSD-AVO) input, left over from the earlier
//  object-based BMS design. The real inputs are the BXSs (basic scenes). The
//  AIM port metadata should be updated to reflect BXS inputs.
// ---------------------------------------------------------------------------
public sealed class BasicAudioVisualSceneDescriptionAim
{
    // Compose an arbitrary set of Basic Scene Descriptors (each of any modality:
    // BAS, BVS, BLS, ...) into one BMS, aligned in the given common frame.
    //
    // Each scene is paired with its SpaceTime. If a scene's own SpaceTime is not
    // separately supplied, the common frame is used as its placement (the
    // honest default: "in the common frame" until a finer placement is known).
    public BasicAudioVisualSceneDescriptors Compose(
        IReadOnlyList<(object scene, SpaceTime? spaceTime)> scenes,
        SpaceTime? commonFrame = null,
        string? mInstanceID = null,
        string? uEnvironmentID = null)
    {
        if (scenes is null) throw new ArgumentNullException(nameof(scenes));

        var frame = commonFrame ?? new SpaceTime();

        var entries = new List<BasicAVSceneEntry>(scenes.Count);
        foreach (var (scene, st) in scenes)
        {
            if (scene is null) continue;
            entries.Add(new BasicAVSceneEntry
            {
                // Per-entry SpaceTime is what preserves spatial alignment. If the
                // caller gave one for this scene, use it; else place it in the
                // common frame.
                BXSSpaceTime = st ?? frame,
                BXSOrBXSID = scene   // any BXS, or a nested BMS (recursion), or an ID
            });
        }

        return new BasicAudioVisualSceneDescriptors
        {
            MInstanceID = mInstanceID ?? "",
            UEnvironmentID = uEnvironmentID,
            BasicAVSceneDescriptorsID = Guid.NewGuid().ToString(),
            BAVSDescriptorsSpaceTime = frame,
            AVObjectCount = entries.Count,   // number of composed BXS entries
            BasicAVSceneDescriptorsData = entries
        };
    }

    // Convenience for the common CAV case: compose an audio scene and a visual
    // scene (the two we produce today) into a BMS in a common frame.
    public BasicAudioVisualSceneDescriptors Compose(
        BasicAudioSceneDescriptors audioScene,
        BasicVisualSceneDescriptors visualScene,
        SpaceTime? commonFrame = null,
        string? mInstanceID = null,
        string? uEnvironmentID = null)
    {
        var list = new List<(object, SpaceTime?)>();
        if (audioScene is not null)  list.Add((audioScene, null));
        if (visualScene is not null) list.Add((visualScene, null));
        return Compose(list, commonFrame, mInstanceID, uEnvironmentID);
    }
}
