using System.Collections.Generic;

using Mpai.Core;

namespace MPAIApps.ASMApp;

// A single pending placement within a draft: an existing Asset (a BAO or an
// AUO) plus where/when it goes. Used identically whether the draft
// represents ONE object's own intrinsic position (Mode 1 - AUO editing:
// exactly one placement) or a scene being composed (Modes 2/3 - ASD editing
// and AED creation: any number of placements). Mode 3 needs no special
// representation at all - it is simply several placements that happen to
// share the same AssetId, each with its own SpaceTime describing where and
// when in the sequence.
public sealed class PendingPlacement
{
    public string AssetId { get; }
    public SpaceTime? SpaceTime { get; set; }

    public PendingPlacement(string assetId, SpaceTime? spaceTime = null)
    {
        AssetId = assetId;
        SpaceTime = spaceTime;
    }
}

// Everything the user has changed but not yet committed to the Repository -
// "nothing enters the Repository until the user says Save." Building,
// clearing, and re-arranging a draft never touches AssetRepository at all;
// only SceneDraftReplayer.Save*() does, and only once, explicitly.
public sealed class SceneDraft
{
    public List<PendingPlacement> Placements { get; } = new();

    // Only meaningful when this draft represents a single object being
    // edited (Mode 1) - a pending change to its own AcousticProfile.
    public AcousticProfile? PendingAcousticProfile { get; set; }

    public void AddPlacement(string assetId, SpaceTime? spaceTime = null)
    {
        Placements.Add(new PendingPlacement(assetId, spaceTime));
    }

    public bool IsEmpty => Placements.Count == 0 && PendingAcousticProfile is null;

    public void Clear()
    {
        Placements.Clear();
        PendingAcousticProfile = null;
    }
}