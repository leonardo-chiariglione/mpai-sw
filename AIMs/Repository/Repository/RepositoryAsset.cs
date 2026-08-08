using System.Collections.Generic;

namespace Mpai.Repository;

public class RepositoryAsset
{
    public string AssetId { get; set; } = string.Empty;

    public AssetType AssetType { get; set; }

    public string? ParentAssetId { get; set; }

    public Dictionary<string, object> Payload { get; set; } = new();

    // Set by Repository at CreateAsset time; strictly increasing, no ties.
    // Used to enforce that a reference can only point at something that
    // already existed when the referencing asset was created - a data
    // instance with an earlier time cannot be referenced by something that
    // existed before it. This is a Repository-level guarantee rather than
    // reading each type's own optional Time field, since none of the
    // schemas seen so far actually require that field to be present.
    public long CreatedSequence { get; set; }

    // Typed payload access. Payload is a Dictionary<string,object> for
    // flexibility across arbitrary MPAI Data Types, but on reload from disk
    // a raw object value comes back as an untyped JsonElement, not the
    // original CLR type. Storing/retrieving the actual data type (e.g.
    // AudioObject, BasicAudioObject) through these two methods - rather than
    // reading Payload directly - keeps that round-trip reliable regardless
    // of whether the asset came from memory or from a reload.
    private const string DataKey = "Data";

    // Same reasoning as Repository's own JsonOptions: without
    // JsonStringEnumConverter, enums embedded in a data type (e.g.
    // CoordinateType, ObjectType inside PointOfView) would serialize as raw
    // integers - silently fragile if the enum is ever reordered.
    internal static readonly System.Text.Json.JsonSerializerOptions DataOptions = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public void SetData<T>(T value)
    {
        Payload[DataKey] = System.Text.Json.JsonSerializer.Serialize(value, DataOptions);
    }

    public T? GetData<T>()
    {
        if (!Payload.TryGetValue(DataKey, out var raw)) return default;

        var json = raw switch
        {
            string s => s,
            System.Text.Json.JsonElement je => je.GetString() ?? "",
            _ => raw.ToString() ?? ""
        };

        return string.IsNullOrEmpty(json) ? default : System.Text.Json.JsonSerializer.Deserialize<T>(json, DataOptions);
    }
}