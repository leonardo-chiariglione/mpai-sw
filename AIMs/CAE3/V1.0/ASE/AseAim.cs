using System;
using System.Collections.Generic;
using System.Linq;

using Mpai.Cae.Aoe;
using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Repository;

namespace Mpai.Cae.Ase;

// ---------------------------------------------------------------------------
//  CAE-ASE-V1.0 - Audio Scene Editing.
//
//  Two real, separate paths, not one path pretending to cover both:
//
//   - CreateScene/AddObjectToScene/Materialize build AudioSceneDescriptors
//     (ASD - the full/hierarchical scene type), composing AudioObjects.
//   - CreateBasicScene/AddObjectToBasicScene/MaterializeBasicScene build
//     BasicAudioSceneDescriptors (BAS), composing BasicAudioObjects
//     directly. Use this path when what's being placed genuinely IS just a
//     single sound, not an artificially-wrapped AudioObject standing in for
//     one - "using ASD for everything, even a single basic object, is the
//     degenerate notion" (direct correction received on this).
//
//  BasicAudioSceneDescriptors requires its own ListenerPointOfView at
//  creation time (a real schema field ASD doesn't even have) and each
//  entry requires its own PointOfView too - both are schema-required, not
//  optional, unlike ASD's listener (which is only ever a delivery-time
//  parameter, per ASD's own schema having no such field at all).
//
//  What's actually WRITTEN to the Repository is the standard schema, at
//  rest: composition arrays are real, populated arrays in the stored
//  Payload, each entry an "ID stub" (only AssetId set, resolved fully by
//  Materialize()) with a genuine embedded SpaceTime carrying the
//  placement's real SpatialAttitude/time when one is given - not
//  reconstructed from a side channel only when read back. Repository
//  References are still created alongside, for the generic graph
//  operations (cycle prevention, GetReferrers, dependency tracking); they
//  are not the source of truth for what a scene contains.
// ---------------------------------------------------------------------------
public sealed class AseAim
{
    private readonly AssetRepository repository;
    private readonly AoeAim aoe;

    // Depends on AoeAim directly to reuse its recursive AudioObject
    // materialization rather than duplicating that walk here - both AIMs
    // are plain C# classes over the same Repository, so this is the same
    // "call directly into the live instance" model ASMApp itself will use.
    public AseAim(AssetRepository repository, AoeAim aoe)
    {
        this.repository = repository;
        this.aoe = aoe;
    }

    // Wraps a single AudioObject into a new, minimal scene (the degenerate
    // case: one placement, no sub-scenes). placement is optional - null
    // means "no position/time recorded for this placement." listenerPointOfView
    // is optional too - the scene can be created before the listener is placed.
    public RepositoryAsset CreateScene(string audioObjectAssetId, SpaceTime? placement = null, PointOfView? listenerPointOfView = null)
    {
        var objectAsset = repository.GetAsset(audioObjectAssetId)
            ?? throw new InvalidOperationException($"{audioObjectAssetId} does not exist.");

        if (objectAsset.AssetType != AssetType.AUO)
            throw new InvalidOperationException($"{audioObjectAssetId} is not an AudioObject (it's {objectAsset.AssetType}).");

        var sceneAsset = new RepositoryAsset { AssetId = repository.GenerateAssetId(AssetType.ASD), AssetType = AssetType.ASD };
        sceneAsset.SetData(new AudioSceneDescriptors
        {
            AudioSceneDescriptorsID = sceneAsset.AssetId,
            ListenerPointOfView = listenerPointOfView,
            AudioObjectCount = 1,
            AudioObjects = new() { MakeEntry(audioObjectAssetId, placement) }
        });
        repository.CreateAsset(sceneAsset);

        repository.CreateReference(sceneAsset.AssetId, audioObjectAssetId);

        return sceneAsset;
    }

    // Adds an existing AudioObject as a further placement in an existing
    // scene. Produces a NEW version of the scene (no-cascade Save rule: the
    // object being added is untouched). placement is optional - null means
    // "no position/time recorded for this placement." listenerPointOfView,
    // if given, updates the scene's listener at the same time; if null, the
    // existing listener (if any) carries forward unchanged.
    public RepositoryAsset AddObjectToScene(string sceneAssetId, string audioObjectAssetId, SpaceTime? placement = null, PointOfView? listenerPointOfView = null)
    {
        var existing = repository.GetAsset(sceneAssetId)
            ?? throw new InvalidOperationException($"{sceneAssetId} does not exist.");

        if (existing.AssetType != AssetType.ASD)
            throw new InvalidOperationException($"{sceneAssetId} is not a Scene (it's {existing.AssetType}).");

        var obj = repository.GetAsset(audioObjectAssetId)
            ?? throw new InvalidOperationException($"{audioObjectAssetId} does not exist.");

        if (obj.AssetType != AssetType.AUO)
            throw new InvalidOperationException($"{audioObjectAssetId} is not an AudioObject (it's {obj.AssetType}).");

        var existingData = existing.GetData<AudioSceneDescriptors>()
            ?? throw new InvalidOperationException($"{sceneAssetId} has no stored scene data.");

        var saved = repository.SaveAsset(existing);
        repository.CreateReference(saved.AssetId, audioObjectAssetId);

        var entries = (existingData.AudioObjects ?? new List<AudioSceneObjectEntry>()).ToList();
        entries.Add(MakeEntry(audioObjectAssetId, placement));

        saved.SetData(new AudioSceneDescriptors
        {
            AudioSceneDescriptorsID = saved.AssetId,
            AudioSceneDescriptorsTime = existingData.AudioSceneDescriptorsTime,
            AudioSceneDescriptorsSpaceTime = existingData.AudioSceneDescriptorsSpaceTime,
            ListenerPointOfView = listenerPointOfView ?? existingData.ListenerPointOfView,
            AudioObjectCount = entries.Count,
            AudioObjects = entries,
            SubAudioSceneCount = existingData.SubAudioSceneCount,
            SubAudioScenes = existingData.SubAudioScenes
        });
        repository.UpdateAsset(saved);

        return saved;
    }

    // Updates just the scene's listener, without adding or moving any
    // object placement - what "drag the listener marker on the canvas"
    // ultimately calls. Produces a new version, same no-cascade rule as
    // everything else.
    public RepositoryAsset SetSceneListener(string sceneAssetId, PointOfView listenerPointOfView)
    {
        var existing = repository.GetAsset(sceneAssetId)
            ?? throw new InvalidOperationException($"{sceneAssetId} does not exist.");

        if (existing.AssetType != AssetType.ASD)
            throw new InvalidOperationException($"{sceneAssetId} is not a Scene (it's {existing.AssetType}).");

        var existingData = existing.GetData<AudioSceneDescriptors>()
            ?? throw new InvalidOperationException($"{sceneAssetId} has no stored scene data.");

        var saved = repository.SaveAsset(existing);
        saved.SetData(new AudioSceneDescriptors
        {
            AudioSceneDescriptorsID = saved.AssetId,
            AudioSceneDescriptorsTime = existingData.AudioSceneDescriptorsTime,
            AudioSceneDescriptorsSpaceTime = existingData.AudioSceneDescriptorsSpaceTime,
            ListenerPointOfView = listenerPointOfView,
            AudioObjectCount = existingData.AudioObjectCount,
            AudioObjects = existingData.AudioObjects,
            SubAudioSceneCount = existingData.SubAudioSceneCount,
            SubAudioScenes = existingData.SubAudioScenes
        });
        repository.UpdateAsset(saved);

        return saved;
    }

    private static AudioSceneObjectEntry MakeEntry(string audioObjectAssetId, SpaceTime? placement) => new()
    {
        // ID stub - only the identifying field set. A minimal but genuinely
        // valid instance of the "object" branch of the schema's "object or
        // id-string" oneOf; Materialize() resolves it into full content.
        ObjectIDOrObject = new AudioObject { AudioObjectID = audioObjectAssetId },
        AudioObjectSpaceTime = placement
    };

    // Resolves a stored scene into a fully self-contained structure: its own
    // fields, with every ID-stub AudioObject entry replaced by its full,
    // live content (via AoeAim.Materialize), and each entry's real
    // AudioObjectSpaceTime carried across unchanged. The stored Payload
    // itself is already schema-valid before this resolution happens.
    public AudioSceneDescriptors Materialize(string sceneAssetId)
    {
        var asset = repository.GetAsset(sceneAssetId)
            ?? throw new InvalidOperationException($"{sceneAssetId} does not exist.");

        if (asset.AssetType != AssetType.ASD)
            throw new InvalidOperationException($"{sceneAssetId} is not a Scene (it's {asset.AssetType}).");

        var scene = asset.GetData<AudioSceneDescriptors>()
            ?? throw new InvalidOperationException($"{sceneAssetId} has no stored scene data.");

        var objectEntries = new List<AudioSceneObjectEntry>();

        foreach (var entry in scene.AudioObjects ?? new List<AudioSceneObjectEntry>())
        {
            var childId = entry.ObjectIDOrObject?.AudioObjectID;
            if (string.IsNullOrEmpty(childId)) continue;

            var childAsset = repository.GetAsset(childId);
            if (childAsset is null || childAsset.AssetType != AssetType.AUO) continue;

            objectEntries.Add(new AudioSceneObjectEntry
            {
                ObjectIDOrObject = aoe.Materialize(childId),
                AudioObjectSpaceTime = entry.AudioObjectSpaceTime
            });
        }

        return new AudioSceneDescriptors
        {
            // Same principle as AoeAim.Materialize: the current AssetId is
            // always authoritative for identity, not whatever ID happens to
            // be stored in the payload (which can go stale after a Save).
            AudioSceneDescriptorsID = asset.AssetId,
            AudioSceneDescriptorsTime = scene.AudioSceneDescriptorsTime,
            AudioSceneDescriptorsSpaceTime = scene.AudioSceneDescriptorsSpaceTime,
            ListenerPointOfView = scene.ListenerPointOfView,
            AudioObjectCount = objectEntries.Count,
            AudioObjects = objectEntries.Count > 0 ? objectEntries : null
        };
    }

    // -----------------------------------------------------------------
    //  BasicAudioSceneDescriptors (BAS) path - a genuinely Basic scene,
    //  composing BasicAudioObjects directly, not AudioObjects.
    // -----------------------------------------------------------------

    // Wraps a single BasicAudioObject into a new, minimal Basic scene.
    // listenerPointOfView is required - BasicAudioSceneDescriptors.json
    // lists it as a required field, unlike ASD which has no such field at
    // all (there, the listener is only ever a delivery-time parameter).
    public RepositoryAsset CreateBasicScene(string basicAudioObjectAssetId, PointOfView listenerPointOfView, SpaceTime? placement = null)
    {
        var baoAsset = repository.GetAsset(basicAudioObjectAssetId)
            ?? throw new InvalidOperationException($"{basicAudioObjectAssetId} does not exist.");

        if (baoAsset.AssetType != AssetType.BAO)
            throw new InvalidOperationException($"{basicAudioObjectAssetId} is not a BasicAudioObject (it's {baoAsset.AssetType}).");

        var sceneAsset = new RepositoryAsset { AssetId = repository.GenerateAssetId(AssetType.BAS), AssetType = AssetType.BAS };
        sceneAsset.SetData(new BasicAudioSceneDescriptors
        {
            BasicAudioSceneDescriptorsID = sceneAsset.AssetId,
            ListenerPointOfView = listenerPointOfView,
            AudioObjectCount = 1,
            BasicAudioSceneDescriptorsEntries = new() { MakeBasicEntry(basicAudioObjectAssetId, placement) }
        });
        repository.CreateAsset(sceneAsset);

        repository.CreateReference(sceneAsset.AssetId, basicAudioObjectAssetId);

        return sceneAsset;
    }

    // Adds an existing BasicAudioObject as a further placement in an
    // existing Basic scene. Produces a NEW version (no-cascade Save rule).
    public RepositoryAsset AddObjectToBasicScene(string sceneAssetId, string basicAudioObjectAssetId, SpaceTime? placement = null)
    {
        var existing = repository.GetAsset(sceneAssetId)
            ?? throw new InvalidOperationException($"{sceneAssetId} does not exist.");

        if (existing.AssetType != AssetType.BAS)
            throw new InvalidOperationException($"{sceneAssetId} is not a Basic Scene (it's {existing.AssetType}).");

        var bao = repository.GetAsset(basicAudioObjectAssetId)
            ?? throw new InvalidOperationException($"{basicAudioObjectAssetId} does not exist.");

        if (bao.AssetType != AssetType.BAO)
            throw new InvalidOperationException($"{basicAudioObjectAssetId} is not a BasicAudioObject (it's {bao.AssetType}).");

        var existingData = existing.GetData<BasicAudioSceneDescriptors>()
            ?? throw new InvalidOperationException($"{sceneAssetId} has no stored scene data.");

        var saved = repository.SaveAsset(existing);
        repository.CreateReference(saved.AssetId, basicAudioObjectAssetId);

        var entries = existingData.BasicAudioSceneDescriptorsEntries.ToList();
        entries.Add(MakeBasicEntry(basicAudioObjectAssetId, placement));

        saved.SetData(new BasicAudioSceneDescriptors
        {
            BasicAudioSceneDescriptorsID = saved.AssetId,
            BasicAudioSceneDescriptorsTime = existingData.BasicAudioSceneDescriptorsTime,
            BASSpaceTime = existingData.BASSpaceTime,
            ListenerPointOfView = existingData.ListenerPointOfView,
            GravityValue = existingData.GravityValue,
            AudioObjectCount = entries.Count,
            BasicAudioSceneDescriptorsEntries = entries
        });
        repository.UpdateAsset(saved);

        return saved;
    }

    private static BasicAudioSceneEntry MakeBasicEntry(string basicAudioObjectAssetId, SpaceTime? placement)
    {
        // The entry's own PointOfView is separately required by the schema
        // (distinct from the scene-level ListenerPointOfView) but the
        // schema doesn't clarify how it differs in intended use from the
        // placement's own SpatialAttitude - reusing the same position here
        // as a pragmatic default rather than inventing a second, unrelated
        // meaning for it.
        var position = placement?.SpatialAttitude1?.Position?.CartPosition ?? new double[] { 0, 0, 0 };
        var orientation = placement?.SpatialAttitude1?.Orientation?.EulerAngles ?? new double[] { 0, 0, 0 };

        return new BasicAudioSceneEntry
        {
            // ID stub - only the identifying field set. A minimal but
            // genuinely valid instance of the "object" branch of the
            // schema's "object or id-string" oneOf; MaterializeBasicScene()
            // resolves it into full content.
            AudioObjectIDOrAudioObject = new BasicAudioObject { BasicAudioObjectID = basicAudioObjectAssetId },
            AudioObjectSpaceTime = placement,
            PointOfView = new PointOfView
            {
                PointOfViewID = Guid.NewGuid().ToString(),
                CartPosition = position,
                Orientation = orientation
            }
        };
    }

    // Resolves a stored Basic scene into a fully self-contained structure:
    // its own fields, with every ID-stub BasicAudioObject entry replaced by
    // its full, live content. The stored Payload itself is already
    // schema-valid before this resolution happens.
    public BasicAudioSceneDescriptors MaterializeBasicScene(string sceneAssetId)
    {
        var asset = repository.GetAsset(sceneAssetId)
            ?? throw new InvalidOperationException($"{sceneAssetId} does not exist.");

        if (asset.AssetType != AssetType.BAS)
            throw new InvalidOperationException($"{sceneAssetId} is not a Basic Scene (it's {asset.AssetType}).");

        var scene = asset.GetData<BasicAudioSceneDescriptors>()
            ?? throw new InvalidOperationException($"{sceneAssetId} has no stored scene data.");

        var resolvedEntries = new List<BasicAudioSceneEntry>();

        foreach (var entry in scene.BasicAudioSceneDescriptorsEntries)
        {
            var childId = entry.AudioObjectIDOrAudioObject?.BasicAudioObjectID;
            if (string.IsNullOrEmpty(childId)) continue;

            var childAsset = repository.GetAsset(childId);
            var bao = childAsset?.GetData<BasicAudioObject>();
            if (bao is null) continue;

            resolvedEntries.Add(new BasicAudioSceneEntry
            {
                AudioObjectIDOrAudioObject = bao,
                AudioObjectSpaceTime = entry.AudioObjectSpaceTime,
                AudioSceneEnrichment = entry.AudioSceneEnrichment,
                PointOfView = entry.PointOfView
            });
        }

        return new BasicAudioSceneDescriptors
        {
            // Same principle as AoeAim.Materialize: the current AssetId is
            // always authoritative for identity, not whatever ID happens to
            // be stored in the payload (which can go stale after a Save).
            BasicAudioSceneDescriptorsID = asset.AssetId,
            BasicAudioSceneDescriptorsTime = scene.BasicAudioSceneDescriptorsTime,
            BASSpaceTime = scene.BASSpaceTime,
            ListenerPointOfView = scene.ListenerPointOfView,
            GravityValue = scene.GravityValue,
            AudioObjectCount = resolvedEntries.Count,
            BasicAudioSceneDescriptorsEntries = resolvedEntries
        };
    }
}