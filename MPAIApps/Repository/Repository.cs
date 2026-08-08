using System; 
using System.Collections.Generic; 
using System.Linq; 
 
namespace ASM.RepositoryCore; 
 
public class Repository 
{ 
    private readonly Dictionary<string, RepositoryAsset> _assets = new(); 
    private readonly List<Reference> _references = new(); 
    private readonly Dictionary<AssetType, int> _counters = new(); 
 
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
        _assets.Add(asset.AssetId, asset); 
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
 
    public void DeleteAsset(string assetId) 
    { 
        if (GetReferrers(assetId).Any()) 
            throw new InvalidOperationException($"Asset {assetId} is referenced."); 
        _assets.Remove(assetId); 
    } 
 
    public void CreateReference(string sourceAssetId,string targetAssetId,string referenceType = "Contains") 
    { 
        _references.Add(new Reference{SourceAssetId=sourceAssetId,TargetAssetId=targetAssetId,ReferenceType=referenceType}); 
    } 
 
    public IEnumerable<string> GetReferences(string assetId) => _references.Where(r=>r.SourceAssetId==assetId).Select(r=>r.TargetAssetId); 
    public IEnumerable<string> GetReferrers(string assetId) => _references.Where(r=>r.TargetAssetId==assetId).Select(r=>r.SourceAssetId); 
 
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
 