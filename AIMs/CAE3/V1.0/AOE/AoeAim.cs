using System;
using System.Collections.Generic;
using System.Linq;

using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Repository;

namespace Mpai.Cae.Aoe;

// ---------------------------------------------------------------------------
//  CAE-AOE-V1.0 - Audio Object Editing.
//
//  Creates and composes AudioObjects in the Repository. Per the spec's
//  Editing Principle (Retrieve -> Edit -> Save -> New Repository Object),
//  every edit here produces a NEW Repository Asset via SaveAsset - it never
//  mutates an existing one. A BasicAudioObject is the degenerate case of an
//  AudioObject with no sub-objects, not a separate type to special-case
//  ("we intend to also say BAO because a BAO is a degenerate AUO").
//
//  What's actually WRITTEN to the Repository is the standard schema, at
//  rest, not something reconstructed only when read back: BasicAudioObjects/
//  SubAudioObjects are real, populated arrays in the stored Payload, each
//  entry an "ID stub" (only AssetId set) per the schema's own "object or
//  id-string" oneOf. Repository References are still created alongside, for
//  the Repository-level graph operations (cycle prevention, GetReferrers,
//  dependency tracking) that are generic infrastructure, not schema content
//  - but they are not the source of truth for what a saved AudioObject
//  contains; the schema-shaped Payload is. Materialize() resolves each ID
//  stub into its full content for an external consumer (ASD delivery,
//  export); the stored Payload itself is already valid against the schema
//  even before that resolution happens.
// ---------------------------------------------------------------------------
public sealed class AoeAim
{
    private readonly AssetRepository repository;

    public AoeAim(AssetRepository repository) => this.repository = repository;

    // Wraps a single BasicAudioObject into a new, minimal AudioObject Asset
    // (the degenerate case: one leaf, no sub-objects). The BAO is stored as
    // its own addressable Asset too - BAOs are static and independently
    // referenceable, per the Repository rules already established.
    public RepositoryAsset CreateObject(BasicAudioObject bao)
    {
        var baoAsset = new RepositoryAsset { AssetId = repository.GenerateAssetId(AssetType.BAO), AssetType = AssetType.BAO };
        baoAsset.SetData(bao.WithId(baoAsset.AssetId));
        repository.CreateAsset(baoAsset);

        var audioObjectAsset = new RepositoryAsset { AssetId = repository.GenerateAssetId(AssetType.AUO), AssetType = AssetType.AUO };
        audioObjectAsset.SetData(new AudioObject
        {
            AudioObjectID = audioObjectAsset.AssetId,
            BasicAudioObjectCount = 1,
            BasicAudioObjects = new() { new BasicAudioObjectEntry { BAObjectIDOrBAObject = IdStubBao(baoAsset.AssetId) } }
        });
        repository.CreateAsset(audioObjectAsset);

        repository.CreateReference(audioObjectAsset.AssetId, baoAsset.AssetId);

        return audioObjectAsset;
    }

    // Adds an existing Asset (a BAO or another AudioObject) as a child of an
    // existing AudioObject. Produces a NEW version of the AudioObject (per
    // the no-cascade Save rule: only the edited instance itself gets a new
    // ID; the child being added is untouched). The child must already exist
    // in the Repository - enforced by CreateReference's cycle-prevention
    // check, which also rules out accidentally creating a cyclic structure.
    public RepositoryAsset AddSubObject(string audioObjectAssetId, string childAssetId)
    {
        var existing = repository.GetAsset(audioObjectAssetId)
            ?? throw new InvalidOperationException($"{audioObjectAssetId} does not exist.");

        if (existing.AssetType != AssetType.AUO)
            throw new InvalidOperationException($"{audioObjectAssetId} is not an AudioObject (it's {existing.AssetType}).");

        var child = repository.GetAsset(childAssetId)
            ?? throw new InvalidOperationException($"{childAssetId} does not exist.");

        if (child.AssetType != AssetType.BAO && child.AssetType != AssetType.AUO)
            throw new InvalidOperationException($"{childAssetId} is not a BAO or AudioObject (it's {child.AssetType}).");

        var existingData = existing.GetData<AudioObject>()
            ?? throw new InvalidOperationException($"{audioObjectAssetId} has no stored AudioObject data.");

        // SaveAsset already carries forward existing References; the new
        // version's schema-shaped Payload is built explicitly here, adding
        // one entry to whichever array the child's type belongs in.
        var saved = repository.SaveAsset(existing);
        repository.CreateReference(saved.AssetId, childAssetId);

        var basicEntries = (existingData.BasicAudioObjects ?? new List<BasicAudioObjectEntry>()).ToList();
        var subEntries = (existingData.SubAudioObjects ?? new List<SubAudioObjectEntry>()).ToList();

        if (child.AssetType == AssetType.BAO)
        {
            basicEntries.Add(new BasicAudioObjectEntry { BAObjectIDOrBAObject = IdStubBao(childAssetId) });
        }
        else
        {
            subEntries.Add(new SubAudioObjectEntry { SubAObjectIDOrSubAObject = IdStubAuo(childAssetId) });
        }

        saved.SetData(new AudioObject
        {
            AudioObjectID = saved.AssetId,
            AudioObjectTime = existingData.AudioObjectTime,
            AudioObjectSpaceTime = existingData.AudioObjectSpaceTime,
            AudioObjectProperties = existingData.AudioObjectProperties,
            ParentAudioObjectIDs = existingData.ParentAudioObjectIDs,
            BasicAudioObjectCount = basicEntries.Count,
            BasicAudioObjects = basicEntries.Count > 0 ? basicEntries : null,
            SubAudioObjectCount = subEntries.Count,
            SubAudioObjects = subEntries.Count > 0 ? subEntries : null
        });
        repository.UpdateAsset(saved);

        return saved;
    }

    // ID-stub entries: only the identifying field set, nothing else - a
    // minimal but genuinely valid instance of the "object" branch of the
    // schema's "object or id-string" oneOf. Materialize() resolves these
    // into full content; the stub itself is what's actually persisted.
    private static BasicAudioObject IdStubBao(string assetId) => new() { BasicAudioObjectID = assetId };
    private static AudioObject IdStubAuo(string assetId) => new() { AudioObjectID = assetId };

    // Produces a new version of a BasicAudioObject with updated properties
    // (Level, PerceptStatus, AcousticProfile, BasicAudioObjectIdentifier).
    // Unspecified (null) parameters keep their existing value - this edits,
    // it does not replace. The audio DATA itself is never touched here,
    // consistent with "BAOs are static" - only the descriptive properties
    // that sit alongside the data change, and that change is itself a new,
    // immutable version, same as everywhere else in this Repository.
    public RepositoryAsset EditBasicObjectProperties(
        string basicAudioObjectAssetId,
        double? level = null,
        bool? perceptStatus = null,
        AcousticProfile? acousticProfile = null,
        InstanceIdentifier? identifier = null)
    {
        var existing = repository.GetAsset(basicAudioObjectAssetId)
            ?? throw new InvalidOperationException($"{basicAudioObjectAssetId} does not exist.");

        if (existing.AssetType != AssetType.BAO)
            throw new InvalidOperationException($"{basicAudioObjectAssetId} is not a BasicAudioObject (it's {existing.AssetType}).");

        var existingData = existing.GetData<BasicAudioObject>()
            ?? throw new InvalidOperationException($"{basicAudioObjectAssetId} has no stored data.");

        var existingProps = existingData.BasicAudioObjectProperties ?? new BasicAudioObjectProperties();

        var updatedProps = new BasicAudioObjectProperties
        {
            BasicAudioObjectSpaceTime = existingProps.BasicAudioObjectSpaceTime,
            Level = level ?? existingProps.Level,
            PerceptStatus = perceptStatus ?? existingProps.PerceptStatus,
            AcousticProfile = acousticProfile ?? existingProps.AcousticProfile,
            BasicAudioObjectIdentifier = identifier ?? existingProps.BasicAudioObjectIdentifier
        };

        var saved = repository.SaveAsset(existing);
        saved.SetData(new BasicAudioObject
        {
            Header = existingData.Header,
            MInstanceID = existingData.MInstanceID,
            UEnvironmentID = existingData.UEnvironmentID,
            BasicAudioObjectID = saved.AssetId,
            BasicAudioObjectTime = existingData.BasicAudioObjectTime,
            ParentObjects = existingData.ParentObjects,
            ChildObjects = existingData.ChildObjects,
            BasicAudioObjectData = existingData.BasicAudioObjectData,
            BasicAudioObjectProperties = updatedProps,
            BasicAudioDataQualifier = existingData.BasicAudioDataQualifier,
            DataXMData = existingData.DataXMData,
            DescrMetadata = existingData.DescrMetadata
        });
        repository.UpdateAsset(saved);

        return saved;
    }

    // Produces a new version of an AudioObject with an updated
    // AudioObjectProperties (AcousticProfile) and/or ParentAudioObjectIDs.
    // Unspecified (null) parameters keep their existing value.
    // placement edits the object's own intrinsic AudioObjectSpaceTime - now
    // genuinely supported now that the schema confirmed this field is
    // SpaceTime, not SimpleTime. This is the AUO-editing mode: a user
    // places THIS object at a position independent of any scene, distinct
    // from ASE's per-placement position when the same object is later put
    // into a scene.
    public RepositoryAsset EditObjectProperties(
        string audioObjectAssetId,
        AcousticProfile? acousticProfile = null,
        List<string>? parentAudioObjectIDs = null,
        SpaceTime? placement = null)
    {
        var existing = repository.GetAsset(audioObjectAssetId)
            ?? throw new InvalidOperationException($"{audioObjectAssetId} does not exist.");

        if (existing.AssetType != AssetType.AUO)
            throw new InvalidOperationException($"{audioObjectAssetId} is not an AudioObject (it's {existing.AssetType}).");

        var existingData = existing.GetData<AudioObject>()
            ?? throw new InvalidOperationException($"{audioObjectAssetId} has no stored data.");

        var saved = repository.SaveAsset(existing);
        saved.SetData(new AudioObject
        {
            AudioObjectID = saved.AssetId,
            AudioObjectTime = existingData.AudioObjectTime,
            AudioObjectSpaceTime = placement ?? existingData.AudioObjectSpaceTime,
            AudioObjectProperties = acousticProfile ?? existingData.AudioObjectProperties,
            ParentAudioObjectIDs = parentAudioObjectIDs ?? existingData.ParentAudioObjectIDs,
            BasicAudioObjectCount = existingData.BasicAudioObjectCount,
            BasicAudioObjects = existingData.BasicAudioObjects,
            SubAudioObjectCount = existingData.SubAudioObjectCount,
            SubAudioObjects = existingData.SubAudioObjects
        });
        repository.UpdateAsset(saved);

        return saved;
    }

    // Resolves a stored AudioObject into a fully self-contained structure -
    // its own fields plus every BAO/sub-AudioObject it (transitively)
    // references, with each ID-stub entry replaced by its full, live
    // content. What an external consumer (ASD delivery, export) needs; the
    // stored Payload itself is already schema-valid before this resolution.
    public AudioObject Materialize(string audioObjectAssetId)
    {
        var asset = repository.GetAsset(audioObjectAssetId)
            ?? throw new InvalidOperationException($"{audioObjectAssetId} does not exist.");

        if (asset.AssetType != AssetType.AUO)
            throw new InvalidOperationException($"{audioObjectAssetId} is not an AudioObject (it's {asset.AssetType}).");

        var audioObject = asset.GetData<AudioObject>()
            ?? throw new InvalidOperationException($"{audioObjectAssetId} has no stored AudioObject data.");

        var basicChildren = new List<BasicAudioObjectEntry>();
        var subChildren = new List<SubAudioObjectEntry>();

        foreach (var entry in audioObject.BasicAudioObjects ?? new List<BasicAudioObjectEntry>())
        {
            var childId = entry.BAObjectIDOrBAObject?.BasicAudioObjectID;
            if (childId is null) continue;

            var childAsset = repository.GetAsset(childId);
            var bao = childAsset?.GetData<BasicAudioObject>();
            if (bao != null)
            {
                basicChildren.Add(new BasicAudioObjectEntry { BasicAudioObjectSpaceTime = entry.BasicAudioObjectSpaceTime, BAObjectIDOrBAObject = bao });
            }
        }

        foreach (var entry in audioObject.SubAudioObjects ?? new List<SubAudioObjectEntry>())
        {
            var childId = entry.SubAObjectIDOrSubAObject?.AudioObjectID;
            if (string.IsNullOrEmpty(childId)) continue;

            var childAsset = repository.GetAsset(childId);
            if (childAsset != null && childAsset.AssetType == AssetType.AUO)
            {
                subChildren.Add(new SubAudioObjectEntry { SubAudioObjectSpaceTime = entry.SubAudioObjectSpaceTime, SubAObjectIDOrSubAObject = Materialize(childId) });
            }
        }

        return new AudioObject
        {
            // AssetId is always authoritative for identity - the domain
            // payload's own AudioObjectID can silently go stale after a
            // SaveAsset (which copies Payload as-is and has no way to know
            // an ID is embedded inside it), so it is not trusted here.
            AudioObjectID = asset.AssetId,
            AudioObjectTime = audioObject.AudioObjectTime,
            AudioObjectSpaceTime = audioObject.AudioObjectSpaceTime,
            AudioObjectProperties = audioObject.AudioObjectProperties,
            ParentAudioObjectIDs = audioObject.ParentAudioObjectIDs,
            BasicAudioObjectCount = basicChildren.Count,
            BasicAudioObjects = basicChildren.Count > 0 ? basicChildren : null,
            SubAudioObjectCount = subChildren.Count,
            SubAudioObjects = subChildren.Count > 0 ? subChildren : null
        };
    }
}