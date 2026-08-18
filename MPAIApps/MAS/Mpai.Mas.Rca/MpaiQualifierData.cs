using System;
using System.Text.Json;
using System.Text.Json.Nodes;

using Mpai.Core;

namespace Mpai.Mas.Rca;

// Qualifiers, coming back off the wire.
//
// MpaiPortData WRITES qualifiers - SerialiseQualifier is a plain
// JsonSerializer.Serialize - but ToSpeech and ToText DISCARD them, rebuilding the
// object from its bytes alone. For AMQ that lost nothing it needed. For TST it
// loses the input language: MMC-ASR reads the language from the Speech
// Qualifier, so speech arriving at the server unqualified was recognised with
// whatever language the server had configured. German limped through as English;
// Japanese had no chance.
//
// This is the missing inverse. It is a separate class for the same reason
// MpaiSelectorData is: MpaiPortData is a static class compiled by two
// applications, and widening it would touch both.
public static class MpaiQualifierData
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static SpeechQualifier? SpeechQualifierFrom(byte[] portData) =>
        Deserialise<SpeechQualifier>(portData, "SpeechQualifier");

    public static TextQualifier? TextQualifierFrom(byte[] portData) =>
        Deserialise<TextQualifier>(portData, "TextQualifier");

    // A qualifier that will not parse is not worth failing an exchange over: the
    // object still carries its data, and the AIMs already cope with a missing
    // qualifier. Losing the language is a degradation, not a fault.
    private static T? Deserialise<T>(byte[] portData, string property) where T : class
    {
        try
        {
            var root = JsonNode.Parse(System.Text.Encoding.UTF8.GetString(portData))?.AsObject();
            var node = root?[property];

            return node is null ? null : node.Deserialize<T>(JsonOpts);
        }
        catch
        {
            return null;
        }
    }
}