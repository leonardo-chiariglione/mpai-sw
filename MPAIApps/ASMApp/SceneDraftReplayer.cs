using System;
using System.Collections.Generic;
using System.Linq;

using Mpai.Cae.Aoe;
using Mpai.Cae.Ase;
using Mpai.Repository;

namespace MPAIApps.ASMApp;

// Commits a SceneDraft to the Repository, in one explicit action - this is
// what "Save" actually does. AoeAim/AseAim did not need to change to
// support drafting: they already only write when called. This class is
// simply choosing to call them once, here, on Save, rather than on every
// individual UI action - the draft accumulates in memory until then.
public static class SceneDraftReplayer
{
    // Commits a Mode-1 draft: edits ONE existing AudioObject's own
    // properties (its intrinsic position, and/or its AcousticProfile).
    // Expects the draft to contain at most one placement, for that same
    // object.
    public static RepositoryAsset SaveObjectEdit(AoeAim aoe, string audioObjectAssetId, SceneDraft draft)
    {
        var placement = draft.Placements.FirstOrDefault(p => p.AssetId == audioObjectAssetId)?.SpaceTime;
        return aoe.EditObjectProperties(audioObjectAssetId, acousticProfile: draft.PendingAcousticProfile, placement: placement);
    }

    // Commits a Modes-2/3 draft: composes a scene from every pending
    // placement, in the order they were added. If sceneAssetId is null, the
    // first placement creates a new scene and every subsequent one is added
    // to it; if sceneAssetId names an existing scene, every placement in the
    // draft is added to it. Several placements may share the same AssetId
    // (a Mode-3 Event, an object visited at a sequence of positions/times) -
    // AddObjectToScene already supports that with no special handling
    // needed here.
    public static RepositoryAsset SaveSceneComposition(AseAim ase, string? sceneAssetId, SceneDraft draft)
    {
        if (draft.Placements.Count == 0)
            throw new InvalidOperationException("Nothing to save - the draft has no placements.");

        string currentSceneId;
        IEnumerable<PendingPlacement> remaining;
        RepositoryAsset current;

        if (sceneAssetId is null)
        {
            var first = draft.Placements[0];
            current = ase.CreateScene(first.AssetId, first.SpaceTime);
            currentSceneId = current.AssetId;
            remaining = draft.Placements.Skip(1);
        }
        else
        {
            currentSceneId = sceneAssetId;
            remaining = draft.Placements;
            current = null!;   // always assigned below: draft.Placements is non-empty (checked above), and in this branch remaining == draft.Placements, so the loop runs at least once.
        }

        foreach (var placement in remaining)
        {
            current = ase.AddObjectToScene(currentSceneId, placement.AssetId, placement.SpaceTime);
            currentSceneId = current.AssetId;
        }

        return current;
    }
}