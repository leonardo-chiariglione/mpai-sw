using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Mpai.Core;

namespace Mpai.Mas.Rca;

// The Selector on the MAS wire.
//
// MpaiPortData covers Visual, Text, Audio and Speech but not OSD-SEL-V1.5,
// because AMQ has no Selector: it was MMC-TST that introduced one. Written as a
// separate class because MpaiPortData is a static class rather than a partial
// one, and making it partial would mean editing a file two applications compile.
//
// Wire form follows the same pattern as the rest: one JSON document, Header
// naming the Data Type, and the fields inline.
public static class MpaiSelectorData
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static byte[] FromSelector(BasicSelectorObject selector)
    {
        var obj = new JsonObject
        {
            ["Header"] = "OSD-SEL-V1.5"
        };

        if (selector.InputLanguage  is not null) obj["InputLanguage"]  = selector.InputLanguage;
        if (selector.OutputLanguage is not null) obj["OutputLanguage"] = selector.OutputLanguage;
        if (selector.TranslateFrom  is not null) obj["TranslateFrom"]  = selector.TranslateFrom.ToString();

        // No PreserveSpeechFeatures here. It belongs to the Selector of the
        // WITH-DESCRIPTORS design, and TST dropped descriptors: serialising a
        // field the object does not have was a straight mistake, caught by the
        // compiler rather than by anything at run time.
        return Encoding.UTF8.GetBytes(obj.ToJsonString(JsonOpts));
    }

    public static BasicSelectorObject ToSelector(byte[] portData)
    {
        var root = JsonNode.Parse(Encoding.UTF8.GetString(portData))?.AsObject()
            ?? throw new FormatException("port-data is not a JSON object.");

        TextSource? translateFrom = null;
        var named = root["TranslateFrom"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(named) &&
            Enum.TryParse<TextSource>(named, ignoreCase: true, out var parsed))
        {
            translateFrom = parsed;
        }

        return new BasicSelectorObject
        {
            InputLanguage  = root["InputLanguage"]?.GetValue<string>(),
            OutputLanguage = root["OutputLanguage"]?.GetValue<string>(),
            TranslateFrom  = translateFrom
        };
    }
}