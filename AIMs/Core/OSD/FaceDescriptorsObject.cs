using System;
using System.Collections.Generic;
using System.Linq;

namespace Mpai.Core.OSD;

// PAF-FDO-V1.6 - Face Descriptors Object. The standard MPAI type that carries an
// Entity's face descriptors (here: an ArcFace embedding) plus a Qualifier saying
// which descriptor format the Data is. Produced by PAF-EFD (Entity Face
// Description) and stored, as a serialized standard type, in the gallery so any
// AIM can read it.
//
// Mirrors schemas/PAF/V1.6/data/FaceDescriptorsObject.json: Data is carried
// inline as a base64 string inside a FaceDescriptorsData item; the embedding is
// packed little-endian float32 -> bytes -> base64.
public sealed class FaceDescriptorsObject
{
    public string Header { get; init; } = "PAF-FDO-V1.6";
    public string? MInstanceID { get; init; }
    public string? UEnvironmentID { get; init; }
    public string FaceDescriptorsObjectID { get; init; } = "";
    public List<FaceDescriptorsDataItem> FaceDescriptorsData { get; init; } = new();
    public FaceDescriptorsQualifier? FaceDescriptorsQualifier { get; init; }
    public string? DescrMetadata { get; init; }

    // Build an FDO from an embedding vector and the model that produced it.
    public static FaceDescriptorsObject FromEmbedding(float[] embedding, string contentFormat)
        => new()
        {
            FaceDescriptorsObjectID = Guid.NewGuid().ToString(),
            FaceDescriptorsData = new List<FaceDescriptorsDataItem>
            {
                new() { Data = EmbeddingCodec.ToBase64(embedding) }
            },
            FaceDescriptorsQualifier = FaceDescriptorsQualifier.For(contentFormat)
        };

    // The embedding carried inline, or null if this object references its data
    // elsewhere (DataURI/DataID) rather than inlining it.
    public float[]? Embedding()
    {
        var inline = FaceDescriptorsData.FirstOrDefault(d => d.Data is not null)?.Data;
        return inline is null ? null : EmbeddingCodec.FromBase64(inline);
    }
}

// One FaceDescriptorsData item: inline (Data), by reference (DataURI+DataLength),
// or by identifier (DataID). Matches the schema's three-way anyOf.
public sealed class FaceDescriptorsDataItem
{
    public string? Data { get; init; }        // inline, base64
    public string? DataURI { get; init; }      // by reference
    public long? DataLength { get; init; }
    public string? DataID { get; init; }       // by identifier
}

// TFA-FDQ-V1.5 - the qualifier. Its ContentFormat records which descriptor format
// the Data is (e.g. "ArcFace (ResNet-100, 512-d)").
public sealed class FaceDescriptorsQualifier
{
    public string Header { get; init; } = "TFA-FDQ-V1.5";
    public string FaceDescriptorsQualifierID { get; init; } = "";
    public FaceDescriptorsFormats Formats { get; init; } = new();

    public static FaceDescriptorsQualifier For(string contentFormat) => new()
    {
        FaceDescriptorsQualifierID = Guid.NewGuid().ToString(),
        Formats = new FaceDescriptorsFormats { ContentFormat = contentFormat }
    };
}

public sealed class FaceDescriptorsFormats
{
    // A value from TFA/V1.5/formats/FaceDescriptorsContentFormats.json.
    public string? ContentFormat { get; init; }
}
