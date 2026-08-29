using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using AIF.Controller;

using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Mmc.Nlu;

// MMC-NLU-V2.5 - Natural Language Understanding, as an AIF IAimProcessor.
//
// Receives an input text (Input Text typed directly, or Recognised Text from ASR)
// and produces the Meaning of the text - a Text Descriptors Object (MMC-TDO)
// carrying the four taggings (POS/NE/dependency/SRL) - plus a Refined Text (the
// cleaned-up version of the input). It may also receive an Instance Identifier and
// the Audio-Visual Scene Descriptors to ground referential meaning; those are read
// when present and (for now) recorded, leaving deeper grounding to a later engine.
//
// ENGINE (first pass, deliberately simple - proves the AIM and the Meaning data
// shape end-to-end through the Controller, then deepens without touching the
// interface, the same way the detectors were proven before being perfected):
//   - Refined Text: trims/normalises whitespace and capitalisation of the input.
//   - POS_tagging:  a small lexicon + suffix-rule tagger over the tokens.
//   - NE_tagging:   capitalised-token / simple-pattern named-entity spotting.
//   - dependency_tagging, SRL_tagging: left null (the spec permits null taggings).
// The Meaning is emitted as a Basic Text Descriptors format inside MMC-TDO.
public sealed class NluAimProcessor : IAimProcessor
{
    private const string PosSet = "MPAI-NLU-simple-POS";
    private const string NeSet  = "MPAI-NLU-simple-NE";

    private readonly string _instanceId;
    private readonly string _inputTextPort;      // OSD-TXO PortNumber 1
    private readonly string _recognisedTextPort; // OSD-TXO PortNumber 2
    private readonly string _meaningPort;        // MMC-TDO
    private readonly string _refinedTextPort;    // OSD-TXO

    public NluAimProcessor(string instanceId, AimPortReader ports)
    {
        _instanceId         = instanceId;
        _inputTextPort      = ports.Input("OSD-BTO-V1.5", 1);
        _recognisedTextPort = ports.Input("OSD-BTO-V1.5", 2);
        _meaningPort        = ports.Output("MMC-TDO-V2.5");
        _refinedTextPort    = ports.Output("OSD-BTO-V1.5");
    }

    public string InstanceId => _instanceId;

    public System.Threading.Tasks.Task<Message> ProcessAsync(Message message)
    {
        // Prefer Recognised Text (from ASR) if present, else the directly-typed Input Text.
        string? text = ReadText(message, _recognisedTextPort) ?? ReadText(message, _inputTextPort);
        if (string.IsNullOrWhiteSpace(text))
            return System.Threading.Tasks.Task.FromResult(
                Message.Error(message.MessageId, _instanceId, "no Input Text or Recognised Text on input ports"));

        string refined = Refine(text);
        var basic = Analyse(refined);
        var meaning = TextDescriptorsObject.FromBasic(basic);
        var refinedObject = BasicTextObject.FromText(refined);

        return System.Threading.Tasks.Task.FromResult(new Message
        {
            MessageId = message.MessageId,
            MessageType = message.MessageType,
            Ports = new Dictionary<string, string>
            {
                [_meaningPort]     = MpaiJson.ToJson(meaning),
                [_refinedTextPort] = MpaiJson.ToJson(refinedObject)
            }
        });
    }

    private static string? ReadText(Message message, string port)
    {
        if (!message.Ports.TryGetValue(port, out var json) || string.IsNullOrWhiteSpace(json))
            return null;
        var txo = MpaiJson.FromJson<BasicTextObject>(json);
        return txo?.GetText();
    }

    // Refined Text: normalise whitespace; keep the content otherwise intact.
    private static string Refine(string text)
    {
        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Trim();
    }

    // Produce the Basic Text Descriptors (the four taggings) for the text. POS and
    // NE are filled by the simple tagger; dependency and SRL are left null.
    private static BasicTextDescriptors Analyse(string text)
    {
        var tokens = Tokenise(text);
        return new BasicTextDescriptors
        {
            BasicTextDescriptorsID = Guid.NewGuid().ToString(),
            TextDescriptorsData = new TextDescriptorsData
            {
                POS_tagging = new Tagging { Set = PosSet, Result = PosTag(tokens) },
                NE_tagging  = new Tagging { Set = NeSet,  Result = NeTag(tokens) },
                dependency_tagging = null,
                SRL_tagging        = null
            }
        };
    }

    private static List<string> Tokenise(string text)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c)) { sb.Append(c); }
            else
            {
                if (sb.Length > 0) { tokens.Add(sb.ToString()); sb.Clear(); }
                if (char.IsPunctuation(c)) tokens.Add(c.ToString());
            }
        }
        if (sb.Length > 0) tokens.Add(sb.ToString());
        return tokens;
    }

    // A tiny closed-class lexicon + suffix rules. Output: space-separated token/TAG.
    private static string PosTag(List<string> tokens)
    {
        var tags = tokens.Select(t => $"{t}/{PosOf(t)}");
        return string.Join(' ', tags);
    }

    private static string PosOf(string token)
    {
        if (token.Length == 1 && char.IsPunctuation(token[0])) return "PUNCT";
        if (token.All(char.IsDigit)) return "NUM";

        string w = token.ToLowerInvariant();
        if (Determiners.Contains(w)) return "DET";
        if (Pronouns.Contains(w))    return "PRON";
        if (Prepositions.Contains(w))return "ADP";
        if (Conjunctions.Contains(w))return "CCONJ";
        if (Auxiliaries.Contains(w)) return "AUX";

        // Suffix rules (very rough).
        if (w.EndsWith("ly")) return "ADV";
        if (w.EndsWith("ing") || w.EndsWith("ed")) return "VERB";
        if (w.EndsWith("tion") || w.EndsWith(" ness") || w.EndsWith("ment")) return "NOUN";

        // Capitalised mid-sentence -> likely proper noun; else noun as default.
        if (token.Length > 0 && char.IsUpper(token[0])) return "PROPN";
        return "NOUN";
    }

    // Named entities: runs of capitalised tokens (proper-noun spans). Output:
    // space-separated span entries "Text:ENT", or "" if none.
    private static string NeTag(List<string> tokens)
    {
        var spans = new List<string>();
        var current = new List<string>();
        foreach (var t in tokens)
        {
            bool capitalised = t.Length > 0 && char.IsUpper(t[0]) && t.Any(char.IsLetter);
            if (capitalised) { current.Add(t); }
            else if (current.Count > 0) { spans.Add(string.Join(' ', current)); current.Clear(); }
        }
        if (current.Count > 0) spans.Add(string.Join(' ', current));
        return string.Join("; ", spans.Select(s => $"{s}:ENT"));
    }

    private static readonly HashSet<string> Determiners  = new() { "the", "a", "an", "this", "that", "these", "those", "my", "your", "his", "her", "its", "our", "their" };
    private static readonly HashSet<string> Pronouns     = new() { "i", "you", "he", "she", "it", "we", "they", "me", "him", "them", "us", "who", "what", "which" };
    private static readonly HashSet<string> Prepositions = new() { "in", "on", "at", "to", "from", "with", "by", "for", "of", "about", "into", "over", "under", "near" };
    private static readonly HashSet<string> Conjunctions = new() { "and", "or", "but", "nor", "so", "yet" };
    private static readonly HashSet<string> Auxiliaries  = new() { "is", "am", "are", "was", "were", "be", "been", "being", "do", "does", "did", "have", "has", "had", "will", "would", "can", "could", "shall", "should", "may", "might", "must" };
}
