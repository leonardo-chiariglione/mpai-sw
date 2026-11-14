using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Mpai.Core;

namespace Mpai.Aif.PortData;

// OSD-SEL-V1.5 - Selector, on the wire as OSD/V1.5/data/Selector.json describes it.
//
// INSIDE, the Selector is BasicSelectorObject: InputLanguage and OutputLanguage
// side by side, and TranslateFrom naming which text to translate. ON THE WIRE the
// schema nests the two languages in a Language object, which requires both, and
// states the medium of the input as InputMedia (Text or Speech). This is the
// only place the two forms meet.
//
// NOTHING OF THE SCHEMA INSTANCE IS LOST. A Selector written as an instance of its schema - by a workflow (AIF V3.0, Workflow Description, 3), or by an AIM
// that reads it that way (CAE-USC reads Language.OutputLanguage) - keeps every element it has (SelectorID, MediaOrDescriptors, ...) in both directions; the
// side-by-side languages of BasicSelectorObject are added inside for the AIMs that read them, and never put on the wire.
public sealed class SelectorCodec : IPortDataCodec
{
    public string DataType => "OSD-SEL-V1.5";

    private static readonly JsonSerializerOptions WireOptions = new() { WriteIndented = false };

    // The elements of the inside form that the schema does not have.
    private static readonly string[] InsideOnly = ["InputLanguage", "OutputLanguage", "TranslateFrom"];

    public byte[] ToWire(string internalJson)
    {
        var inside = JsonNode.Parse(internalJson) as JsonObject ?? throw new FormatException("Selector is empty.");
        var selector = MpaiJson.FromJson<BasicSelectorObject>(internalJson) ?? throw new FormatException("Selector is empty.");

        // everything of the schema the Selector already carries, as it is
        var wire = new JsonObject { ["Header"] = "OSD-SEL-V1.5" };
        foreach (var (key, value) in inside)
            if (key != "Header" && Array.IndexOf(InsideOnly, key) < 0) wire[key] = value?.DeepClone();

        // The schema's Language requires both languages; a Selector that names
        // only one carries no Language object rather than an invalid one.
        if (wire["Language"] is null &&
            !string.IsNullOrWhiteSpace(selector.InputLanguage) &&
            !string.IsNullOrWhiteSpace(selector.OutputLanguage))
            wire["Language"] = new JsonObject
            {
                ["InputLanguage"]  = selector.InputLanguage,
                ["OutputLanguage"] = selector.OutputLanguage
            };

        if (wire["InputMedia"] is null && selector.TranslateFrom is { } from)
            wire["InputMedia"] = from == TextSource.RecognisedText ? "Speech" : "Text";

        return Encoding.UTF8.GetBytes(wire.ToJsonString(WireOptions));
    }

    public string ToInternal(byte[] wire)
    {
        var root = JsonNode.Parse(Encoding.UTF8.GetString(wire)) as JsonObject
                   ?? throw new FormatException("Selector port-data is not a JSON object.");

        var language = root["Language"] as JsonObject;
        var media    = (string?)root["InputMedia"];

        var inside = JsonNode.Parse(MpaiJson.ToJson(new BasicSelectorObject
        {
            InputLanguage  = (string?)language?["InputLanguage"],
            OutputLanguage = (string?)language?["OutputLanguage"],
            TranslateFrom  = media switch
            {
                "Speech" => TextSource.RecognisedText,
                "Text"   => TextSource.InputText,
                _        => null
            }
        }))!.AsObject();
        foreach (var (key, value) in root)
            if (!inside.ContainsKey(key)) inside[key] = value?.DeepClone();
        return inside.ToJsonString(WireOptions);
    }
}
