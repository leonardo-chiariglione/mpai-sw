using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using AIF.Controller;

namespace Mpai.Pgm;

// PGM-BKN-V1.0 - Basic Knowledge. The one AIM of the A-User that is not a composite: it consults its partners (Prompt Creation, Domain Access, User State Refinement, Personality
// Alignment) in a turn of queries and responses, then asks the language model, and gives its Final Response to A-User Formation ONLY (never to A-User Control, never to the
// M-Instance); to A-User Control it gives the response to its Request, which names the Final Response.
//
// The language model is the AIM's configuration, not its code: the model name is a setting of the implementation.
public sealed class BknAim : PgmAim
{
    public const string PersonaSetting = "Persona.ItemId";

    private readonly string _request, _prompt, _prcIn, _dacIn, _usrIn, _palIn, _final, _response, _prcOut, _dacOut, _usrOut, _palOut;
    private readonly ILanguageModel _model;
    private readonly string _persona;

    private JsonObject? _pendingRequest, _pendingPrompt;
    private bool _consulting;
    private string _dialog = "";
    private readonly Dictionary<string, JsonObject> _answers = new();

    public BknAim(string instanceId, AimPortReader ports, ILanguageModel model, string persona = "a-user-persona") : base(instanceId, ports)
    {
        _model = model; _persona = persona;
        _request = In(Auc.Bkn); _prompt = In("PGM-PCP-V1.0"); _prcIn = In("PGM-KPR-V1.0"); _dacIn = In("PGM-KDA-V1.0"); _usrIn = In("PGM-KUS-V1.0"); _palIn = In("PGM-KPA-V1.0");
        _final = Out("PGM-FRS-V1.0"); _response = Out(Auc.Bkn); _prcOut = Out("PGM-KPR-V1.0"); _dacOut = Out("PGM-KDA-V1.0"); _usrOut = Out("PGM-KUS-V1.0"); _palOut = Out("PGM-KPA-V1.0");
    }

    public override async Task<Message> ProcessAsync(Message message)
    {
        if (Read(message, _request) is { } rq && Auc.IsRequest(rq)) _pendingRequest = rq;
        if (Read(message, _prompt) is { } pcp) _pendingPrompt = pcp;
        foreach (var (key, header) in new[] { (_prcIn, "PGM-KPR-V1.0"), (_dacIn, "PGM-KDA-V1.0"), (_usrIn, "PGM-KUS-V1.0"), (_palIn, "PGM-KPA-V1.0") })
            if (Read(message, key) is { } a && (string?)a["Mode"] == "RS") _answers[header] = a;

        var outputs = new List<(string, JsonNode?)>();

        // Turn 1: the request and the prompt are here - ask the four partners.
        if (!_consulting && _pendingRequest is not null && _pendingPrompt is not null)
        {
            _consulting = true;
            var session = Session.Of(_pendingRequest);
            _dialog = (string?)_pendingPrompt["DialogID"] ?? Auc.IdOf(_pendingRequest);
            var q = Knowledge.Query("PGM-KPR-V1.0", session, _dialog, 1); q["Kind"] = "action_context"; q["Question"] = "What may the A-User do, and how many steps?";
            outputs.Add((_prcOut, q));
            var d = Knowledge.Query("PGM-KDA-V1.0", session, _dialog, 1); d["Kind"] = "feasibility"; d["Question"] = "What can the A-User do in this domain?";
            outputs.Add((_dacOut, d));
            outputs.Add((_usrOut, Knowledge.Query("PGM-KUS-V1.0", session, _dialog, 1)));
            outputs.Add((_palOut, Knowledge.Query("PGM-KPA-V1.0", session, _dialog, 1)));
        }

        // Turn 2: the four answers are here - the model answers.
        if (_consulting && _answers.Count == 4 && _pendingRequest is not null && _pendingPrompt is not null)
        {
            var request = _pendingRequest; var prompt = _pendingPrompt;
            _pendingRequest = null; _pendingPrompt = null; _consulting = false; _answers.Clear();
            outputs.AddRange(await Conclude(request, prompt));
        }
        return Give(message, outputs);
    }


    // ---- the Communicative Functions (M3308 3.5) -----------------------------------------------------------------------------------------------------

    // The highest Level this Basic Knowledge produces (a setting of the implementation, Bkn.Level; 1 by default, as M3308 3.6 proposes for the models of the reference software).
    public const string LevelSetting = "Bkn.Level";
    public int LevelDeclared { get; init; } = 1;

    private async Task<List<(string, JsonNode?)>> Conclude(JsonObject request, JsonObject prompt)
    {
        var session = Session.Of(request);
        var allowed = (request["Actions"]?["Allowed"] as JsonArray)?.Select(x => (string?)x).Where(x => x is not null).Select(x => x!).ToArray();
        if (allowed is not { Length: > 0 }) allowed = ["MU-Actuate", "MM-Animate"];
        var said = (string?)prompt["Prompt"] ?? "";

        // The Level: what A-User Control requested, no higher than this AIM declares; the Functions: those A-User Control allows that the Persona can perform (PC Prompt).
        var levelRequested = (int?)request["Actions"]?["FunctionLevel"] ?? 0;
        var level = Math.Min(levelRequested, LevelDeclared);
        var context = prompt["ActionContext"] as JsonObject;
        static string[] Strings(JsonNode? n) => (n as JsonArray)?.Select(x => (string?)x).Where(x => x is not null).Select(x => x!).ToArray() ?? [];
        var functionsAllowed = Strings(request["Actions"]?["Functions"]);
        if (functionsAllowed.Length == 0) functionsAllowed = Communicative.Functions;
        var personaFunctions = Strings(context?["Functions"]);
        var functions = personaFunctions.Length > 0 ? functionsAllowed.Intersect(personaFunctions).ToArray() : functionsAllowed;
        var personaBehaviours = Strings(context?["Behaviours"]);
        var referents = (prompt["ResolvedReferents"] as JsonArray)?.Select(r => (string?)r?["EntityID"]).Where(x => x is not null).Select(x => x!) ?? [];
        var items = Strings(context?["Items"]).Concat(referents).Distinct().ToArray();
        var places = Strings(context?["MLocations"]);
        if (functions.Length == 0) level = 0;

        string reply; var worldActions = new List<(string Verb, string Target)>(); var moves = new List<string>();
        var produced = new JsonArray();
        var failure = "";
        try
        {
            var system =
                "You are the A-User, an autonomous assistant of a responsible human in a metaverse building. " +
                "'reply' is what you say aloud to the human: one or two short natural sentences, never a command name. " +
                "If the human asks you to do something to an item of the building (for instance to open doors), add one entry to worldActions: verb 'MU-Actuate', target the item (for instance 'doors'). " +
                "If you need to go somewhere in the building yourself, put the place in personaMoves (for instance 'doors'). Otherwise leave the lists empty. ";
            if (level >= 1)
                system +=
                    "While you speak, your body can express communicative functions. In 'functions' list the ones that suit your reply (usually one to three): " +
                    "'function' is one of: " + string.Join("; ", functions.Select(f => $"{f} ({Communicative.Meaning[f]})")) + ". " +
                    "'words' are the exact words of your reply that the function goes with (copy them from the reply). " +
                    "'target' is 'User' (the human) or one of the items: " + string.Join(", ", items.Concat(places)) + " - or 'none'. " +
                    "'intensity' is from 0 (slight) to 1 (very marked). 'required' is true if the function is essential to what you mean. " +
                    (level >= 2 ? "'behaviour' is the movement that realises the function, chosen among those allowed for it: " +
                        string.Join("; ", functions.Select(f => $"{f}: {string.Join(", ", Communicative.BehavioursOf[f].Where(b => personaBehaviours.Length == 0 || personaBehaviours.Contains(b)))}")) + ". " : "");
            system +=
                "Example. Human: 'please close the window'. You: {\"reply\":\"Closing the window now.\",\"worldActions\":[{\"verb\":\"MU-Actuate\",\"target\":\"window\"}],\"personaMoves\":[]" +
                (level >= 1 ? ",\"functions\":[{\"function\":\"Deictic\",\"words\":\"the window\",\"target\":\"window\",\"intensity\":0.6,\"required\":true" + (level >= 2 ? ",\"behaviour\":\"Point\"" : "") + "}]" : "") + "}. " +
                "Example. Human: 'how are you'. You: {\"reply\":\"I am fine, thank you.\",\"worldActions\":[],\"personaMoves\":[]" +
                (level >= 1 ? ",\"functions\":[{\"function\":\"Affirmation\",\"words\":\"fine\",\"target\":\"User\",\"intensity\":0.4,\"required\":false" + (level >= 2 ? ",\"behaviour\":\"Smile\"" : "") + "}]" : "") + "}.";
            var raw = await _model.AnswerAsync(system, said, Schema(allowed, level, functions, items.Concat(places).ToArray(), personaBehaviours));
            var json = JsonNode.Parse(raw) as JsonObject ?? throw new InvalidOperationException("the model did not give a JSON object");
            reply = (string?)json["reply"] ?? "";
            foreach (var a in json["worldActions"] as JsonArray ?? [])
                if (a is JsonObject o && (string?)o["verb"] is { } v && allowed.Contains(v) && (string?)o["target"] is { Length: > 0 } t) worldActions.Add((v, t));
            foreach (var g in json["personaMoves"] as JsonArray ?? [])
                if ((string?)g is { Length: > 0 } name) moves.Add(name);
            if (level >= 1) produced = Functions(json["functions"] as JsonArray, reply, level, functions, items, places);
        }
        catch (Exception e)
        {
            failure = e.Message;
            reply = "";
        }

        var response = Auc.Make(Auc.Bkn, Auc.Response, session, Auc.IdOf(request));
        if (failure.Length > 0)
        {
            response["Outcome"] = "not_achieved";
            response["Reason"] = new JsonObject { ["Code"] = "partner_unavailable", ["Description"] = failure };
            response["DeliberationState"] = "blocked";
            return [(_response, response)];
        }

        var fr = new JsonObject { ["Header"] = "PGM-FRS-V1.0" };
        session.Stamp(fr);
        fr["FinalResponseID"] = NewId("frs");
        fr["FinalResponseTime"] = Time();
        fr["AUCBKNRequestID"] = Auc.IdOf(request);
        fr["DialogID"] = _dialog;
        fr["Text"] = Text(reply);

        var pas = new JsonObject();
        var n = 0;
        foreach (var (verb, target) in worldActions)
            if (verb == "MU-Actuate" && !pas.ContainsKey(verb))
                pas[verb] = new JsonObject
                {
                    ["Header"] = "MMM-3CP-V2.2", ["MUActuatePAID"] = NewId("pa" + ++n), ["Time"] = Time(),
                    ["MUActuatePARQ"] = new JsonObject { ["RQ1"] = new JsonObject { ["Complement"] = "Nil", ["ItemID"] = target } }
                };
        // The Actions of the Final Response move the Persona in the M-Instance (M3308 Q5): MM-Animate. What the body expresses while speaking is in the Communicative Functions.
        if (moves.Count > 0)
            fr["Actions"] = new JsonArray(new JsonObject { ["Header"] = "MMM-ACT-V2.2", ["Action"] = "MM-Animate" });
        if (pas.Count > 0)
            fr["ProcessActions"] = new JsonObject { ["Header"] = "MMM-PAT-V2.2", ["MEnvironmentTime"] = Time(), ["ProcessActions"] = pas };
        // A Level 1 or 2 request answered with no function: a Level 0 Final Response.
        var levelProduced = produced.Count == 0 ? 0 : produced.Any(f => f is JsonObject o && o.ContainsKey("Behaviour")) ? 2 : 1;
        if (levelProduced > 0)
        {
            fr["FunctionLevel"] = levelProduced;
            fr["CommunicativeFunctions"] = produced;
        }

        response["Outcome"] = "achieved";
        response["DeliberationState"] = "concluded";
        response["Conclusion"] = new JsonObject { ["Headline"] = reply, ["FinalResponseID"] = (string)fr["FinalResponseID"]! };
        response["ActionsOutcome"] = new JsonObject { ["Outcome"] = pas.Count + moves.Count > 0 ? "produced" : "not_requested", ["StepCount"] = pas.Count + moves.Count };
        var outcome = new JsonObject { ["LevelRequested"] = levelRequested, ["LevelProduced"] = levelProduced, ["LevelDeclared"] = LevelDeclared, ["FunctionCount"] = produced.Count };
        if (levelProduced < levelRequested)
            outcome["Reason"] = levelRequested > LevelDeclared ? $"this Basic Knowledge produces up to Level {LevelDeclared}"
                              : functions.Length == 0 ? "no Function is both allowed and in the repertoire of the Persona"
                              : levelProduced == 0 ? "the model gave no function that conforms" : "the model gave no Behaviour that conforms";
        response["FunctionsOutcome"] = outcome;
        return [(_final, fr), (_response, response)];
    }

    // The functions of the model, kept only if they conform: an allowed Function, words found in the reply (the Anchor), a Target the PC Prompt gives, and at Level 2 a Behaviour of
    // the list of its Function (a wrong Behaviour is dropped, the function kept: a Level 2 Final Response may carry functions without behaviour).
    private static JsonArray Functions(JsonArray? given, string reply, int level, string[] functions, string[] items, string[] places)
    {
        var result = new JsonArray();
        var words = Communicative.Words(reply);
        static string Bare(string w) => new string(w.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        var bare = words.Select(Bare).ToArray();
        var n = 0;
        foreach (var g in given ?? [])
        {
            if (g is not JsonObject o || (string?)o["function"] is not { } name || !functions.Contains(name)) continue;
            var f = new JsonObject { ["FunctionID"] = $"f{++n}", ["Function"] = name };

            var target = (string?)o["target"] ?? "none";
            if (target.Equals("User", StringComparison.OrdinalIgnoreCase)) f["Target"] = new JsonObject { ["Kind"] = "User" };
            else if (items.Contains(target)) f["Target"] = new JsonObject { ["Kind"] = "Item", ["ID"] = target };
            else if (places.Contains(target)) f["Target"] = new JsonObject { ["Kind"] = "MLocation", ["ID"] = target };
            else if (name is "Gaze" or "Deictic") continue;   // a look or a pointing towards nothing that is known

            // The Anchor: the words of the reply the model named, found in the reply.
            var anchor = Communicative.Words((string?)o["words"] ?? "").Select(Bare).Where(w => w.Length > 0).ToArray();
            var first = -1;
            for (var i = 0; anchor.Length > 0 && i + anchor.Length <= bare.Length && first < 0; i++)
                if (anchor.Select((w, k) => bare[i + k] == w).All(x => x)) first = i;
            if (first < 0 && anchor.Length > 0) first = Array.IndexOf(bare, anchor[0]);
            f["Anchor"] = first >= 0
                ? new JsonObject { ["Position"] = "During", ["FirstWord"] = first, ["LastWord"] = Math.Min(words.Length - 1, first + Math.Max(1, anchor.Length) - 1) }
                : new JsonObject { ["Position"] = "Before" };
            f["Intensity"] = Math.Round(Math.Clamp((double?)o["intensity"] ?? 0.5, 0, 1), 2);
            f["Requirement"] = (bool?)o["required"] == true ? "Required" : "Optional";
            if (level >= 2 && (string?)o["behaviour"] is { } b && Communicative.BehavioursOf[name].Contains(b))
                f["Behaviour"] = new JsonObject { ["Name"] = b, ["BodyPart"] = Communicative.BodyPartOf(b), ["TowardsTarget"] = f.ContainsKey("Target") };
            result.Add(f);
        }
        return result;
    }

    // What the model must give: constrained by a schema, so that a small model cannot invent fields or names outside the closed lists.
    private static JsonObject Schema(string[] allowed, int level, string[] functions, string[] targets, string[] personaBehaviours)
    {
        var properties = new JsonObject
        {
            ["reply"] = new JsonObject { ["type"] = "string" },
            ["worldActions"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("verb", "target"),
                    ["properties"] = new JsonObject
                    {
                        ["verb"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(allowed.Select(a => (JsonNode)a).ToArray()) },
                        ["target"] = new JsonObject { ["type"] = "string" }
                    }
                }
            },
            ["personaMoves"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } }
        };
        var required = new JsonArray("reply", "worldActions", "personaMoves");
        if (level >= 1)
        {
            var item = new JsonObject
            {
                ["function"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(functions.Select(f => (JsonNode)f).ToArray()) },
                ["words"] = new JsonObject { ["type"] = "string" },
                ["target"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(new[] { "User", "none" }.Concat(targets).Distinct().Select(t => (JsonNode)t).ToArray()) },
                ["intensity"] = new JsonObject { ["type"] = "number" },
                ["required"] = new JsonObject { ["type"] = "boolean" }
            };
            var itemRequired = new JsonArray("function", "words", "target", "intensity", "required");
            if (level >= 2)
            {
                var behaviours = functions.SelectMany(f => Communicative.BehavioursOf[f]).Distinct().Where(b => personaBehaviours.Length == 0 || personaBehaviours.Contains(b));
                item["behaviour"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(behaviours.Select(b => (JsonNode)b).ToArray()) };
                itemRequired.Add("behaviour");
            }
            properties["functions"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["required"] = itemRequired, ["properties"] = item }
            };
            required.Add("functions");
        }
        return new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["required"] = required, ["properties"] = properties };
    }
}
