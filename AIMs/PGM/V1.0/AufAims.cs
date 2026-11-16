using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using AIF.Controller;

namespace Mpai.Pgm;

// The Sub-AIMs of PGM-AUF-V1.0 that this slice implements, each against the Ports of its own Metadata.

// PGM-PSD-V1.0 - Personal Status Demultiplexing: splits the A-User State into the Speech, Face and Body Personal Statuses.
public sealed class PsdAim : PgmAim
{
    private readonly string _state, _request, _speech, _face, _body, _response;

    public PsdAim(string instanceId, AimPortReader ports) : base(instanceId, ports)
    {
        _state = In("MMC-EST-V2.5"); _request = In(Auc.Auf);
        _speech = Out("MMC-SPS-V2.5"); _face = Out("MMC-FPS-V2.5"); _body = Out("MMC-BPS-V2.5"); _response = Out(Auc.Auf);
    }

    public override Task<Message> ProcessAsync(Message message)
    {
        var outputs = new List<(string, JsonNode?)>();
        var state = Read(message, _state);
        if (state is not null)
        {
            outputs.Add((_speech, new JsonObject { ["Header"] = "MMC-SPS-V2.5", ["SpeechPersonalStatusID"] = NewId("sps") }));
            outputs.Add((_face, new JsonObject { ["Header"] = "MMC-FPS-V2.5", ["FacePersonalStatusID"] = NewId("fps") }));
            outputs.Add((_body, new JsonObject { ["Header"] = "MMC-BPS-V2.5", ["BodyPersonalStatusID"] = NewId("bps") }));
        }
        if (Read(message, _request) is { } rq && Auc.IsRequest(rq)) outputs.Add((_response, AufResponse.Make(rq)));
        return Done(Give(message, outputs));
    }
}

// PGM-FRD-V1.0 - Final Response Demultiplexing. Takes apart the Final Response of Basic Knowledge: the Text (for Text-To-Speech and the Portable Avatar), the Actions of MMM-TEC V2.2 that go
// with the text (for Animation Object Generation) and the Process Actions, more remote from the text (they leave A-User Formation unchanged).
// A Port carries one Message at a time: of the Actions the Port carries the first (in this slice Basic Knowledge gives at most one).
public sealed class FrdAim : PgmAim
{
    private readonly string _request, _final, _text, _functions, _action, _actions, _response;

    public FrdAim(string instanceId, AimPortReader ports) : base(instanceId, ports)
    {
        _request = In(Auc.Auf); _final = In("PGM-FRS-V1.0");
        _text = Out("OSD-BTO-V1.5"); _functions = Out(Communicative.Header); _action = Out("MMM-ACT-V2.2"); _actions = Out("MMM-PAT-V2.2"); _response = Out(Auc.Auf);
    }

    public override Task<Message> ProcessAsync(Message message)
    {
        var outputs = new List<(string, JsonNode?)>();
        if (Read(message, _final) is { } fr)
        {
            if (fr["Text"] is JsonObject text) outputs.Add((_text, text.DeepClone()));
            // The Communicative Functions, as a Communicative Functions Data Type for Behaviour Planning (a Level 0 Final Response has none).
            if (fr["CommunicativeFunctions"] is JsonArray { Count: > 0 } functions)
            {
                var cfn = new JsonObject { ["Header"] = Communicative.Header };
                Session.Of(fr).Stamp(cfn);
                cfn["CommunicativeFunctionsID"] = NewId("cfn");
                cfn["CommunicativeFunctionsTime"] = Time();
                cfn["FinalResponseID"] = (string?)fr["FinalResponseID"] ?? "";
                cfn["Level"] = Communicative.LevelOf(fr);
                cfn["Functions"] = functions.DeepClone();
                outputs.Add((_functions, cfn));
            }
            if (fr["Actions"] is JsonArray { Count: > 0 } actions && actions[0] is JsonObject first) outputs.Add((_action, first.DeepClone()));
            if (fr["ProcessActions"] is JsonObject processActions) outputs.Add((_actions, processActions.DeepClone()));
            if (Read(message, _request) is { } rq && Auc.IsRequest(rq)) outputs.Add((_response, AufResponse.Make(rq)));
        }
        return Done(Give(message, outputs));
    }
}

// PGM-AOG-V1.0 - Animation Object Generation. Generates the animation of the Persona of the A-User from the Communicative Functions planned by Behaviour Planning (each with its Behaviour
// and its times) and from an Action of MMM-TEC V2.2 that goes with the text (MM-Animate: the Persona moves in the M-Instance): a Face Animation Object (PAF-AMO, Port 1) with the
// Behaviours of the eyes and the face, and a Body Animation Object (PAF-AMO, Port 2) with those of the head, the hands and the torso. A STUB of the movement: the Animation Qualifier
// names a symbolic format (PoseToken) and each item of the data is one timed Behaviour ("Nod Head 1.20-1.90 peak 1.45 amplitude 0.65 towards User"); the frames of the face (FACS) and
// of the body (BAP, BVH) are for a realiser to make.
public sealed class AogAim : PgmAim
{
    private readonly string _request, _functions, _action, _face, _body, _response;

    // Where the Persona walks to, when the Action is a walk (the M-Location of the MM-Animate Process Action of the Final Response): A-User Formation gives it.
    public string? WalkTarget { get; set; }

    public AogAim(string instanceId, AimPortReader ports) : base(instanceId, ports)
    {
        _request = In(Auc.Auf); _functions = In(Communicative.Header); _action = In("MMM-ACT-V2.2");
        _face = Out("PAF-AMO-V1.6", 1); _body = Out("PAF-AMO-V1.6", 2); _response = Out(Auc.Auf);
    }

    private JsonObject Animation(string idPrefix, IEnumerable<string> items) => new()
    {
        ["Header"] = "PAF-AMO-V1.6", ["AnimationObjectID"] = NewId(idPrefix),
        ["AnimationObjectData"] = new JsonArray(items.Select(i => (JsonNode)new JsonObject { ["Data"] = i }).ToArray()),
        ["AnimationQualifier"] = new JsonObject { ["Header"] = "TFA-AMQ-V1.5", ["AnimationQualifierID"] = NewId("amq"), ["SubTypes"] = "Animation", ["Formats"] = new JsonObject { ["ContentFormat"] = "PoseToken" } }
    };

    public override Task<Message> ProcessAsync(Message message)
    {
        var outputs = new List<(string, JsonNode?)>();
        var action = Read(message, _action);
        var functions = Read(message, _functions);
        if (action is null && functions is null) return Done(Give(message, outputs));

        var face = new List<string>(); var body = new List<string>();
        foreach (var f in functions?["Functions"] as JsonArray ?? [])
        {
            if (f is not JsonObject o || (string?)o["Behaviour"]?["Name"] is not { } b) continue;
            var t = o["Times"];
            var target = o["Target"] is JsonObject tg ? " towards " + ((string?)tg["ID"] ?? (string?)tg["Kind"]) : "";
            var item = $"{b} {(string?)o["Behaviour"]?["BodyPart"]} {(double?)t?["Start"]:0.00}-{(double?)t?["End"]:0.00} peak {(double?)t?["Peak"]:0.00} amplitude {(double?)o["Behaviour"]?["Amplitude"]:0.00}{target} ({(string?)o["Function"]} {(string?)o["FunctionID"]})";
            (Communicative.IsFace(b) ? face : body).Add(item);
        }
        var keyword = (string?)action?["Action"] ?? "";
        if (keyword == "MM-Animate") body.Add(WalkTarget is { Length: > 0 } place ? $"Walk Body to {place}" : "MM-Animate: the Persona moves in the M-Instance");
        if (face.Count > 0) outputs.Add((_face, Animation("fan", face)));
        if (body.Count > 0) outputs.Add((_body, Animation("ban", body)));
        if (Read(message, _request) is { } rq && Auc.IsRequest(rq))
        {
            var r = AufResponse.Make(rq);
            if (action is not null) r["ActionStatus"] = new JsonArray(new JsonObject { ["Action"] = keyword, ["Status"] = keyword == "MM-Animate" ? "completed" : "failed" });
            outputs.Add((_response, r));
        }
        return Done(Give(message, outputs));
    }
}

// PGM-EFD-V1.0 - Entity Face Description. The Face Descriptors of the A-User: mostly from its speech, with the Face Animation Object and the Face Personal Status when there are.
// The descriptors are a STUB: a note of what the face is to do, in the Data property of the Face Descriptors Object.
public sealed class EfdAim : PgmAim
{
    private readonly string _request, _final, _speech, _motion, _status, _face, _response;

    public EfdAim(string instanceId, AimPortReader ports) : base(instanceId, ports)
    {
        _request = In(Auc.Auf); _final = In("PGM-FRS-V1.0"); _speech = In("OSD-BSO-V1.5"); _motion = In("PAF-AMO-V1.6"); _status = In("MMC-FPS-V2.5");
        _face = Out("PAF-FDO-V1.6"); _response = Out(Auc.Auf);
    }

    public override Task<Message> ProcessAsync(Message message)
    {
        var outputs = new List<(string, JsonNode?)>();
        if (Read(message, _speech) is not null)
        {
            var note = Read(message, _motion)?["AnimationObjectData"] is JsonArray { Count: > 0 } g ? "speaking; " + string.Join("; ", g.Select(i => (string?)i?["Data"])) : "speaking";
            outputs.Add((_face, new JsonObject
            {
                ["Header"] = "PAF-FDO-V1.6", ["FaceDescriptorsObjectID"] = NewId("fdo"),
                ["FaceDescriptorsData"] = new JsonArray(new JsonObject { ["Confidence"] = 1.0, ["Data"] = note })
            }));
        }
        if (Read(message, _request) is { } rq && Auc.IsRequest(rq)) outputs.Add((_response, AufResponse.Make(rq)));
        return Done(Give(message, outputs));
    }
}

// PGM-EBD-V1.0 - Entity Body Description. The Body Descriptors of the A-User: from the Body Animation Object and the Body Personal Status. A STUB: a note of what the body is to do.
public sealed class EbdAim : PgmAim
{
    private readonly string _request, _final, _status, _motion, _body, _response;

    public EbdAim(string instanceId, AimPortReader ports) : base(instanceId, ports)
    {
        _request = In(Auc.Auf); _final = In("PGM-FRS-V1.0"); _status = In("MMC-BPS-V2.5"); _motion = In("PAF-AMO-V1.6");
        _body = Out("PAF-BDO-V1.6"); _response = Out(Auc.Auf);
    }

    public override Task<Message> ProcessAsync(Message message)
    {
        var outputs = new List<(string, JsonNode?)>();
        if (Read(message, _final) is not null)
        {
            var note = Read(message, _motion)?["AnimationObjectData"] is JsonArray { Count: > 0 } g ? string.Join("; ", g.Select(i => (string?)i?["Data"])) : "at rest";
            outputs.Add((_body, new JsonObject
            {
                ["Header"] = "PAF-BDO-V1.6", ["BodyDescriptorsObjectID"] = NewId("bdo"),
                ["BodyDescriptorsData"] = new JsonArray(new JsonObject { ["Confidence"] = 1.0, ["Data"] = note })
            }));
        }
        if (Read(message, _request) is { } rq && Auc.IsRequest(rq)) outputs.Add((_response, AufResponse.Make(rq)));
        return Done(Give(message, outputs));
    }
}

// The response of an AUF Sub-AIM to the Request of A-User Control (PGM-UCF, Mode RS).
public static class AufResponse
{
    public static JsonObject Make(JsonObject request)
    {
        var r = Auc.Make(Auc.Auf, Auc.Response, PgmAim.Session.Of(request), Auc.IdOf(request));
        r["Outcome"] = "achieved";
        return r;
    }
}

// PGM-AUF-V1.0 - A-User Formation. The composite AIM: from the Final Response of Basic Knowledge and the A-User State it forms the Portable Avatar - the speech, the face and the body of
// the A-User - and the Process Actions that are not the Persona's own. It gives them to A-User Control and to nothing else. Inside, in this order: Personal Status Demultiplexing,
// Final Response Demultiplexing, Animation Object Generation, Text-To-Speech (the synthesiser given), Entity Face Description, Entity Body Description, and the multiplexing of the Portable Avatar.
public sealed class AufAim : PgmAim
{
    private readonly string _request, _model, _speechModel, _final, _state, _avatar, _response, _actions, _avatarIdPort;
    private readonly PsdAim _psd; private readonly FrdAim _frd; private readonly BplAim _bpl; private readonly AogAim _aog; private readonly EfdAim _efd; private readonly EbdAim _ebd;
    private readonly ISpeechSynthesiser _voice;
    private readonly string _avatarId;
    private JsonObject? _pendingRequest, _pendingFinal, _avatarModel, _aUserState, _speechModelObject;
    // Each formation forms one Final Response (its Port is required): what it carries - the Text, the Communicative Functions, the Actions - and, if A-User Control asks for no
    // speech (FormationControl Speech not_requested), without the Text.
    private string _actionsFormedFor = "", _processActionsGivenFor = "";

    // What the last formation planned and animated (for a report): the Communicative Functions with their Behaviours and times, the Face and Body Animation Objects.
    public JsonObject? LastPlan { get; private set; }
    public JsonObject? LastFaceAnimation { get; private set; }
    public JsonObject? LastBodyAnimation { get; private set; }
    public double? LastSpeechSeconds { get; private set; }
    public List<string> Trace { get; } = new();   // what A-User Formation formed, in order (for a report)

    public AufAim(string instanceId, AimPortReader ports, PsdAim psd, FrdAim frd, BplAim bpl, AogAim aog, EfdAim efd, EbdAim ebd, ISpeechSynthesiser voice, string avatarId = "a-user-avatar") : base(instanceId, ports)
    {
        (_psd, _frd, _bpl, _aog, _efd, _ebd, _voice, _avatarId) = (psd, frd, bpl, aog, efd, ebd, voice, avatarId);
        _request = In(Auc.Auf); _model = In("OSD-B3O-V1.5"); _speechModel = In("AIP-MLO-V1.0"); _final = In("PGM-FRS-V1.0"); _state = In("MMC-EST-V2.5"); _avatarIdPort = In("OSD-IID-V1.5");
        _avatar = Out("PAF-PAV-V1.6"); _response = Out(Auc.Auf); _actions = Out("MMM-PAT-V2.2");
    }

    public override async Task<Message> ProcessAsync(Message message)
    {
        if (Read(message, _request) is { } rq && Auc.IsRequest(rq)) _pendingRequest = rq;
        if (Read(message, _final) is { } fr) _pendingFinal = fr;
        if (Read(message, _model) is { } m) _avatarModel = m;
        if (Read(message, _speechModel) is { } sm) _speechModelObject = sm;
        if (Read(message, _state) is { } st) _aUserState = st;
        if (_pendingRequest is null || _pendingFinal is null) return Give(message, []);
        var request = _pendingRequest; var final = _pendingFinal;
        _pendingRequest = null; _pendingFinal = null;
        var finalId = (string?)final["FinalResponseID"] ?? "";
        var speechWanted = (string?)request["FormationControl"]?["Speech"] != "not_requested";
        var formActions = _actionsFormedFor != finalId;
        if (formActions) _actionsFormedFor = finalId;
        _aog.WalkTarget = (string?)final["ProcessActions"]?["ProcessActions"]?["MM-Animate"]?["MMAnimatePARQ"]?["RQ3"]?["MLocationID"];

        Dictionary<string, string> With(PgmAim a, params (string Key, JsonNode? Value)[] ports) =>
            ports.Where(p => p.Value is not null).ToDictionary(p => p.Key, p => p.Value!.ToJsonString());
        JsonObject? Take(Message m, PgmAim a, string dataType, int n = 1) => Read(m, a.OutputKey(dataType, n));

        // 1. Personal Status Demultiplexing
        var psd = await _psd.ProcessAsync(new Message
        {
            Ports = With(_psd, (_psd.InputKey("MMC-EST-V2.5"), _aUserState), (_psd.InputKey(Auc.Auf), request))
        });
        // 2. Final Response Demultiplexing
        var frd = await _frd.ProcessAsync(new Message { Ports = With(_frd, (_frd.InputKey(Auc.Auf), request), (_frd.InputKey("PGM-FRS-V1.0"), final)) });
        var others = _processActionsGivenFor == finalId ? null : Take(frd, _frd, "MMM-PAT-V2.2");
        if (others is not null) _processActionsGivenFor = finalId;
        // 3. Text-To-Speech
        var textObject = speechWanted ? Take(frd, _frd, "OSD-BTO-V1.5") ?? final["Text"] as JsonObject : null;
        var words = TextOf(textObject) ?? "";
        var speech = words.Length > 0 ? await _voice.SynthesiseAsync(words) : null;
        Trace.Add($"formed {finalId}: speech {(speechWanted ? "\"" + words + "\"" : "not requested")}, actions {(formActions ? "formed" : "not formed")}");
        LastSpeechSeconds = Communicative.SecondsOf(speech);
        // 4. Behaviour Planning: a Behaviour and its times on the Machine Speech for every Communicative Function (none at Level 0)
        var functions = speechWanted ? Take(frd, _frd, Communicative.Header) : null;
        Message? bpl = null;
        if (functions is not null)
            bpl = await _bpl.ProcessAsync(new Message
            {
                Ports = With(_bpl, (_bpl.InputKey(Auc.Auf), request), (_bpl.InputKey(Communicative.Header), functions), (_bpl.InputKey("OSD-BTO-V1.5"), textObject),
                    (_bpl.InputKey("OSD-BSO-V1.5"), speech), (_bpl.InputKey("OSD-B3O-V1.5"), _avatarModel))
            });
        var plan = bpl is null ? null : Take(bpl, _bpl, Communicative.Header);
        LastPlan = plan;
        // 5. Animation Object Generation
        var aog = await _aog.ProcessAsync(new Message
        {
            Ports = With(_aog, (_aog.InputKey(Auc.Auf), request), (_aog.InputKey("MMM-ACT-V2.2"), formActions ? Take(frd, _frd, "MMM-ACT-V2.2") : null), (_aog.InputKey(Communicative.Header), plan))
        });
        var faceMotion = Take(aog, _aog, "PAF-AMO-V1.6", 1); var bodyMotion = Take(aog, _aog, "PAF-AMO-V1.6", 2);
        LastFaceAnimation = faceMotion; LastBodyAnimation = bodyMotion;
        // 6. Entity Face Description, 7. Entity Body Description
        var efd = await _efd.ProcessAsync(new Message
        {
            Ports = With(_efd, (_efd.InputKey(Auc.Auf), request), (_efd.InputKey("PGM-FRS-V1.0"), final), (_efd.InputKey("OSD-BSO-V1.5"), speech),
                (_efd.InputKey("PAF-AMO-V1.6"), faceMotion), (_efd.InputKey("MMC-FPS-V2.5"), Take(psd, _psd, "MMC-FPS-V2.5")))
        });
        var ebd = await _ebd.ProcessAsync(new Message
        {
            Ports = With(_ebd, (_ebd.InputKey(Auc.Auf), request), (_ebd.InputKey("PGM-FRS-V1.0"), final), (_ebd.InputKey("MMC-BPS-V2.5"), Take(psd, _psd, "MMC-BPS-V2.5")),
                (_ebd.InputKey("PAF-AMO-V1.6"), bodyMotion))
        });

        // 8. Portable Avatar Multiplexing
        var session = Session.Of(request);
        var pav = new JsonObject { ["Header"] = "PAF-PAV-V1.6" };
        pav["MInstanceID"] = session.MInstance;
        pav["PortableAvatarID"] = NewId("pav");
        pav["PortableAvatarTime"] = Time();
        pav["AvatarOrAvatarID"] = new JsonArray(_avatarModel is not null ? _avatarModel.DeepClone() : JsonValue.Create(_avatarId));
        if (_speechModelObject is not null) pav["MLModelOrMLModelID"] = new JsonArray(_speechModelObject.DeepClone());
        if (speech is not null) pav["SPOOrSPOID"] = new JsonArray(speech);
        if (Take(efd, _efd, "PAF-FDO-V1.6") is { } fdo) pav["FaceDescriptorsObject"] = fdo;
        if (Take(ebd, _ebd, "PAF-BDO-V1.6") is { } bdo) pav["BodyDescriptorsObject"] = bdo;

        var response = AufResponse.Make(request);
        response["RenderState"] = "rendered";
        response["PortableAvatarID"] = (string)pav["PortableAvatarID"]!;
        response["ElementOutcomes"] = new JsonObject
        {
            ["SpeechSynthesis"] = new JsonObject { ["Outcome"] = speech is null ? "not_requested" : "success" },
            ["FaceDescription"] = new JsonObject { ["Outcome"] = pav.ContainsKey("FaceDescriptorsObject") ? "success" : "not_requested" },
            ["BodyDescription"] = new JsonObject { ["Outcome"] = pav.ContainsKey("BodyDescriptorsObject") ? "success" : "not_requested" },
            ["AvatarSynthesis"] = new JsonObject { ["Outcome"] = "success" }
        };
        if (Read(aog, _aog.OutputKey(Auc.Auf)) is { } animated && animated["ActionStatus"] is JsonNode status) response["ActionStatus"] = status.DeepClone();
        // The report on each Communicative Function, from Behaviour Planning.
        if (bpl is not null && Read(bpl, _bpl.OutputKey(Auc.Auf)) is { } planned)
        {
            if (planned["FunctionStatus"] is JsonNode fs) response["FunctionStatus"] = fs.DeepClone();
            if ((string?)planned["Outcome"] == "achieved_differently") { response["Outcome"] = "achieved_differently"; response["Reason"] = planned["Reason"]?.DeepClone(); }
        }

        return Give(message, [(_avatar, pav), (_response, response), (_actions, others)]);
    }
}
