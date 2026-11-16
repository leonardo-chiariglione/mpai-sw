using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using AIF.Controller;

namespace Mpai.Pgm;

// PGM-AUC-V1.0 - A-User Control. Takes the Human Command and drives the partners of the A-User with Requests (Mode RQ), taking their Responses (Mode RS):
//   Human Command RQ -> CXT Request -> CXT Response -> PRC Request -> PRC Response -> BKN Request -> BKN Response (the Final Response goes from BKN to AUF, not here)
//   -> Human Command Response (as soon as it has decided) + AUF Request -> the Portable Avatar and the Process Actions of AUF, which it authorises (or stops).
// An authorised Portable Avatar goes on (to Portable Avatar Demultiplexing, then to the Units); an authorised Process Action is requested of the M-Instance (Process Action Request),
// whose Process Action Response it takes. Nothing else leaves it.
//
// In this slice A-User Control authorises everything that arrives, except when the Human Command is of a kind that stops (pause, terminate): then it stops the Process Actions
// and the Portable Avatar. The partners DAC, USR, PAL and AUS (history) are not asked by A-User Control in this slice; the partners ask each other.
public sealed class AucAim : PgmAim
{
    private readonly string _humanIn, _paResponse, _paIn, _avatarIn, _cxtIn, _prcIn, _bknIn, _dacIn, _usrIn, _palIn, _aufIn, _ausIn;
    private readonly string _action, _avatarOut, _paRequest, _cxtOut, _prcOut, _bknOut, _dacOut, _usrOut, _palOut, _aufOut, _humanOut, _ausOut;

    private JsonObject? _command;
    private bool _authorised = true;

    // A PENDING GOAL (M3313 2): a command to be done when something happens. Basic Knowledge understood it with a Condition (in the Constraints of GoalUnderstood);
    // A-User Control keeps it, asks Context Description to report the event, and when it is reported asks Basic Knowledge again, the Condition held.
    public JsonObject? PendingGoal { get; private set; }
    private string _bknResponseId = "", _watchId = "";

    // THE PROCESS ACTIONS IN ORDER (M3313 4). The Process Actions of a formation are requested of the M-Instance one at a time: the next when the M-Instance has answered the
    // previous one (the Persona has reached the place before the door opens). The Portable Avatar of the same formation waits until they are all done (the Persona says
    // "welcome" when the door is open).
    private readonly Queue<(string Kind, JsonObject PA, JsonObject? Avatar)> _toRequest = new();
    private string? _awaiting, _awaitingId;   // the Process Action requested and not yet answered, and the ID of its Request: only the Response to it is taken
    private JsonObject? _avatar;          // the Portable Avatar formed last, not yet requested
    private bool _formingMotion, _actionsArrived, _speakAfter, _conditionHeld;
    private string _bknHeldId = "";
    public List<string> Requested { get; } = new();
    public bool SpeechToCome => _speakAfter;
    // What was taken already: a Datum that reaches A-User Control again (by its ID) is not acted on twice.
    private readonly HashSet<string> _taken = new(StringComparer.Ordinal);
    private bool Fresh(string? id) => id is null || _taken.Add(id);
    public List<string> Trace { get; } = new();   // what A-User Control decided, in order (for a report)

    // THE PERSONA IS ANIMATED (M3313). Every Portable Avatar of A-User Formation is the Stream with which A-User Control requests MM-Animate of its Persona (setting
    // Persona.ItemId): the walk to a place (the MM-Animate of the Final Response, At the M-Location) and the speech with its gestures. A Persona is not moved with MM-Move.
    public string Persona { get; init; } = "a-user-persona";

    // What the Controller of the test (or the Module) may ask: how the command ended.
    public string Outcome { get; private set; } = "";

    // THE COMMUNICATIVE FUNCTIONS (M3308 3.5). The Level wanted (setting Auc.FunctionLevel, 1 by default) and the Functions allowed (all of the closed list by default); A-User Control
    // never requests a Level above the one Basic Knowledge declared (LevelDeclared of its AUC-BKN Response). What it learnt: Basic Knowledge's FunctionsOutcome and, from
    // A-User Formation, the status of each function.
    public const string LevelSetting = "Auc.FunctionLevel";
    public int FunctionLevel { get; set; } = 1;
    public string[] FunctionsAllowed { get; set; } = Communicative.Functions;
    public int? BknLevelDeclared { get; set; }
    public int LevelRequested { get; private set; }
    public JsonObject? FunctionsOutcome { get; private set; }
    public JsonArray? FunctionStatus { get; private set; }

    public AucAim(string instanceId, AimPortReader ports) : base(instanceId, ports)
    {
        _humanIn = In("PGM-HCM-V1.0"); _paResponse = In("MMM-PAC-V2.2"); _paIn = In("MMM-PAT-V2.2"); _avatarIn = In("PAF-PAV-V1.6");
        _cxtIn = In(Auc.Cxt); _prcIn = In(Auc.Prc); _bknIn = In(Auc.Bkn); _dacIn = In(Auc.Dac); _usrIn = In(Auc.Usr); _palIn = In(Auc.Pal); _aufIn = In(Auc.Auf); _ausIn = In(Auc.Aus);
        _action = Out("MMM-ACT-V2.2"); _avatarOut = Out("PAF-PAV-V1.6"); _paRequest = Out("MMM-PAC-V2.2");
        _cxtOut = Out(Auc.Cxt); _prcOut = Out(Auc.Prc); _bknOut = Out(Auc.Bkn); _dacOut = Out(Auc.Dac); _usrOut = Out(Auc.Usr); _palOut = Out(Auc.Pal); _aufOut = Out(Auc.Auf);
        _humanOut = Out("PGM-HCM-V1.0"); _ausOut = Out(Auc.Aus);
    }

    public override Task<Message> ProcessAsync(Message message)
    {
        var outputs = new List<(string, JsonNode?)>();

        if (Read(message, _humanIn) is { } hc && (string?)hc["Mode"] == "RQ")
        {
            _command = hc;
            var stops = (string?)hc["CommandType"] is "pause" or "terminate" or "override";
            _authorised = !stops;
            var session = Session.Of(hc);
            var rq = Auc.Make(Auc.Cxt, Auc.Request, session);
                        outputs.Add((_cxtOut, rq));
        }

        // The event of a pending goal is heard: Basic Knowledge is asked again, with the Condition held.
        if (Read(message, _cxtIn) is { } heard && !Auc.IsRequest(heard) && _watchId.Length > 0 && (string?)heard["RequestID"] == _watchId)
        {
            _watchId = "";
            if (PendingGoal is { } goal && (string?)heard["Outcome"] == "achieved")
            {
                PendingGoal = null;
                var what = string.Join("; ", (heard["GoalUnderstood"]?["Constraints"] as JsonArray ?? []).Select(c => (string?)c));
                var rq = Auc.Make(Auc.Bkn, Auc.Request, Session.Of(heard), objective: (string?)goal["Objective"] ?? "");
                rq["Exchange"] = new JsonObject { ["Phase"] = "continuation", ["ResponseID"] = _bknResponseId };
                rq["Goal"] = new JsonObject
                {
                    ["Objective"] = (string?)goal["Objective"] ?? "",
                    ["Constraints"] = new JsonArray((JsonNode)(BknAim.ConditionHeld + ConditionOf(goal) + " - " + what))
                };
                rq["Actions"] = ActionsBlock();
                outputs.Add((_bknOut, rq));
                _conditionHeld = true;
            }
            return Done(Give(message, Merge(outputs)));
        }

        if (Read(message, _cxtIn) is { } cxt && !Auc.IsRequest(cxt))
        {
            if ((string?)cxt["Outcome"] == "not_achieved")
                outputs.Add((_humanOut, HumanResponse("rejected")));
            else
            {
                var rq = Auc.Make(Auc.Prc, Auc.Request, Session.Of(cxt));
                outputs.Add((_prcOut, rq));
            }
        }

        if (Read(message, _prcIn) is { } prc && !Auc.IsRequest(prc))
        {
            var rq = Auc.Make(Auc.Bkn, Auc.Request, Session.Of(prc));
            rq["Actions"] = ActionsBlock();
            outputs.Add((_bknOut, rq));
        }

        if (Read(message, _bknIn) is { } bkn && !Auc.IsRequest(bkn) && Fresh(Auc.IdOf(bkn)))
        {
            if (bkn["FunctionsOutcome"] is JsonObject fo)
            {
                FunctionsOutcome = (JsonObject)fo.DeepClone();
                BknLevelDeclared = (int?)fo["LevelDeclared"];
            }
            // A conditional command: accepted, pending. A-User Formation says what the A-User will do; Context Description is asked to report the event.
            var condition = (bkn["GoalUnderstood"]?["Constraints"] as JsonArray)?.Select(c => (string?)c).FirstOrDefault(c => c?.StartsWith(BknAim.ConditionPrefix, StringComparison.Ordinal) == true);
            if ((string?)bkn["DeliberationState"] == "deliberating" && condition is not null && _authorised)
            {
                PendingGoal = (JsonObject)bkn["GoalUnderstood"]!.DeepClone();
                _bknResponseId = Auc.IdOf(bkn);
                outputs.Add((_humanOut, HumanResponse("accepted")));
                var say = Auc.Make(Auc.Auf, Auc.Request, Session.Of(bkn));
                say["FormationControl"] = new JsonObject { ["Speech"] = "required", ["Face"] = "required", ["Body"] = "optional", ["Avatar"] = "required" };
                outputs.Add((_aufOut, say));
                var watch = Auc.Make(Auc.Cxt, Auc.Request, Session.Of(bkn), objective: "Report when: " + condition[BknAim.ConditionPrefix.Length..]);
                watch["Goal"] = new JsonObject
                {
                    ["Objective"] = "Report when: " + condition[BknAim.ConditionPrefix.Length..],
                    ["Constraints"] = new JsonArray((PendingGoal["Constraints"] as JsonArray)!.Select(c => (JsonNode?)c?.DeepClone()).Where(c => ((string?)c)?.StartsWith(BknAim.AudioEventPrefix, StringComparison.Ordinal) == true).ToArray())
                };
                watch["Audio"] = new JsonObject { ["Capture"] = new JsonObject { ["Requirement"] = "required", ["Priority"] = "high" } };
                _watchId = Auc.IdOf(watch);
                outputs.Add((_cxtOut, watch));
                return Done(Give(message, Merge(outputs)));
            }
            // Decided: the Human Command is answered at once; A-User Formation is asked to form what BKN concluded.
            var concluded = (string?)bkn["DeliberationState"] == "concluded";
            var actionsToCome = (int?)bkn["ActionsOutcome"]?["StepCount"] > 0;
            outputs.Add((_humanOut, HumanResponse(concluded ? (_authorised ? "accepted" : "rejected") : "escalated")));
            if (concluded && _authorised)
            {
                // The Portable Avatar of a formation with Process Actions is the Stream of the MM-Animate among them (the walk). When the Condition of a pending goal held,
                // what the A-User says comes after the Process Actions: Basic Knowledge is asked for it when they are done (a continuation Request).
                _formingMotion = actionsToCome; _actionsArrived = false; _speakAfter = actionsToCome && _conditionHeld; _conditionHeld = false;
                if (_speakAfter) _bknHeldId = Auc.IdOf(bkn);
                Trace.Add($"concluded: actions {actionsToCome}, words after them {_speakAfter}");
                if (bkn["Conclusion"]?["FinalResponseID"] is not null)
                    outputs.Add((_aufOut, FormationRequest(Session.Of(bkn), speech: true)));
            }
        }

        // The Portable Avatar of A-User Formation, and its Process Actions: each Portable Avatar becomes the Stream of an MM-Animate of the Persona.
        if (Read(message, _avatarIn) is { } avatar && _authorised && Fresh((string?)avatar["PortableAvatarID"])) { _avatar = avatar; Proceed(outputs); }
        if (Read(message, _paIn) is { } actions && actions["ProcessActions"] is JsonObject pas && _authorised
            && Fresh("pat:" + string.Join(",", pas.Select(p => (string?)p.Value?[p.Key.Replace("-", "").Replace(" ", "") + "PAID"]))))
        {
            foreach (var (kind, pa) in pas) if (pa is JsonObject o) _toRequest.Enqueue((kind, (JsonObject)o.DeepClone(), null));
            _actionsArrived = true;
            Proceed(outputs);
        }

        // The M-Instance's answer to a Process Action Request (for an MM-Animate: when the animation is over - the Persona has reached the place, the speech is said):
        // the next one is requested; when none is left, A-User Formation is asked for the speech, if it is still to come.
        if (Read(message, _paResponse) is { } paResponse && _awaitingId is not null && (string?)paResponse["ProcessActionID"] == _awaitingId)
        {
            _awaitingId = null;
            var ack = paResponse["PAStatus"] is JsonArray st && st.Any(s => s is JsonObject o && o.ContainsKey("Ack"));
            Outcome = ack ? "done" : "failed";
            _awaiting = null;
            if (!ack) { _toRequest.Clear(); _speakAfter = false; }
            else RequestNext(outputs);
        }

        if (Read(message, _aufIn) is { } auf && !Auc.IsRequest(auf))
        {
            if (auf["FunctionStatus"] is JsonArray fs) FunctionStatus = (JsonArray)fs.DeepClone();
            if (Outcome.Length == 0) Outcome = "formed";
        }

        return Done(Give(message, Merge(outputs)));
    }

    private JsonObject ActionsBlock() => new()
    {
        ["Requirement"] = "optional",
        ["Allowed"] = new JsonArray("Property Change", "MM-Move", "MM-Animate"),
        ["MaxSteps"] = 3,
        ["Syntax"] = "MMM-TEC-V2.2",
        // The Level of Communicative Functions: the one wanted, never above the Level Basic Knowledge declared in its last Response.
        ["FunctionLevel"] = LevelRequested = Math.Min(FunctionLevel, BknLevelDeclared ?? FunctionLevel),
        ["Functions"] = new JsonArray(FunctionsAllowed.Select(f => (JsonNode)f).ToArray())
    };

    private static string ConditionOf(JsonObject goal) =>
        (goal["Constraints"] as JsonArray)?.Select(c => (string?)c).FirstOrDefault(c => c?.StartsWith(BknAim.ConditionPrefix, StringComparison.Ordinal) == true)?[BknAim.ConditionPrefix.Length..] ?? "";

    private JsonObject FormationRequest(Session session, bool speech)
    {
        var rq = Auc.Make(Auc.Auf, Auc.Request, session);
        rq["FormationControl"] = new JsonObject { ["Speech"] = speech ? "required" : "not_requested", ["Face"] = "required", ["Body"] = "required", ["Avatar"] = "required" };
        return rq;
    }

    // A Portable Avatar formed: the Stream of the MM-Animate of the Final Response (the walk), when the motion was formed and its Process Actions are here; otherwise the Stream of
    // an MM-Animate of the Persona of its own (the speech), requested after the Process Actions already queued.
    private void Proceed(List<(string, JsonNode?)> outputs)
    {
        if (_avatar is null) return;
        if (_formingMotion)
        {
            if (!_actionsArrived) return;
            _formingMotion = false;
            var queued = _toRequest.ToList(); _toRequest.Clear();
            var given = false;
            foreach (var (kind, pa, av) in queued)
                if (kind == "MM-Animate" && !given && pa["MMAnimatePARQ"] is JsonObject rq)
                {
                    rq["RQ4"] = new JsonObject { ["Complement"] = "With", ["Stream"] = (string?)_avatar["PortableAvatarID"] ?? "" };
                    _toRequest.Enqueue((kind, pa, _avatar)); given = true;
                }
                else _toRequest.Enqueue((kind, pa, av));
            if (!given) _toRequest.Enqueue(("MM-Animate", Animate(_avatar, null), _avatar));
        }
        else _toRequest.Enqueue(("MM-Animate", Animate(_avatar, null), _avatar));
        _avatar = null;
        if (_awaiting is null) RequestNext(outputs);
    }

    // MM-Animate of the Persona With a Portable Avatar as its Stream (At an M-Location, for a walk).
    private JsonObject Animate(JsonObject avatar, string? at)
    {
        var rq = new JsonObject
        {
            ["RQ1"] = new JsonObject { ["Complement"] = "Nil", ["ItemID"] = Persona },
            ["RQ4"] = new JsonObject { ["Complement"] = "With", ["Stream"] = (string?)avatar["PortableAvatarID"] ?? "" }
        };
        if (at is not null) rq["RQ3"] = new JsonObject { ["Complement"] = "At", ["MLocationID"] = at };
        return new JsonObject { ["Header"] = "MMM-2AP-V2.2", ["MMAnimatePAID"] = NewId("pa"), ["Time"] = Time(), ["MMAnimatePARQ"] = rq };
    }

    // The next Process Action, as an Action and a Process Action Request (MMM-PAC) whose Request Complements are those of the Process Action: Nil, To, From, At the Items
    // (an M-Location by its Point of View or its ID), With the Item carried (an Item Property of RQ12) or the Stream (a Portable Avatar, which goes on before the Request).
    // When none is left, A-User Formation is asked for the speech if it is still to come.
    private void RequestNext(List<(string, JsonNode?)> outputs)
    {
        if (_toRequest.Count == 0)
        {
            if (_speakAfter && _command is not null)
            {
                _speakAfter = false;
                var rq = Auc.Make(Auc.Bkn, Auc.Request, Session.Of(_command), objective: Command("CommandIntent") ?? "");
                rq["Exchange"] = new JsonObject { ["Phase"] = "continuation", ["ResponseID"] = _bknHeldId };
                rq["Goal"] = new JsonObject
                {
                    ["Objective"] = Command("CommandIntent") ?? "",
                    ["Constraints"] = new JsonArray((JsonNode)(BknAim.ActionsDone + string.Join(", ", Requested)))
                };
                rq["Actions"] = ActionsBlock();
                outputs.Add((_bknOut, rq));
                Trace.Add("Process Actions done: Basic Knowledge asked for what comes after them");
            }
            return;
        }
        var (kind, pa, avatar) = _toRequest.Dequeue();
        if (avatar is not null) outputs.Add((_avatarOut, avatar));
        _awaiting = kind;
        Requested.Add(kind);
        var session = Session.Of(_command);
        var complements = new JsonArray();
        foreach (var (_, slot) in pa[kind.Replace("-", "").Replace(" ", "") + "PARQ"] as JsonObject ?? [])
        {
            if (slot is not JsonObject s) continue;
            var c = (string?)s["Complement"];
            var id = (string?)s["ItemID"] ?? (string?)s["MLocationID"] ?? (string?)s["PointOfView2"]?["PointOfViewID"] ?? (string?)s["PointOfView1"]?["PointOfViewID"];
            switch (c)
            {
                case "Nil" when id is not null: complements.Add(new JsonObject { ["NilItem"] = id }); break;
                case "To" when id is not null: complements.Add(new JsonObject { ["ToItem"] = id }); break;
                case "From" when id is not null: complements.Add(new JsonObject { ["FromItem"] = id }); break;
                case "At" when id is not null: complements.Add(new JsonObject { ["AtItem"] = id }); break;
                case "With" when (string?)s["Stream"] is { Length: > 0 } stream: complements.Add(new JsonObject { ["WithItem"] = stream }); break;
                case "With" when s["Properties"] is JsonArray props:
                    var target = (string?)pa[kind.Replace("-", "").Replace(" ", "") + "PARQ"]?["RQ1"]?["ItemID"] ?? "";
                    var ipr = new JsonObject { ["Header"] = "MMM-IPR-V3.0", ["MInstanceID"] = session.MInstance, ["ItemID"] = target, ["Properties"] = props.DeepClone() };
                    complements.Add(new JsonObject { ["WithItem"] = NewId("ipr"), ["Item"] = ipr });
                    break;
            }
        }
        outputs.Add((_action, new JsonObject { ["Header"] = "MMM-ACT-V2.2", ["Action"] = kind }));
        _awaitingId = NewId("pac");
        outputs.Add((_paRequest, new JsonObject
        {
            ["Header"] = "MMM-PAC-V2.2", ["MInstanceID"] = session.MInstance, ["UEnvironmentID"] = session.UEnvironment,
            ["ProcessActionID"] = _awaitingId, ["ProcessActionTime"] = Time(),
            ["RQ-Complements"] = complements, ["PAStatus"] = new JsonArray(), ["DescrMetadata"] = kind
        }));
    }

    // Several Data for the same Port in one firing: the Port carries the last (a Port is a Channel of one Message at a time in a firing).
    private static IEnumerable<(string, JsonNode?)> Merge(List<(string Key, JsonNode? Value)> outputs) =>
        outputs.GroupBy(o => o.Key).Select(g => (g.Key, g.Last().Value));

    private string? Command(string property) => (string?)_command?[property];

    private JsonObject HumanResponse(string result)
    {
        var rs = new JsonObject { ["Header"] = "PGM-HCM-V1.0", ["Mode"] = "RS" };
        Session.Of(_command).Stamp(rs);
        rs["HumanCommandID"] = Command("HumanCommandID") ?? NewId("hc");
        rs["HumanCommandTime"] = Time();
        rs["RequestID"] = Command("HumanCommandID") ?? "";
        rs["ExecutionResult"] = result;
        return rs;
    }
}
