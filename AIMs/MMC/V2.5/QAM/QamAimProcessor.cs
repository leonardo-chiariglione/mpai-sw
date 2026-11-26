using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using AIF.Controller;

using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Mmc.Qam;

// MMC-QAM-V2.5 - Question Analysis Module (2026-11-25, N-50 e, for the composite MMC-MQA).
//
// The web specification: it takes the Meaning of the question (the Text Descriptors Object) and gives its Intention (MMC-INT): what is asked - the topic (qtopic), the focus
// (qfocus), the lexical answer type (qLAT), the semantic answer type (qSAT) and the domain (qdomain). The standard says what the Intention holds, not how it is found.
//
// ENGINE (first version, rules, no model): the words come from the POS tagging of the Text Descriptors (what NLU gives: "token/TAG ..."). The question word gives the
// semantic answer type (what -> entity, who -> person, where -> location, when -> time, how many/much -> quantity, why -> reason, how -> manner, a question that starts with
// a verb -> yes/no); the first noun after the question word, or "it" when the question points at the object shown, is the focus; the lexical answer type is the noun that
// says what kind of thing is asked for ("what colour" -> colour); the topic is the last noun of the question; the domain is "visual object" when the question is about the
// thing shown (it, this, that, the object, the picture), else "general".
public sealed class QamAimProcessor : IAimProcessor
{
    private readonly string _tdoIn, _intentionOut;

    public QamAimProcessor(string instanceId, AimPortReader ports)
    {
        InstanceId    = instanceId;
        _tdoIn        = ports.Input("MMC-TDO-V2.5");
        _intentionOut = ports.Output("MMC-INT-V2.5");
    }

    public string InstanceId { get; }

    public Task<Message> ProcessAsync(Message message)
    {
        if (!message.Ports.TryGetValue(_tdoIn, out var json) || string.IsNullOrWhiteSpace(json))
            return Task.FromResult(Message.Error(message.MessageId, InstanceId, "no Text Descriptors Object on the input Port"));
        var tokens = Tokens(json);
        var intention = Analyse(tokens);
        return Task.FromResult(new Message
        {
            MessageId = message.MessageId, MessageType = message.MessageType,
            Ports = new Dictionary<string, string> { [_intentionOut] = intention.ToJsonString() }
        });
    }

    // The tokens and their tags, from the POS tagging the Text Descriptors carry.
    public static List<(string Word, string Tag)> Tokens(string tdoJson)
    {
        var result = new List<(string, string)>();
        try
        {
            var tdo = MpaiJson.FromJson<TextDescriptorsObject>(tdoJson);
            foreach (var item in tdo?.TextDescriptorsData ?? [])
            {
                if (string.IsNullOrWhiteSpace(item.Data)) continue;
                var pos = (string?)JsonNode.Parse(item.Data)?["TextDescriptorsData"]?["POS_tagging"]?["Result"];
                foreach (var t in (pos ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var slash = t.LastIndexOf('/');
                    result.Add(slash > 0 ? (t[..slash], t[(slash + 1)..]) : (t, ""));
                }
            }
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException) { }
        return result;
    }

    private static readonly string[] Pointing = ["it", "this", "that", "object", "thing", "picture", "image", "photo"];
    private static readonly string[] Verbs = ["is", "are", "was", "were", "do", "does", "did", "can", "could", "will", "would", "has", "have", "should"];

    public static JsonObject Analyse(IReadOnlyList<(string Word, string Tag)> tokens)
    {
        var words = tokens.Select(t => t.Word.ToLowerInvariant().Trim('?', '.', ',', '!')).Where(w => w.Length > 0).ToList();
        string Sat()
        {
            var first = words.FirstOrDefault() ?? "";
            var second = words.Skip(1).FirstOrDefault() ?? "";
            return first switch
            {
                "who" or "whom" or "whose" => "person",
                "where" => "location",
                "when" => "time",
                "why" => "reason",
                "how" when second is "many" or "much" or "old" or "big" or "long" or "tall" => "quantity",
                "how" => "manner",
                "what" or "which" => "entity",
                _ when Verbs.Contains(first) => "yes/no",
                _ => "entity"
            };
        }
        bool Noun(int i) => i < tokens.Count && (tokens[i].Tag.StartsWith("N", StringComparison.Ordinal) || tokens[i].Tag == "PRP" && Pointing.Contains(words.ElementAtOrDefault(i)));
        var nouns = Enumerable.Range(0, tokens.Count).Where(i => i < words.Count && (Noun(i) || Pointing.Contains(words[i]))).Select(i => words[i]).ToList();
        var lat = words.Count > 1 && words[0] is "what" or "which" && words.Count > 1 && !Verbs.Contains(words[1]) ? words[1] : "";
        var focus = nouns.FirstOrDefault(n => n != lat) ?? "it";
        var topic = nouns.LastOrDefault() ?? focus;
        var domain = words.Any(w => Pointing.Contains(w)) ? "visual object" : "general";
        return new JsonObject
        {
            ["Header"] = "MMC-INT-V2.5",
            ["IntentionID"] = Guid.NewGuid().ToString(),
            ["Intention"] = new JsonArray(new JsonObject { ["qtopic"] = topic, ["qfocus"] = focus, ["qLAT"] = lat, ["qSAT"] = Sat(), ["qdomain"] = domain })
        };
    }
}

public sealed class QamPlugin : IAimPlugin
{
    public string AimName => "MMC-QAM-V2.5";
    public IAimProcessor Create(AimPortReader ports, IReadOnlyDictionary<string, string> settings) => new QamAimProcessor(AimName, ports);
}
