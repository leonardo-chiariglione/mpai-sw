using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using AIF.SharedStorage;
using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Repository;

namespace Mpai.Cae.Aoe;

// ---------------------------------------------------------------------------
//  CAE-AOE-V1.0 - Audio Object Editing.
//
//  Ported directly onto the proposed MPAI-AIF Shared Storage API
//  (Put/Get/Delete/List/Exists/GetKeyInfo) - no intermediate Repository
//  class or method vocabulary (CreateAsset/SaveAsset/GetAsset/
//  CreateReference are gone). An AssetId doubles as its own Shared Storage
//  key: its type prefix (BAO/AUO) already makes it self-describing for
//  List(prefix) queries, so no separate type-namespacing is layered on top
//  of it, per the Shared Storage proposal's Section 4.1 pattern.
//
//  Editing Principle preserved exactly: every edit produces a NEW key via a
//  fresh Put - never an overwrite of an existing AssetId's content. A
//  BasicAudioObject is the degenerate case of an AudioObject with no
//  sub-objects, not a separate type to special-case.
//
//  Cycle prevention in AddSubObject is a genuine new implementation here
//  (WouldCreateCycle below), not a port of the previous Repository's
//  CreateReference-based check, whose exact algorithm was not available at
//  port time - verified with its own dedicated test rather than assumed
//  equivalent.
// ---------------------------------------------------------------------------
public sealed class AoeAim
{
    private readonly ISharedStorage storage;

    public AoeAim(ISharedStorage storage) => this.storage = storage;

    private static readonly JsonSerializerOptions DataOptions = new() { Converters = { new JsonStringEnumConverter() } };
    private static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, DataOptions);
    private static T Deserialize<T>(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes, DataOptions)!;

    // A fresh, sequential, type-prefixed key per Asset (AUO000001,
    // AUO000002, ...), matching the numbering scheme already familiar from
    // the whole project. The counter is itself a reserved key ("_counter:"
    // prefix, which List(typePrefix) never matches, since it doesn't start
    // with the type prefix), so it persists across sessions exactly like
    // everything else - proven by the same cross-session persistence
    // ISharedStorage itself already relies on.
    private string NextId(string typePrefix)
    {
        var counterKey = $"_counter:{typePrefix}";
        long next = storage.Exists(counterKey)
            ? long.Parse(Encoding.UTF8.GetString(storage.Get(counterKey))) + 1
            : 1;
        storage.Put(counterKey, Encoding.UTF8.GetBytes(next.ToString()));
        return $"{typePrefix}{next:D6}";
    }

    // Per Section 4.3 of the Shared Storage proposal - recorded for
    // completeness/future queries, even though AoeAim/AseAim's own public
    // surface does not currently expose GetReferences/GetReferrers.
    private void PutReference(string fromId, string toId)
    {
        storage.Put($"ref:{fromId}:{toId}", Array.Empty<byte>());
        storage.Put($"refby:{toId}:{fromId}", Array.Empty<byte>());
    }

    private static bool IsType(string assetId, string prefix) => assetId.StartsWith(prefix, StringComparison.Ordinal);

    // ID-stub entries: only the identifying field set, nothing else - a
    // minimal but genuinely valid instance of the "object" branch of the
    // schema's "object or id-string" oneOf. Materialize() resolves these
    // into full content; the stub itself is what's actually persisted.
    private static BasicAudioObject IdStubBao(string assetId) => new() { BasicAudioObjectID = assetId };
    private static AudioObject IdStubAuo(string assetId) => new() { AudioObjectID = assetId };

    // Wraps a single BasicAudioObject into a new, minimal AudioObject Asset
    // (the degenerate case: one leaf, no sub-objects). The BAO is stored as
    // its own addressable key too - BAOs are static and independently
    // referenceable.
    public RepositoryAsset CreateObject(BasicAudioObject bao)
    {
        var baoId = NextId("BAO");
        storage.Put(baoId, Serialize(bao.WithId(baoId)));

        var auoId = NextId("AUO");
        storage.Put(auoId, Serialize(new AudioObject
        {
            AudioObjectID = auoId,
            BasicAudioObjectCount = 1,
            BasicAudioObjects = new() { new BasicAudioObjectEntry { BAObjectIDOrBAObject = IdStubBao(baoId) } }
        }));

        PutReference(auoId, baoId);

        return new RepositoryAsset { AssetId = auoId, AssetType = AssetType.AUO };
    }

    // Adds an existing Asset (a BAO or another AudioObject) as a child of an
    // existing AudioObject. Produces a NEW key for the AudioObject (per the
    // no-cascade Save rule: only the edited instance itself gets a new
    // identity; the child being added is untouched).
    public RepositoryAsset AddSubObject(string audioObjectAssetId, string childAssetId)
    {
        if (!storage.Exists(audioObjectAssetId))
            throw new InvalidOperationException($"{audioObjectAssetId} does not exist.");
        if (!IsType(audioObjectAssetId, "AUO"))
            throw new InvalidOperationException($"{audioObjectAssetId} is not an AudioObject.");

        if (!storage.Exists(childAssetId))
            throw new InvalidOperationException($"{childAssetId} does not exist.");

        var childIsBao = IsType(childAssetId, "BAO");
        var childIsAuo = IsType(childAssetId, "AUO");
        if (!childIsBao && !childIsAuo)
            throw new InvalidOperationException($"{childAssetId} is not a BAO or AudioObject.");

        if (childIsAuo && WouldCreateCycle(audioObjectAssetId, childAssetId))
            throw new InvalidOperationException($"Adding {childAssetId} to {audioObjectAssetId} would create a cycle.");

        var existingData = Deserialize<AudioObject>(storage.Get(audioObjectAssetId));

        var newId = NextId("AUO");
        PutReference(newId, childAssetId);

        var basicEntries = (existingData.BasicAudioObjects ?? new List<BasicAudioObjectEntry>()).ToList();
        var subEntries = (existingData.SubAudioObjects ?? new List<SubAudioObjectEntry>()).ToList();

        if (childIsBao)
        {
            basicEntries.Add(new BasicAudioObjectEntry { BAObjectIDOrBAObject = IdStubBao(childAssetId) });
        }
        else
        {
            subEntries.Add(new SubAudioObjectEntry { SubAObjectIDOrSubAObject = IdStubAuo(childAssetId) });
        }

        storage.Put(newId, Serialize(new AudioObject
        {
            AudioObjectID = newId,
            AudioObjectTime = existingData.AudioObjectTime,
            AudioObjectSpaceTime = existingData.AudioObjectSpaceTime,
            AudioObjectProperties = existingData.AudioObjectProperties,
            ParentAudioObjectIDs = existingData.ParentAudioObjectIDs,
            BasicAudioObjectCount = basicEntries.Count,
            BasicAudioObjects = basicEntries.Count > 0 ? basicEntries : null,
            SubAudioObjectCount = subEntries.Count,
            SubAudioObjects = subEntries.Count > 0 ? subEntries : null
        }));

        return new RepositoryAsset { AssetId = newId, AssetType = AssetType.AUO };
    }

    // A cycle would form if rootId is reachable by walking down from
    // candidateChildId's own sub-object tree. Only SubAudioObjects matter -
    // BasicAudioObjects/BAOs are always leaves and can never participate in
    // a cycle.
    private bool WouldCreateCycle(string rootId, string candidateChildId)
    {
        if (rootId == candidateChildId) return true;
        if (!IsType(candidateChildId, "AUO") || !storage.Exists(candidateChildId)) return false;

        var data = Deserialize<AudioObject>(storage.Get(candidateChildId));
        foreach (var entry in data.SubAudioObjects ?? new List<SubAudioObjectEntry>())
        {
            var grandchildId = entry.SubAObjectIDOrSubAObject?.AudioObjectID;
            if (grandchildId != null && WouldCreateCycle(rootId, grandchildId))
                return true;
        }
        return false;
    }

    // Produces a new version of a BasicAudioObject with updated properties
    // (Level, PerceptStatus, AcousticProfile, BasicAudioObjectIdentifier).
    // Unspecified (null) parameters keep their existing value - this edits,
    // it does not replace. The audio DATA itself is never touched here,
    // consistent with "BAOs are static" - only the descriptive properties
    // that sit alongside the data change, and that change is itself a new,
    // immutable key, same as everywhere else.
    public RepositoryAsset EditBasicObjectProperties(
        string basicAudioObjectAssetId,
        double? level = null,
        bool? perceptStatus = null,
        AcousticProfile? acousticProfile = null,
        InstanceIdentifier? identifier = null)
    {
        if (!storage.Exists(basicAudioObjectAssetId))
            throw new InvalidOperationException($"{basicAudioObjectAssetId} does not exist.");
        if (!IsType(basicAudioObjectAssetId, "BAO"))
            throw new InvalidOperationException($"{basicAudioObjectAssetId} is not a BasicAudioObject.");

        var existingData = Deserialize<BasicAudioObject>(storage.Get(basicAudioObjectAssetId));
        var existingProps = existingData.BasicAudioObjectProperties ?? new BasicAudioObjectProperties();

        var updatedProps = new BasicAudioObjectProperties
        {
            BasicAudioObjectSpaceTime = existingProps.BasicAudioObjectSpaceTime,
            Level = level ?? existingProps.Level,
            PerceptStatus = perceptStatus ?? existingProps.PerceptStatus,
            AcousticProfile = acousticProfile ?? existingProps.AcousticProfile,
            BasicAudioObjectIdentifier = identifier ?? existingProps.BasicAudioObjectIdentifier
        };

        var newId = NextId("BAO");
        storage.Put(newId, Serialize(new BasicAudioObject
        {
            Header = existingData.Header,
            MInstanceID = existingData.MInstanceID,
            UEnvironmentID = existingData.UEnvironmentID,
            BasicAudioObjectID = newId,
            BasicAudioObjectTime = existingData.BasicAudioObjectTime,
            ParentObjects = existingData.ParentObjects,
            ChildObjects = existingData.ChildObjects,
            BasicAudioObjectData = existingData.BasicAudioObjectData,
            BasicAudioObjectProperties = updatedProps,
            AudioQualifier = existingData.AudioQualifier,
            DataXMData = existingData.DataXMData,
            DescrMetadata = existingData.DescrMetadata
        }));

        return new RepositoryAsset { AssetId = newId, AssetType = AssetType.BAO };
    }

    // Produces a new version of an AudioObject with an updated
    // AudioObjectProperties (AcousticProfile) and/or ParentAudioObjectIDs.
    // placement edits the object's own intrinsic AudioObjectSpaceTime -
    // Mode 1 (AUO editing): a user places THIS object at a position
    // independent of any scene, distinct from ASE's per-placement position
    // when the same object is later put into a scene.
    public RepositoryAsset EditObjectProperties(
        string audioObjectAssetId,
        AcousticProfile? acousticProfile = null,
        List<string>? parentAudioObjectIDs = null,
        SpaceTime? placement = null)
    {
        if (!storage.Exists(audioObjectAssetId))
            throw new InvalidOperationException($"{audioObjectAssetId} does not exist.");
        if (!IsType(audioObjectAssetId, "AUO"))
            throw new InvalidOperationException($"{audioObjectAssetId} is not an AudioObject.");

        var existingData = Deserialize<AudioObject>(storage.Get(audioObjectAssetId));

        var newId = NextId("AUO");
        storage.Put(newId, Serialize(new AudioObject
        {
            AudioObjectID = newId,
            AudioObjectTime = existingData.AudioObjectTime,
            AudioObjectSpaceTime = placement ?? existingData.AudioObjectSpaceTime,
            AudioObjectProperties = acousticProfile ?? existingData.AudioObjectProperties,
            ParentAudioObjectIDs = parentAudioObjectIDs ?? existingData.ParentAudioObjectIDs,
            BasicAudioObjectCount = existingData.BasicAudioObjectCount,
            BasicAudioObjects = existingData.BasicAudioObjects,
            SubAudioObjectCount = existingData.SubAudioObjectCount,
            SubAudioObjects = existingData.SubAudioObjects
        }));

        return new RepositoryAsset { AssetId = newId, AssetType = AssetType.AUO };
    }

    // Resolves a stored AudioObject into a fully self-contained structure -
    // its own fields plus every BAO/sub-AudioObject it (transitively)
    // references, with each ID-stub entry replaced by its full, live
    // content. The stored value itself is already schema-valid before this
    // resolution happens; this is what an external consumer (ASD delivery,
    // export) needs.
    // Does this asset exist? Needed by the AIF-facing half to tell an OPEN from a
    // CREATE: a Basic Audio Object arriving with an identifier already in the
    // repository is being opened for editing; one without is new. Without this,
    // an existing Basic Audio Object could never be edited, only replaced.
    public bool Has(string assetId) =>
        !string.IsNullOrWhiteSpace(assetId) && storage.Exists(assetId);

    public AudioObject Materialize(string audioObjectAssetId)
    {
        if (!storage.Exists(audioObjectAssetId))
            throw new InvalidOperationException($"{audioObjectAssetId} does not exist.");
        if (!IsType(audioObjectAssetId, "AUO"))
            throw new InvalidOperationException($"{audioObjectAssetId} is not an AudioObject.");

        var audioObject = Deserialize<AudioObject>(storage.Get(audioObjectAssetId));

        var basicChildren = new List<BasicAudioObjectEntry>();
        var subChildren = new List<SubAudioObjectEntry>();

        foreach (var entry in audioObject.BasicAudioObjects ?? new List<BasicAudioObjectEntry>())
        {
            var childId = entry.BAObjectIDOrBAObject?.BasicAudioObjectID;
            if (childId is null || !storage.Exists(childId)) continue;

            var bao = Deserialize<BasicAudioObject>(storage.Get(childId));
            basicChildren.Add(new BasicAudioObjectEntry { BasicAudioObjectSpaceTime = entry.BasicAudioObjectSpaceTime, BAObjectIDOrBAObject = bao });
        }

        foreach (var entry in audioObject.SubAudioObjects ?? new List<SubAudioObjectEntry>())
        {
            var childId = entry.SubAObjectIDOrSubAObject?.AudioObjectID;
            if (string.IsNullOrEmpty(childId) || !storage.Exists(childId) || !IsType(childId, "AUO")) continue;

            subChildren.Add(new SubAudioObjectEntry { SubAudioObjectSpaceTime = entry.SubAudioObjectSpaceTime, SubAObjectIDOrSubAObject = Materialize(childId) });
        }

        return new AudioObject
        {
            // AssetId is always authoritative for identity - the domain
            // payload's own AudioObjectID has no independent way of being
            // kept in sync, so it is not trusted here.
            AudioObjectID = audioObjectAssetId,
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

    // --- Deletion (surfaces the sixth Shared Storage primitive, Delete) ---
    // One-level reference set for an asset: every id it references
    // (ref:{id}:*) and every id that references it (refby:{id}:*). Used by
    // the UI to decide whether a Delete is safe and, if not, to show and
    // optionally cascade over exactly the assets involved - one level only.
    public IReadOnlyList<string> ReferencedBy(string assetId) =>
        storage.List($"refby:{assetId}:").Select(k => k[$"refby:{assetId}:".Length..]).ToList();

    public IReadOnlyList<string> References(string assetId) =>
        storage.List($"ref:{assetId}:").Select(k => k[$"ref:{assetId}:".Length..]).ToList();

    // Deletes a single asset key together with its own ref:/refby: bookkeeping
    // (both directions), per Section 4.3 of the Shared Storage proposal.
    // Deleting a non-existent key is not an error (Section 4.10.3). Does NOT
    // itself cascade - the caller decides, one level, with user consent.
    public void Delete(string assetId)
    {
        foreach (var toId in References(assetId))
        {
            storage.Delete($"ref:{assetId}:{toId}");
            storage.Delete($"refby:{toId}:{assetId}");
        }
        foreach (var fromId in ReferencedBy(assetId))
        {
            storage.Delete($"ref:{fromId}:{assetId}");
            storage.Delete($"refby:{assetId}:{fromId}");
        }
        storage.Delete(assetId);
    }
}