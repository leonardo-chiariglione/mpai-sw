using System.Collections.Generic;

namespace ASM.RepositoryCore;

public class RepositoryAsset
{
    public string AssetId { get; set; } = string.Empty;

    public AssetType AssetType { get; set; }

    public string? ParentAssetId { get; set; }

    public Dictionary<string, object> Payload { get; set; } = new();
}