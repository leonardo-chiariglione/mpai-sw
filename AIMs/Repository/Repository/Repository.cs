using System; 
using System.Collections.Generic; 
using System.IO;
using System.Linq; 
using System.Text.Json;
using System.Text.RegularExpressions;

using ASL.FileService;
using ASL.SearchService;

namespace Mpai.Repository; 

// An Asset is any MPAI Data Type instance (Basic Audio Object, Basic Audio
// Scene, Acoustic Profile, and in future any other AIM's data type). This
// class only knows about Assets as identified, versioned, referenceable
// things - it is not audio-specific, and any AIM can use it the same way.
//
// Persistence, when a root path is supplied, is layered on top of the
// existing in-memory model rather than replacing it: every mutating
// operation still updates the in-memory dictionaries exactly as before
// (so GetAncestors/GetDescendants/ValidateRepository etc. are unchanged),
// and is additionally written to disk via IFileService, one JSON file per
// Asset under {root}\{AssetType}\{AssetId}.json. On construction, any
// existing files under root are loaded back in via ISearchService, so a
// Repository picks up where a previous run left off.
public class AssetRepository 
{ 
    private readonly Dictionary<string, RepositoryAsset> _assets = new(); 
    private readonly List<Reference> _references = new(); 
    private readonly Dictionary<AssetType, int> _counters = new(); 
    private long _nextSequence = 0;

    private readonly string? _rootPath;
    private readonly IFileService? _fileService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public AssetRepository() { }

    public AssetRepository(string rootPath, IFileService? fileService = null, ISearchService? searchService = null)
    {
        _rootPath = rootPath;
        _fileService = fileService ?? new FileService();
        var search = searchService ?? new SearchService();

        foreach (AssetType assetType in Enum.GetValues<AssetType>())
        {
            Directory.CreateDirectory(AssetTypeDirectory(assetType));
        }

        Load(search);
    }

    private string AssetTypeDirectory(AssetType assetType) => Path.Combine(_rootPath!, assetType.ToString());

    private string AssetPath(RepositoryAsset asset) => Path.Combine(AssetTypeDirectory(asset.AssetType), $"{asset.AssetId}.json");

    private string ReferencesPath => Path.Combine(_rootPath!, "_references.json");

    private void Load(ISearchService search)
    {
        if (_rootPath == null || _fileService == null) return;

        foreach (var file in search.SearchFileNames(_rootPath, ".json"))
        {
            if (Path.GetFileName(file) == "_references.json") continue;

            var asset = JsonSerializer.Deserialize<RepositoryAsset>(_fileService.ReadFile(file), JsonOptions);
            if (asset == null) continue;

            _assets[asset.AssetId] = asset;
            BumpCounterFor(asset);
            if (asset.CreatedSequence >= _nextSequence) _nextSequence = asset.CreatedSequence + 1;
        }

        if (_fileService is not null && File.Exists(ReferencesPath))
        {
            var loaded = JsonSerializer.Deserialize<List<Reference>>(_fileService.ReadFile(ReferencesPath), JsonOptions);
            if (loaded != null) _references.AddRange(loaded);
        }
    }

    // AssetId is "{AssetType}{Counter:D6}" (e.g. "BAO000001"). On load, the
    // in-memory counter must be advanced past whatever's already on disk,
    // or a freshly generated ID could collide with one already saved.
    private void BumpCounterFor(RepositoryAsset asset)
    {
        var match = Regex.Match(asset.AssetId, @"(\d+)$");
        if (!match.Success) return;

        var number = int.Parse(match.Value);
        if (!_counters.TryGetValue(asset.AssetType, out var current) || number > current)
        {
            _counters[asset.AssetType] = number;
        }
    }

    private void Persist(RepositoryAsset asset)
    {
        if (_fileService == null) return;
        _fileService.WriteFile(AssetPath(asset), JsonSerializer.Serialize(asset, JsonOptions));
    }

    private void PersistReferences()
    {
        if (_fileService == null) return;
        _fileService.WriteFile(ReferencesPath, JsonSerializer.Serialize(_references, JsonOptions));
    }

    public string GenerateAssetId(AssetType assetType) 
    { 
        if (!_counters.ContainsKey(assetType)) _counters[assetType] = 0; 
        _counters[assetType]++; 
        return $"{assetType}{_counters[assetType]:D6}"; 
    } 
 
    public void CreateAsset(RepositoryAsset asset) 
    { 
        if (_assets.ContainsKey(asset.AssetId)) 
            throw new InvalidOperationException($"Asset {asset.AssetId} already exists."); 
        asset.CreatedSequence = _nextSequence++;
        _assets.Add(asset.AssetId, asset); 
        Persist(asset);
    } 

    // Re-persists an asset whose Payload has changed in memory (e.g. via
    // SetData) since it was created. Does NOT create a new version or a new
    // AssetId - for that, use SaveAsset. This exists specifically so a
    // caller can build up a schema-correct payload (e.g. appending an entry
    // to AudioSceneDescriptors.AudioObjects) across a couple of steps and
    // then commit the final result to disk in one call, rather than the
    // in-memory object silently drifting from what's actually persisted.
    public void UpdateAsset(RepositoryAsset asset)
    {
        if (!_assets.ContainsKey(asset.AssetId))
            throw new InvalidOperationException($"Cannot update {asset.AssetId}: it does not exist.");
        Persist(asset);
    }
 
    public RepositoryAsset SaveAsset(RepositoryAsset asset) 
    { 
        var savedAsset = new RepositoryAsset 
        { 
            AssetId = GenerateAssetId(asset.AssetType), 
            AssetType = asset.AssetType, 
            ParentAssetId = asset.AssetId, 
            Payload = new Dictionary<string, object>(asset.Payload) 
        }; 
        CreateAsset(savedAsset); 

        // References are static and belong to the version that made them,
        // not automatically to the version being edited (references never
        // repoint from an old ID to a new one on their own). But the asset
        // being saved is a composite - editing it must not silently drop
        // the very children it referenced. So the new version starts out
        // referencing exactly what the old version referenced, unchanged;
        // any actual change to *which* children it references is a
        // separate, explicit edit the caller makes on top of this.
        foreach (var reference in _references.Where(r => r.SourceAssetId == asset.AssetId).ToList())
        {
            CreateReference(savedAsset.AssetId, reference.TargetAssetId, reference.ReferenceType, new Dictionary<string, object>(reference.Metadata));
        }

        return savedAsset; 
    } 
 
    public bool ValidateRepository() 
    { 
        foreach (var asset in _assets.Values) 
        { 
            if (asset.ParentAssetId != null && !_assets.ContainsKey(asset.ParentAssetId)) 
                return false; 
        } 
 
        foreach (var reference in _references) 
        { 
            if (!_assets.ContainsKey(reference.SourceAssetId)) return false; 
            if (!_assets.ContainsKey(reference.TargetAssetId)) return false; 
            if (reference.SourceAssetId == reference.TargetAssetId) return false; 
        } 
 
        return true; 
    } 
 
    public RepositoryAsset? GetAsset(string assetId) 
    { 
        _assets.TryGetValue(assetId, out var asset); 
        return asset; 
    } 
 
    public IEnumerable<RepositoryAsset> FindAssets(AssetType assetType) 
    { 
        return _assets.Values.Where(a => a.AssetType == assetType); 
    } 
 
    // Section 13.7 Integrity Operations - CheckDeleteAllowed. DeleteAsset
    // enforces this same rule (Repository Pentekaidekalogue Principle 10:
    // a referenced Asset shall not be deleted while references exist), so
    // both go through the one check rather than duplicating the rule.
    public bool CheckDeleteAllowed(string assetId) => !GetReferrers(assetId).Any();

    public void DeleteAsset(string assetId) 
    { 
        if (!CheckDeleteAllowed(assetId)) 
            throw new InvalidOperationException($"Asset {assetId} is referenced."); 

        if (_fileService != null && _assets.TryGetValue(assetId, out var asset) && File.Exists(AssetPath(asset)))
        {
            _fileService.DeleteFile(AssetPath(asset));
        }

        _assets.Remove(assetId); 
    } 
 
    public void CreateReference(string sourceAssetId, string targetAssetId, string referenceType = "Contains", Dictionary<string, object>? metadata = null) 
    { 
        var source = GetAsset(sourceAssetId) ?? throw new InvalidOperationException($"Cannot reference from {sourceAssetId}: it does not exist.");
        var target = GetAsset(targetAssetId) ?? throw new InvalidOperationException($"Cannot reference {targetAssetId}: it does not exist.");

        // A data instance with an earlier creation time cannot be referenced
        // by something that existed before it. Enforcing target strictly
        // earlier than source, using the Repository's own reliable creation
        // order rather than each type's optional Time field, provably rules
        // out cycles: a cycle would require some asset to be both strictly
        // earlier and strictly later than another, which is a contradiction.
        if (target.CreatedSequence >= source.CreatedSequence)
        {
            throw new InvalidOperationException(
                $"Cannot reference {targetAssetId} from {sourceAssetId}: {targetAssetId} was not created before {sourceAssetId}. " +
                "A reference can only point at something that already existed.");
        }

        _references.Add(new Reference{SourceAssetId=sourceAssetId,TargetAssetId=targetAssetId,ReferenceType=referenceType, Metadata = metadata ?? new()}); 
        PersistReferences();
    } 

    // Section 13.4 Reference Operations - RemoveReference. Removes the
    // first matching reference; matches on all three fields so removing
    // one edge doesn't accidentally remove a different reference type
    // between the same two assets.
    public bool RemoveReference(string sourceAssetId, string targetAssetId, string referenceType = "Contains")
    {
        var match = _references.FirstOrDefault(r =>
            r.SourceAssetId == sourceAssetId &&
            r.TargetAssetId == targetAssetId &&
            r.ReferenceType == referenceType);

        if (match == null) return false;

        _references.Remove(match);
        PersistReferences();
        return true;
    }

    public IEnumerable<string> GetReferences(string assetId) => _references.Where(r=>r.SourceAssetId==assetId).Select(r=>r.TargetAssetId); 
    public IEnumerable<string> GetReferrers(string assetId) => _references.Where(r=>r.TargetAssetId==assetId).Select(r=>r.SourceAssetId); 

    // The full Reference objects (with Metadata) for a source asset - for
    // callers that need placement-specific data, not just the target ID.
    public IEnumerable<Reference> GetReferenceDetails(string assetId) => _references.Where(r => r.SourceAssetId == assetId);

    // Section 13.5 Provenance Operations - GetProvenance. Per section 5,
    // provenance for a single Asset is just its immediate parent (the
    // asset it was Saved from); the full ancestor chain is GetAncestors.
    public string? GetProvenance(string assetId) => GetAsset(assetId)?.ParentAssetId;

    // Section 13.6 Dependency Operations. Dependencies are distinguished
    // from References in section 3.7 ("Dependencies are maintained
    // independently of asset versioning") and illustrated as a transitive
    // chain (BAS -> BAO -> AcousticProfile), so GetDependencies walks the
    // full outgoing-reference graph, not just one hop like GetReferences.
    public IEnumerable<string> GetDependencies(string assetId)
    {
        var visited = new HashSet<string>();
        var result = new List<string>();
        var queue = new Queue<string>(GetReferences(assetId));

        while (queue.Count > 0)
        {
            var next = queue.Dequeue();
            if (!visited.Add(next)) continue;

            result.Add(next);

            foreach (var deeper in GetReferences(next))
            {
                queue.Enqueue(deeper);
            }
        }

        return result;
    }

    // ValidateDependencies: every asset this one (transitively) depends on
    // must still exist. A dependency pointing at a deleted/missing asset
    // is exactly the integrity failure this guards against.
    public bool ValidateDependencies(string assetId) =>
        GetDependencies(assetId).All(id => _assets.ContainsKey(id));
 
    public IEnumerable<string> GetAncestors(string assetId) 
    { 
        var result = new List<string>(); 
        var current = GetAsset(assetId); 
        while (current?.ParentAssetId != null) 
        { 
            var parent = GetAsset(current.ParentAssetId); 
            if (parent == null) break; 
            result.Add(parent.AssetId); 
            current = parent; 
        } 
        return result; 
    } 
 
    public IEnumerable<string> GetDescendants(string assetId) 
    { 
        var result = new List<string>(); 
        foreach (var asset in _assets.Values) 
        { 
            if (asset.ParentAssetId == assetId) 
            { 
                result.Add(asset.AssetId); 
                result.AddRange(GetDescendants(asset.AssetId)); 
            } 
        } 
        return result; 
    } 
} 
 