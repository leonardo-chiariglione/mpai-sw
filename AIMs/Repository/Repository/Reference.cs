using System.Collections.Generic;

namespace Mpai.Repository;

// A Reference is generic ("this Asset points at that Asset"), deliberately
// domain-agnostic - Mpai.Repository doesn't know what a SpatialAttitude or
// any other domain type is. Metadata is where a caller (e.g. AseAim) can
// attach typed, placement-specific data - "this particular placement of
// child X within parent Y is positioned here" - without Repository itself
// needing to reference any domain-specific project.
public class Reference
{
    public string SourceAssetId { get; set; } = string.Empty;

    public string TargetAssetId { get; set; } = string.Empty;

    public string ReferenceType { get; set; } = string.Empty;

    public Dictionary<string, object> Metadata { get; set; } = new();

    private const string DataKey = "Data";

    public void SetData<T>(T value)
    {
        Metadata[DataKey] = System.Text.Json.JsonSerializer.Serialize(value, RepositoryAsset.DataOptions);
    }

    public T? GetData<T>()
    {
        if (!Metadata.TryGetValue(DataKey, out var raw)) return default;

        var json = raw switch
        {
            string s => s,
            System.Text.Json.JsonElement je => je.GetString() ?? "",
            _ => raw.ToString() ?? ""
        };

        return string.IsNullOrEmpty(json) ? default : System.Text.Json.JsonSerializer.Deserialize<T>(json, RepositoryAsset.DataOptions);
    }
}