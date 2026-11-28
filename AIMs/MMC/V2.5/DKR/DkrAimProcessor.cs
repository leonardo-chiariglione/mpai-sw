using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using AIF.Controller;

using Mpai.Core;

namespace Mpai.Mmc.Dkr;

// MMC-DKR-V2.5 - Domain Knowledge Retrieval (2026-11-27).
//
// What an application knows of its domain reaches the dialogue of a machine without the dialogue knowing the application: the application gives its Domain Knowledge
// (MMC-DKN: items of text, some naming something it has - a place, an instrument - with the Reference by which it identifies it), on its Port or as a file of the Access of the
// implementation (Setting DomainKnowledge); for each request (a Text Object) this AIM gives the items relevant to it, each with its Relevance, as a Domain Knowledge: what
// Entity Dialogue Processing (MMC-EDP) and JSON Interface (MMC-JSI) are told of the domain. The standard says what goes in and out, not how relevance is found.
//
// TWO LAYERS (2026-11-28): the Domain Knowledge of the Access (Setting DomainKnowledge) is that of the Domain Type - for a CAV, what CAV-TEC says a passenger can ask and the
// terms of the domain; the Domain Knowledge received on the Port is that of the Domain Instance - the places of this CAV, from its Offline Map. Both are searched together;
// the latest received replaces the previous one received, never the one of the Access.
//
// ENGINE (first version, no model): BM25 over the words of the items (their Text, Name and Synonyms); an item whose whole Name, or one of whose Synonyms, the request says is
// relevant whatever its score - the vocabulary of the domain, with its synonyms and variations, is what finds what the human means. Setting Items: how many at most (default 6).
public sealed class DkrAimProcessor : IAimProcessor
{
    private readonly string _requestIn, _knowledgeIn, _knowledgeOut;
    private readonly int _most;
    private readonly JsonObject? _type;      // the Domain Type layer (Access)
    private JsonObject? _instance;           // the Domain Instance layer (Port)

    public DkrAimProcessor(string instanceId, AimPortReader ports, IReadOnlyDictionary<string, string>? settings = null)
    {
        InstanceId    = instanceId;
        _requestIn    = ports.Input("OSD-BTO-V1.5");
        _knowledgeIn  = ports.InputOrDefault("MMC-DKN-V2.5", "");
        _knowledgeOut = ports.Output("MMC-DKN-V2.5");
        _most = settings is not null && settings.TryGetValue("Items", out var n) && int.TryParse(n, out var k) && k > 0 ? k : 6;
        if (settings is not null && settings.TryGetValue("DomainKnowledge", out var file) && File.Exists(file))
            _type = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
    }

    public string InstanceId { get; }

    public Task<Message> ProcessAsync(Message message)
    {
        if (_knowledgeIn.Length > 0 && message.Ports.TryGetValue(_knowledgeIn, out var given) && !string.IsNullOrWhiteSpace(given))
            _instance = JsonNode.Parse(given) as JsonObject;
        if (!message.Ports.TryGetValue(_requestIn, out var json) || string.IsNullOrWhiteSpace(json))
            return Task.FromResult(new Message { MessageId = message.MessageId, MessageType = message.MessageType, Ports = new Dictionary<string, string>() });   // the knowledge only: kept
        var request = MpaiJson.FromJson<BasicTextObject>(json)?.GetText() ?? "";
        var found = Retrieve(Merge(_type, _instance), request, _most);
        return Task.FromResult(new Message
        {
            MessageId = message.MessageId, MessageType = message.MessageType,
            Ports = new Dictionary<string, string> { [_knowledgeOut] = found.ToJsonString() }
        });
    }

    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "to", "of", "in", "on", "at", "for", "and", "or", "is", "are", "be", "me", "my", "i", "you", "your", "it", "this", "that", "please", "can", "could",
        "would", "will", "do", "does", "what", "which", "where", "how", "with", "by", "from", "we", "us", "let", "lets", "go", "take"
    };
    public static List<string> Words(string text) =>
        Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value).Where(w => !Stop.Contains(w)).ToList();

    // The two layers as one Domain Knowledge: the items of both, the Domain of both.
    public static JsonObject? Merge(JsonObject? type, JsonObject? instance)
    {
        if (type is null || instance is null) return instance ?? type;
        var items = new JsonArray();
        foreach (var layer in new[] { type, instance })
            foreach (var i in layer["Items"] as JsonArray ?? []) items.Add(i!.DeepClone());
        var domain = string.Join(" ", new[] { (string?)type["Domain"], (string?)instance["Domain"] }.Where(d => !string.IsNullOrWhiteSpace(d)));
        var merged = new JsonObject { ["Header"] = "MMC-DKN-V2.5", ["DomainKnowledgeID"] = $"{(string?)type["DomainKnowledgeID"]}+{(string?)instance["DomainKnowledgeID"]}", ["Items"] = items };
        if (domain.Length > 0) merged["Domain"] = domain;
        return merged;
    }

    private static string Phrase(string text) => " " + string.Join(" ", Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}]+").Select(m => m.Value)) + " ";
    private static IEnumerable<string> Synonyms(JsonObject item) => (item["Synonyms"] as JsonArray ?? []).Select(s => (string?)s ?? "").Where(s => s.Length > 0);

    // The items of the knowledge relevant to the request, most relevant first, at most `most` of them; Relevance 1 for an item the request names (its Name or one of its
    // Synonyms said), at most 0.9 for the others: the items of Relevance 1 are what the request selects of the application's knowledge (M3328 3.7).
    public static JsonObject Retrieve(JsonObject? knowledge, string request, int most = 6)
    {
        var items = (knowledge?["Items"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        var docs = items.Select(i => Words($"{(string?)i["Name"]} {string.Join(" ", Synonyms(i))} {(string?)i["Text"]}")).ToList();
        var query = Words(request).Distinct().ToList();
        double avg = docs.Count == 0 ? 1 : Math.Max(1, docs.Average(d => d.Count));
        double Idf(string w) { var n = docs.Count(d => d.Contains(w)); return Math.Log(1 + (docs.Count - n + 0.5) / (n + 0.5)); }
        const double k1 = 1.2, b = 0.75;
        var said = Phrase(request);
        var scored = items.Select((item, i) =>
        {
            var d = docs[i];
            var score = query.Sum(w => { var f = d.Count(x => x == w); return f == 0 ? 0 : Idf(w) * f * (k1 + 1) / (f + k1 * (1 - b + b * d.Count / avg)); });
            var name = (string?)item["Name"];
            var named = new[] { name ?? "" }.Concat(Synonyms(item)).Where(n => n.Length > 0).Any(n => Phrase(n).Trim().Length > 0 && said.Contains(Phrase(n)));
            return (Item: item, Score: score + (named ? 100 : 0), Named: named);
        }).Where(x => x.Score > 0).OrderByDescending(x => x.Score).Take(most).ToList();
        var best = scored.Count == 0 ? 1 : scored[0].Score;
        var result = new JsonObject
        {
            ["Header"] = "MMC-DKN-V2.5", ["DomainKnowledgeID"] = $"DKN-{Guid.NewGuid():N}",
            ["Items"] = new JsonArray(scored.Select(x =>
            {
                var o = (JsonObject)x.Item.DeepClone();
                o["Relevance"] = x.Named ? 1.0 : Math.Round(Math.Min(0.9, x.Score / best), 3);      // 1: named by the request (its Name or a Synonym said); what an application acts on
                return (JsonNode)o;
            }).ToArray())
        };
        if (knowledge?["Domain"] is JsonNode domain) result["Domain"] = domain.DeepClone();
        return result;
    }
}
