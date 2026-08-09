using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Mpai.Core;

namespace Mpai.Mas.Rca;

// Serialises MPAI Basic Objects to/from the MAS "MPAI/port-data" wire bytes,
// per the OSD/V1.5 schemas (BasicVisualObject/BasicTextObject/BasicAudioObject).
//
// Wire form: a single JSON document conforming to the object's schema, with the
// heavy data carried INLINE as base64 in the "...Data" array (the schema's
// { "Data": "<string>" } variant). The reference forms (DataURI / ID) are not
// used for the demo; inline keeps the client self-contained.
//
// This is a BOUNDARY translator: our internal objects (Mpai.Core) already match
// these schemas closely, so the mapping is thin. Qualifiers are carried through
// when present but are not required for the demo payload (the Data is what the
// pipeline needs); full qualifier fidelity is a later conformance pass.
public static class MpaiPortData
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // ── Visual ───────────────────────────────────────────────────────────────
    // OSD-BVO-V1.5: { Header, BasicVisualObjectID, BasicVisualObjectData:[{Data}],
    //                 VisualQualifier? }
    public static byte[] FromVisual(BasicVisualObject v)
    {
        var obj = new JsonObject
        {
            ["Header"]              = "OSD-BVO-V1.5",
            ["BasicVisualObjectID"] = string.IsNullOrEmpty(v.BasicVisualObjectID)
                                        ? Guid.NewGuid().ToString() : v.BasicVisualObjectID,
            ["BasicVisualObjectData"] = new JsonArray(
                new JsonObject { ["Data"] = Convert.ToBase64String(v.Data) })
        };
        if (v.VisualQualifier is not null)
            obj["VisualQualifier"] = SerialiseQualifier(v.VisualQualifier);

        return Encode(obj);
    }

    public static BasicVisualObject ToVisual(byte[] portData)
    {
        var root = Parse(portData);
        var data = FirstInlineData(root, "BasicVisualObjectData");
        var bytes = data is null ? Array.Empty<byte>() : Convert.FromBase64String(data);
        var id    = root["BasicVisualObjectID"]?.GetValue<string>() ?? Guid.NewGuid().ToString();

        // We reconstruct the internal object via FromFile (which also builds a
        // qualifier from the file name); for a wire-received object we have no
        // file name, so pass an empty name and rely on the bytes.
        return new BasicVisualObject
        {
            BasicVisualObjectID = id,
            FileName            = root["FileName"]?.GetValue<string>(),
            Data                = bytes
        };
    }

    // ── Text ─────────────────────────────────────────────────────────────────
    // OSD-BTO-V1.5 (strict, additionalProperties:false):
    //   { Header, BasicTextObjectID, BasicTextData:[{Data}], TextQualifier? }
    public static byte[] FromText(BasicTextObject t)
    {
        var obj = new JsonObject
        {
            ["Header"]            = "OSD-BTO-V1.5",
            ["BasicTextObjectID"] = string.IsNullOrEmpty(t.BasicTextObjectID)
                                      ? Guid.NewGuid().ToString() : t.BasicTextObjectID,
            ["BasicTextData"]     = new JsonArray(
                new JsonObject { ["Data"] = t.GetText() })
        };
        if (t.TextQualifier is not null)
            obj["TextQualifier"] = SerialiseQualifier(t.TextQualifier);

        return Encode(obj);
    }

    public static BasicTextObject ToText(byte[] portData)
    {
        var root = Parse(portData);
        var text = FirstInlineData(root, "BasicTextData") ?? string.Empty;
        return BasicTextObject.FromText(text);
    }

    // ── Audio ────────────────────────────────────────────────────────────────
    // OSD-BAO-V1.5: { Header, BasicAudioObjectID, BasicAudioObjectData:[{Data}],
    //                 AudioQualifier? }.  Internal audio Data is already base64.
    public static byte[] FromAudio(BasicAudioObject a)
    {
        var obj = new JsonObject
        {
            ["Header"]            = "OSD-BAO-V1.5",
            ["BasicAudioObjectID"]= string.IsNullOrEmpty(a.BasicAudioObjectID)
                                      ? Guid.NewGuid().ToString() : a.BasicAudioObjectID,
            ["BasicAudioObjectData"] = new JsonArray(
                new JsonObject { ["Data"] = Convert.ToBase64String(a.Data) })
        };
        if (a.AudioQualifier is not null)
            obj["AudioQualifier"] = SerialiseQualifier(a.AudioQualifier);

        return Encode(obj);
    }

    public static BasicAudioObject ToAudio(byte[] portData)
    {
        var root  = Parse(portData);
        var data  = FirstInlineData(root, "BasicAudioObjectData");
        var bytes = data is null ? Array.Empty<byte>() : Convert.FromBase64String(data);
        return BasicAudioObject.FromData(bytes);
    }

    // ── helpers ────────────────────────────────────────────────────────────
    private static byte[] Encode(JsonObject obj) =>
        Encoding.UTF8.GetBytes(obj.ToJsonString(JsonOpts));

    private static JsonObject Parse(byte[] portData)
    {
        var text = Encoding.UTF8.GetString(portData);
        return JsonNode.Parse(text)?.AsObject()
            ?? throw new FormatException("port-data is not a JSON object.");
    }

    // Returns the first inline { "Data": "..." } string from a "...Data" array.
    private static string? FirstInlineData(JsonObject root, string arrayName)
    {
        if (root[arrayName] is not JsonArray arr) return null;
        foreach (var item in arr)
        {
            if (item is JsonObject o && o["Data"] is JsonValue val &&
                val.TryGetValue<string>(out var s))
                return s;
        }
        return null;
    }

    // Qualifiers are serialised with the same options as the rest; they are
    // carried through opaquely (round-tripped as JSON) rather than re-modelled,
    // which is sufficient for the demo. Full schema validation is a later pass.
    private static JsonNode? SerialiseQualifier(object qualifier)
    {
        var json = JsonSerializer.Serialize(qualifier, qualifier.GetType(), JsonOpts);
        return JsonNode.Parse(json);
    }
}
