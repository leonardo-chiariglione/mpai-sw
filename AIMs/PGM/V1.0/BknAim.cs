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
    private JsonObject? _lastPrompt;      // the PC Prompt of the last deliberation: a continuation Request (a Condition that now holds) is concluded on it
    private JsonObject? _wordsToCome;     // what the A-User says once the Process Actions of a Condition that held are done: the Text and its Communicative Functions

    // The Constraint of a continuation Request that reports the Process Actions of the last Final Response done (M3313 4: the greeting when the door is open).
    public const string ActionsDone = "Process Actions done: ";
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

        // A CONDITION THAT NOW HOLDS. A-User Control kept a goal pending (its Condition is in the Constraints of the Goal BKN understood); when Context Description reports the
        // event, it sends a continuation Request, the Condition held in the Constraints of the Goal. BKN concludes it on the PC Prompt of the command, without a new consultation.
        // The Process Actions of a Condition that held are done: the words that come after them, in a Final Response of their own (no model is asked).
        if (_pendingRequest is { } done && (string?)done["Exchange"]?["Phase"] == "continuation"
            && ((done["Goal"]?["Constraints"] as JsonArray)?.Select(x => (string?)x) ?? []).Any(c => c is not null && c.StartsWith(ActionsDone, StringComparison.Ordinal)))
        {
            _pendingRequest = null;
            var session = Session.Of(done);
            var rs = Auc.Make(Auc.Bkn, Auc.Response, session, Auc.IdOf(done));
            rs["Outcome"] = "achieved";
            rs["DeliberationState"] = "concluded";
            rs["ActionsOutcome"] = new JsonObject { ["Outcome"] = "not_requested", ["StepCount"] = 0 };
            if (_wordsToCome is { } words)
            {
                _wordsToCome = null;
                var fr2 = new JsonObject { ["Header"] = "PGM-FRS-V1.0" };
                session.Stamp(fr2);
                fr2["FinalResponseID"] = NewId("frs");
                fr2["FinalResponseTime"] = Time();
                fr2["AUCBKNRequestID"] = Auc.IdOf(done);
                fr2["DialogID"] = _dialog;
                foreach (var (k, v) in words) fr2[k] = v?.DeepClone();
                rs["Conclusion"] = new JsonObject { ["Headline"] = TextOf(words["Text"] as JsonObject) ?? "", ["FinalResponseID"] = (string)fr2["FinalResponseID"]! };
                outputs.Add((_final, fr2));
            }
            outputs.Add((_response, rs));
            return Give(message, outputs);
        }
        if (!_consulting && _pendingPrompt is null && _pendingRequest is { } cont && (string?)cont["Exchange"]?["Phase"] == "continuation" && _lastPrompt is not null)
        {
            _pendingRequest = null;
            outputs.AddRange(await Conclude(cont, _lastPrompt));
            return Give(message, outputs);
        }
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
            _lastPrompt = prompt;
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
        if (allowed is not { Length: > 0 }) allowed = ["Property Change", "MM-Animate"];
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
        // The Condition: held (a continuation Request says so in the Constraints of its Goal), or to be found in the command.
        var held = Strings(request["Goal"]?["Constraints"]).FirstOrDefault(c => c.StartsWith(ConditionHeld, StringComparison.Ordinal));
        string? conditionEvent = null, conditionSound = null;

        string reply; var worldActions = new List<(string Verb, string Target, string Property, string Value)>(); var moves = new List<string>();
        var produced = new JsonArray();
        var failure = "";
        try
        {
            // FIRST, IS THE COMMAND CONDITIONAL? One small question of its own (N-37, 2026-11-17): asked together with the actions, a small model (llama3.2:3b) often wrote a
            // conditional reply but did the action at once, or the reverse. The answer decides what the model is asked next.
            if (held is null)
            {
                // The model copies the words of the condition ("if somebody knocks"), or nothing: asked so, llama3.2:3b was right on 20 of 20 wordings, against 18 of 20 asked for
                // a true/false (Night\n37\classify.py). It sometimes copies the words of an example instead of the human's: the event is then said from the sound.
                var c = JsonNode.Parse(await _model.AnswerAsync(ConditionSystem, said, ConditionSchema)) as JsonObject;
                if (((string?)c?["condition"])?.Trim().TrimEnd('.', ',') is { Length: > 0 } words)
                {
                    conditionSound = (string?)c!["sound"] is "Bell" or "Knock" ? (string)c["sound"]! : "None";
                    var bare = System.Text.RegularExpressions.Regex.Replace(words, @"^(if|when|whenever|as soon as|once)\s+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    conditionEvent = said.Contains(words, StringComparison.OrdinalIgnoreCase) && bare.Length > 0 ? bare
                                   : conditionSound == "Bell" ? "the bell rings" : conditionSound == "Knock" ? "somebody knocks" : bare.Length > 0 ? bare : "the event";
                }
            }
            var system =
                "You are the A-User, an autonomous assistant of a responsible human in a metaverse building. " +
                "'reply' is what you say aloud to the human: one or two short natural sentences, never a command name. " +
                "If the human asks you to change an item of the building (for instance to open the doors, to let somebody in - which is opening the door -, or to switch on the lamp), add one entry to worldActions: verb 'Property Change', target the item, " +
                "and change: what you do to it, one of: " + string.Join(", ", ItemProperties.Changes.Keys) + ". " +
                "If you need to go somewhere in the building yourself, put the place in personaMoves (for instance 'doors'). Otherwise leave the lists empty. " +
                (places.Length > 0 ? "The places you can go to are: " + string.Join(", ", places) + "; to act on an item, first go to the place next to it. " : "") +
                (conditionEvent is not null
                    ? "The command is to be done only later, when this happens: " + conditionEvent + ". Do nothing now: worldActions and personaMoves stay empty, and 'reply' tells " +
                      "the human, in one sentence, what you will do and that you will do it when " + conditionEvent + " (for instance 'I will open the door when " + conditionEvent + ".'). "
                    : (held is null ? "Do now what the command asks. "
                       : "The human gave you this command earlier, to be done when something happened, and it has happened now (" + held[ConditionHeld.Length..].Trim() + "). " +
                         "Do now everything the command asks, in order. ") +
                      (places.Length > 0 ? "You are not next to the items: whenever you change an item, personaMoves must name the place next to it. " : "") +
                      "To let somebody in, the change is 'open'; 'unlock' and 'lock' only when the human says unlock or lock. " +
                      (held is null ? ""
                       : "Example: the command was 'when somebody knocks, open the window and say hi', and somebody knocked: worldActions [{\"verb\":\"Property Change\",\"target\":\"window\"," +
                         "\"change\":\"open\"}], personaMoves [the place next to the window], reply \"Hi!\". " +
                         "'reply' is only what you say now to whoever is there (for instance a short greeting), not to the human. "));
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
                "Example. Human: 'please close the window'. You: {\"reply\":\"Closing the window now.\",\"worldActions\":[{\"verb\":\"Property Change\",\"target\":\"window\",\"change\":\"close\"}],\"personaMoves\":[]" +
                (level >= 1 ? ",\"functions\":[{\"function\":\"Deictic\",\"words\":\"the window\",\"target\":\"window\",\"intensity\":0.6,\"required\":true" + (level >= 2 ? ",\"behaviour\":\"Point\"" : "") + "}]" : "") + "}. " +
                "Example. Human: 'how are you'. You: {\"reply\":\"I am fine, thank you.\",\"worldActions\":[],\"personaMoves\":[]" +
                (level >= 1 ? ",\"functions\":[{\"function\":\"Affirmation\",\"words\":\"fine\",\"target\":\"User\",\"intensity\":0.4,\"required\":false" + (level >= 2 ? ",\"behaviour\":\"Smile\"" : "") + "}]" : "") + "}.";
            var raw = await _model.AnswerAsync(system, said, Schema(allowed, level, functions, items.Concat(places).ToArray(), personaBehaviours, false, items, places));
            var json = JsonNode.Parse(raw) as JsonObject ?? throw new InvalidOperationException("the model did not give a JSON object");
            reply = (string?)json["reply"] ?? "";
            foreach (var a in json["worldActions"] as JsonArray ?? [])
                if (a is JsonObject o && (string?)o["verb"] is { } v && allowed.Contains(v) && Known((string?)o["target"], items) is { Length: > 0 } t)
                {
                    // the change the model chose ("open"), as the Item Property and its value (OpenState Open); a model that gives the property and the value is also understood
                    if ((string?)o["change"] is { } ch && ItemProperties.Changes.TryGetValue(ch.Trim().ToLowerInvariant(), out var pc)) worldActions.Add((v, t, pc.Name, pc.Value));
                    else if ((string?)o["property"] is { } pn && ItemProperties.Vocabulary.TryGetValue(pn, out var values) && (string?)o["value"] is { } pv && values.Contains(pv))
                        worldActions.Add((v, t, pn, pv));
                }
            foreach (var g in json["personaMoves"] as JsonArray ?? [])
                if (Known((string?)g, places) is { Length: > 0 } name) moves.Add(name);
            if (level >= 1) produced = Functions(json["functions"] as JsonArray, reply, level, functions, items, places);
            if (conditionEvent is not null) { worldActions.Clear(); moves.Clear(); }
            // An Item changed with no walk, when the PC Prompt gives one place only: the Persona goes there first (the prompt asks it; llama3.2:3b sometimes forgot, and the
            // door opened by itself while the Persona stood across the room - N-37, 2026-11-17).
            else if (worldActions.Count > 0 && moves.Count == 0 && places.Length == 1) moves.Add(places[0]);
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
        // Going to a place: the Persona walks there, animated by a Stream - MM-Animate (MMM-TEC V2.2), RQ1 the Persona, RQ3 At the M-Location it goes to, RQ4 With the
        // Stream, which is the Portable Avatar that A-User Formation forms (A-User Control names it). A Persona is not moved with MM-Move: a walk would be a very long list of
        // MM-Moves. It comes before the change of an Item, which A-User Control sends when the Persona has reached the place.
        foreach (var place in moves.Where(places.Contains).Take(1))
            pas["MM-Animate"] = new JsonObject
            {
                ["Header"] = "MMM-2AP-V2.2", ["MMAnimatePAID"] = NewId("pa" + ++n), ["Time"] = Time(),
                ["MMAnimatePARQ"] = new JsonObject
                {
                    ["RQ1"] = new JsonObject { ["Complement"] = "Nil", ["ItemID"] = _persona },
                    ["RQ3"] = new JsonObject { ["Complement"] = "At", ["MLocationID"] = place },
                    ["RQ4"] = new JsonObject { ["Complement"] = "With", ["Stream"] = "" }
                }
            };
        // A change of an Item of the M-Instance is an MM action: Property Change (MMM-TEC V2.2), RQ1 the Item. RQ12, the Properties (Item Property, MMM-IPR), is in
        // MMM-TEC V3.0 (draft); PGM-AUA V1.0 still refers to MMM-TEC V2.2, whose schema of the Process Action does not forbid it.
        // One Property Change for the first Item changed, with every Property the model changes on it (the last value of a Property given twice).
        var changes = worldActions.Where(w => w.Verb == "Property Change").ToList();
        if (changes.Count > 0)
        {
            var target = changes[0].Target;
            var properties = new JsonArray(changes.Where(c => c.Target == target).GroupBy(c => c.Property)
                                                  .Select(g => (JsonNode)new JsonObject { ["Name"] = g.Key, ["Value"] = g.Last().Value }).ToArray());
            pas["Property Change"] = new JsonObject
            {
                ["Header"] = "MMM-PCP-V2.2", ["PropertyChangePAID"] = NewId("pa" + ++n), ["Time"] = Time(),
                ["PropertyChangePARQ"] = new JsonObject
                {
                    ["RQ1"] = new JsonObject { ["Complement"] = "Nil", ["ItemID"] = target },
                    ["RQ12"] = new JsonObject { ["Complement"] = "With", ["Properties"] = properties }
                }
            };
        }
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
        // A CONDITION THAT HELD, AND PROCESS ACTIONS: the words are said to whoever is there when the Process Actions are done (the greeting when the door is open). This Final
        // Response carries the Actions and the Process Actions; the Text and its Communicative Functions wait for the continuation Request that reports them done.
        // The same for a command done at once (N-37, 2026-11-17): "The door is now open" is said when the door is open, not as the Persona starts walking to it.
        _wordsToCome = null;
        if (pas.Count > 0 && reply.Length > 0)
        {
            _wordsToCome = new JsonObject { ["Text"] = fr["Text"]!.DeepClone() };
            if (levelProduced > 0) { _wordsToCome["FunctionLevel"] = levelProduced; _wordsToCome["CommunicativeFunctions"] = produced.DeepClone(); }
            fr.Remove("Text"); fr.Remove("FunctionLevel"); fr.Remove("CommunicativeFunctions");
        }

        response["Outcome"] = "achieved";
        response["DeliberationState"] = conditionEvent is null ? "concluded" : "deliberating";
        // A conditional command: the goal is understood with its Condition, and nothing is done until A-User Control reports that it holds.
        if (conditionEvent is not null)
            response["GoalUnderstood"] = new JsonObject
            {
                ["Objective"] = said,
                ["Constraints"] = new JsonArray((JsonNode)(ConditionPrefix + conditionEvent), (JsonNode)(AudioEventPrefix + conditionSound), (JsonNode)"Persistence: Once")
            };
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
    // The Constraints of a Goal that carry a Condition (M3313: Event, Persistence; the Goal of PGM-UCK V1.0 has no Condition element, so they are Constraints).
    public const string ConditionPrefix = "Condition: ", AudioEventPrefix = "Audio Event: ", ConditionHeld = "Condition held: ";

    // The question whether a command is conditional, and its schema (the property "condition" at the top level tells it from the main answer).
    private const string ConditionSystem =
        "Read the human's command. Some commands are to be done only when something happens first: they contain a condition such as 'if somebody knocks', " +
        "'when someone rings', 'as soon as the bell rings', 'whenever there is a knock', 'when they ring'. Others are to be done now. " +
        "First copy into 'condition' the words of the command that say what must happen first (for instance 'if somebody knocks'), or '' if there are none. " +
        "Then 'sound': Bell if the condition is a bell, a doorbell or somebody ringing; Knock if it is a knock or somebody knocking; None if there is no condition. " +
        "Examples. 'Open the door if somebody knocks': {\"condition\":\"if somebody knocks\",\"sound\":\"Knock\"}. " +
        "'When someone rings, say hi': {\"condition\":\"when someone rings\",\"sound\":\"Bell\"}. " +
        "'Open the door': {\"condition\":\"\",\"sound\":\"None\"}. " +
        "'Open the door and greet whoever is there': {\"condition\":\"\",\"sound\":\"None\"}.";
    private static readonly JsonObject ConditionSchema = new()
    {
        ["type"] = "object", ["additionalProperties"] = false, ["required"] = new JsonArray("condition", "sound"),
        ["properties"] = new JsonObject
        {
            ["condition"] = new JsonObject { ["type"] = "string" },
            ["sound"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("Bell", "Knock", "None") }
        }
    };

    // A name the model gave, as the PC Prompt writes it (the model may write "door" for "Door"); when the PC Prompt gives no list, the name as it is.
    private static string? Known(string? name, string[] names) =>
        name is null || names.Length == 0 ? name : names.FirstOrDefault(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static JsonObject Schema(string[] allowed, int level, string[] functions, string[] targets, string[] personaBehaviours, bool askCondition = false,
                                     string[]? items = null, string[]? places = null)
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
                    ["required"] = new JsonArray("verb", "target", "change"),
                    ["properties"] = new JsonObject
                    {
                        ["verb"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(allowed.Where(a => a is not "MM-Animate" and not "MM-Move").Select(a => (JsonNode)a).ToArray()) },
                        // the target: one of the Items of the PC Prompt when it gives them
                        ["target"] = items is { Length: > 0 } ? new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(items.Select(i => (JsonNode)i).ToArray()) } : new JsonObject { ["type"] = "string" },
                        // what is done to it, in plain words (a small model chose LockState Unlocked for "let them in"; it chooses "open"): mapped to the Item Property and its value
                        ["change"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(ItemProperties.Changes.Keys.Select(k => (JsonNode)k).ToArray()) }
                    }
                }
            },
            ["personaMoves"] = new JsonObject
            {
                ["type"] = "array",
                // a place: one of the M-Locations of the PC Prompt when it gives them
                ["items"] = places is { Length: > 0 } ? new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(places.Select(p => (JsonNode)p).ToArray()) } : new JsonObject { ["type"] = "string" }
            }
        };
        var required = new JsonArray("reply", "worldActions", "personaMoves");
        if (askCondition)
        {
            properties["condition"] = new JsonObject
            {
                ["type"] = "object", ["required"] = new JsonArray("conditional", "event", "sound"),
                ["properties"] = new JsonObject
                {
                    ["conditional"] = new JsonObject { ["type"] = "boolean" }, ["event"] = new JsonObject { ["type"] = "string" },
                    ["sound"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("Bell", "Knock", "None") }
                }
            };
            required.Add("condition");
        }
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

// THE PROPERTIES OF ITEMS that a Property Change sets: the starting vocabulary proposed in the M-doc on Property Change (name -> allowed values). A PROPOSAL, not yet MMM-TEC.
public static class ItemProperties
{
    public static readonly IReadOnlyDictionary<string, string[]> Vocabulary = new Dictionary<string, string[]>
    {
        ["OpenState"] = ["Open", "Closed"],
        ["LockState"] = ["Locked", "Unlocked"],
        ["PowerState"] = ["On", "Off"]
    };

    // The same, as what is done to an Item in plain words (what the language model of Basic Knowledge chooses): the Item Property and its value.
    public static readonly IReadOnlyDictionary<string, (string Name, string Value)> Changes = new Dictionary<string, (string, string)>
    {
        ["open"] = ("OpenState", "Open"), ["close"] = ("OpenState", "Closed"),
        ["lock"] = ("LockState", "Locked"), ["unlock"] = ("LockState", "Unlocked"),
        ["switch on"] = ("PowerState", "On"), ["switch off"] = ("PowerState", "Off")
    };
}
