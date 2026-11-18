using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using AIF.Controller;

namespace Mpai.Pgm;

// The Communicative Functions of PGM-AUA (PGM-CFN-V1.0, and the CommunicativeFunctions element of the Final Response): the closed lists of the schema and the rules that BKN, Behaviour
// Planning and the conformance checks share. The lists are those of D:\AI\schemas\PGM1\V1.0\data\CommunicativeFunctions.json; a test compares them with the schema.
public static class Communicative
{
    public const string Header = "PGM-CFN-V1.0";

    // Function -> the Behaviours that may realise it, the first being the one Behaviour Planning prefers.
    public static readonly IReadOnlyDictionary<string, string[]> BehavioursOf = new Dictionary<string, string[]>
    {
        ["Gaze"] = ["LookAt", "TurnTowards"],
        ["Deictic"] = ["Point", "HeadPoint", "LookAt"],
        ["Greeting"] = ["Wave", "Nod", "Bow", "Smile"],
        ["Affirmation"] = ["Nod", "Smile"],
        ["Negation"] = ["HeadShake", "Frown"],
        ["Beat"] = ["Stroke", "Nod", "RaiseEyebrows"],
        ["Iconic"] = ["Trace", "Frame"],
        ["Metaphoric"] = ["PalmUp", "Frame", "Trace"],
        ["Uncertainty"] = ["Shrug", "HeadTilt", "PalmUp"],
        ["Attention"] = ["LookAt", "LeanForward", "HeadTilt", "Nod"],
        ["Invitation"] = ["PalmUp", "HoldOut", "Nod", "LookAt"],
        ["Stop"] = ["StopPalm", "HeadShake"]
    };

    public static readonly string[] Functions = BehavioursOf.Keys.ToArray();

    public static readonly string[] Behaviours =
        ["LookAt", "TurnTowards", "Point", "HeadPoint", "Nod", "HeadShake", "HeadTilt", "Wave", "Bow", "Smile", "RaiseEyebrows", "Frown", "Shrug", "PalmUp", "HoldOut", "StopPalm", "Stroke", "Trace", "Frame", "LeanForward"];

    // What each Function means, for the prompt of the language model (the descriptions of the schema, shortened).
    public static readonly IReadOnlyDictionary<string, string> Meaning = new Dictionary<string, string>
    {
        ["Gaze"] = "look at the target", ["Deictic"] = "indicate the target (an item or a place)", ["Greeting"] = "greet or say goodbye to the target",
        ["Affirmation"] = "say yes or agree", ["Negation"] = "say no or disagree", ["Beat"] = "stress important words", ["Iconic"] = "depict a shape, size or movement",
        ["Metaphoric"] = "give a concrete form to an abstract idea", ["Uncertainty"] = "show doubt", ["Attention"] = "show that you listen to the target",
        ["Invitation"] = "invite the target to act or to speak", ["Stop"] = "ask the target to stop or wait"
    };

    // The body part of each Behaviour, and whether it is a movement of the face (Face Animation Object) or of the body (Body Animation Object).
    public static string BodyPartOf(string behaviour) => behaviour switch
    {
        "LookAt" => "Eyes",
        "Smile" or "RaiseEyebrows" or "Frown" => "Face",
        "Nod" or "HeadShake" or "HeadTilt" or "HeadPoint" => "Head",
        "Shrug" => "Shoulders",
        "Bow" or "LeanForward" or "TurnTowards" => "Torso",
        "Frame" => "BothHands",
        _ => "RightHand"
    };

    public static bool IsFace(string behaviour) => BodyPartOf(behaviour) is "Eyes" or "Face";

    public static string[] Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    // The Level of a Final Response: its FunctionLevel, 0 when absent.
    public static int LevelOf(JsonObject finalResponse) => (int?)finalResponse["FunctionLevel"] ?? 0;

    // CONFORMANCE of a Final Response (M3308 3.5): its Level is not higher than the Level requested; every Function is allowed; every Anchor is within the Text; every Target is in
    // the PC Prompt; every Behaviour is in the closed list of its Function (the schema checks the lists themselves). Empty: conforming; otherwise the reasons.
    public static List<string> Check(JsonObject finalResponse, int levelRequested, IEnumerable<string> functionsAllowed, IEnumerable<string> targets)
    {
        var problems = new List<string>();
        var level = LevelOf(finalResponse);
        if (level > levelRequested) problems.Add($"Level {level} is higher than the Level requested ({levelRequested})");
        var allowed = functionsAllowed.ToHashSet();
        var known = targets.ToHashSet();
        var words = Words(PgmAim.TextOf(finalResponse["Text"] as JsonObject) ?? "").Length;
        foreach (var f in finalResponse["CommunicativeFunctions"] as JsonArray ?? [])
        {
            if (f is not JsonObject o) continue;
            var id = (string?)o["FunctionID"] ?? "?";
            var name = (string?)o["Function"] ?? "";
            if (!allowed.Contains(name)) problems.Add($"{id}: the Function {name} is not allowed");
            if ((string?)o["Anchor"]?["Position"] == "During")
            {
                var first = (int?)o["Anchor"]?["FirstWord"] ?? -1; var last = (int?)o["Anchor"]?["LastWord"] ?? -1;
                if (first < 0 || last < first || last >= words) problems.Add($"{id}: the Anchor {first}-{last} is not within the {words} words of the Text");
            }
            if (o["Target"] is JsonObject t && (string?)t["Kind"] != "User" && !known.Contains((string?)t["ID"] ?? "")) problems.Add($"{id}: the Target {(string?)t["ID"]} is not in the PC Prompt");
            if ((string?)o["Behaviour"]?["Name"] is { } b && !(BehavioursOf.TryGetValue(name, out var list) && list.Contains(b))) problems.Add($"{id}: the Behaviour {b} is not in the list of {name}");
            if (level < 2 && o.ContainsKey("Behaviour")) problems.Add($"{id}: a Behaviour in a Level {level} Final Response");
        }
        return problems;
    }

    // ---- the times of the words -------------------------------------------------------------------------------------------------------------------

    // How long a Speech Object lasts, from its Data and its Qualifier: a WAV header if there is one, otherwise the raw sampling frequency and precision; null if neither says.
    public static double? SecondsOf(JsonObject? speech)
    {
        if (speech is null) return null;
        byte[] data;
        try
        {
            data = speech["Data"] is JsonValue v && v.TryGetValue<string>(out var b64) ? Convert.FromBase64String(b64)
                 : speech["BasicSpeechData"]?[0]?["Data"] is JsonValue w && w.TryGetValue<string>(out var b) ? Convert.FromBase64String(b) : [];
        }
        catch (FormatException) { return null; }
        if (data.Length == 0) return null;
        if (data.Length > 44 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F')
        {
            var byteRate = BitConverter.ToInt32(data, 28);
            return byteRate > 0 ? (data.Length - 44) / (double)byteRate : null;
        }
        var rate = Find(speech, "SamplingFrequency"); var bits = Find(speech, "Precision");
        return rate is > 0 && bits is > 0 ? data.Length / (rate.Value * bits.Value / 8.0) : null;
    }

    private static double? Find(JsonNode? node, string name)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var (k, v) in o)
                {
                    if (k == name && v is JsonValue jv && jv.TryGetValue<double>(out var d)) return d;
                    if (Find(v, name) is { } found) return found;
                }
                return null;
            case JsonArray a:
                foreach (var v in a) if (Find(v, name) is { } found) return found;
                return null;
            default: return null;
        }
    }

    // The samples of a Speech Object (16-bit PCM, mono): from a WAV file in its Data (its own rate), or raw PCM at the Qualifier's sampling frequency; null if it is neither.
    public static short[]? SamplesOf(JsonObject? speech, out int rate)
    {
        rate = 0;
        if (speech is null) return null;
        byte[] data;
        try
        {
            data = speech["Data"] is JsonValue v && v.TryGetValue<string>(out var b64) ? Convert.FromBase64String(b64)
                 : speech["BasicSpeechData"]?[0]?["Data"] is JsonValue w && w.TryGetValue<string>(out var b) ? Convert.FromBase64String(b)
                 : speech["BasicSpeechObjectData"]?[0]?["Data"] is JsonValue x && x.TryGetValue<string>(out var c) ? Convert.FromBase64String(c) : [];
        }
        catch (FormatException) { return null; }
        if (data.Length < 4) return null;
        int start, channels = 1, bits = 16, length;
        if (data.Length > 44 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F')
        {
            var at = 12; start = -1; length = 0;
            while (at + 8 <= data.Length)
            {
                var id = System.Text.Encoding.ASCII.GetString(data, at, 4); var size = BitConverter.ToInt32(data, at + 4);
                if (id == "fmt ") { channels = BitConverter.ToInt16(data, at + 10); rate = BitConverter.ToInt32(data, at + 12); bits = BitConverter.ToInt16(data, at + 22); }
                else if (id == "data") { start = at + 8; length = Math.Min(size, data.Length - start); break; }
                at += 8 + size + (size & 1);
            }
            if (start < 0 || bits != 16 || channels < 1 || rate <= 0) return null;
        }
        else
        {
            rate = (int)(Find(speech, "SamplingFrequency") ?? 0);
            bits = (int)(Find(speech, "Precision") ?? Find(speech, "SamplePrecision") ?? 16);
            if (rate <= 0 || bits != 16) return null;
            start = 0; length = data.Length;
        }
        var frames = length / (2 * channels);
        var samples = new short[frames];
        for (var i = 0; i < frames; i++) samples[i] = BitConverter.ToInt16(data, start + i * 2 * channels);
        return samples;
    }

    // THE WORDS ALIGNED WITH THE MACHINE SPEECH ITSELF (M3308 Q4: the word times are computed in A-User Formation). The speech is read as 10 ms frames: where its level is far below its
    // loud part it is silent. The silence before the first word and after the last is given to no word; each pause inside the speech (120 ms or more of silence) falls between two words -
    // the word boundary whose share of the letters before it is closest to the share of the spoken time before the pause, a boundary after a punctuation mark preferred; within each
    // stretch of speech between pauses the time is shared among its words in proportion to their letters. Where the speech cannot be read, its duration is shared by the letters alone.
    public static (double Start, double End)[] WordTimes(string text, short[] samples, int rate)
    {
        var words = Words(text);
        if (words.Length == 0) return [];
        if (samples.Length == 0 || rate <= 0) return WordTimes(text, 0.5);
        var hop = Math.Max(1, rate / 100);
        var count = samples.Length / hop;
        if (count < 3) return WordTimes(text, samples.Length / (double)rate);
        var db = new double[count];
        for (var f = 0; f < count; f++)
        {
            double sum = 0;
            for (var i = f * hop; i < (f + 1) * hop; i++) { var v = samples[i] / 32768.0; sum += v * v; }
            db[f] = 10 * Math.Log10(sum / hop + 1e-12);
        }
        var loud = db.OrderByDescending(d => d).Skip(Math.Min(count - 1, count / 20)).First();      // the 95th percentile, not a click
        var threshold = Math.Max(-50, loud - 30);
        var voiced = db.Select(d => d > threshold).ToArray();
        var first = Array.IndexOf(voiced, true); var last = Array.LastIndexOf(voiced, true);
        if (first < 0) return WordTimes(text, samples.Length / (double)rate);
        // gaps shorter than 80 ms are inside words (stops, closures)
        var gap = 0;
        for (var f = first; f <= last; f++)
        {
            if (!voiced[f]) { gap++; continue; }
            if (gap > 0 && gap < 8) for (var k = f - gap; k < f; k++) voiced[k] = true;
            gap = 0;
        }
        // the stretches of speech between pauses of 120 ms or more
        var stretches = new List<(int From, int To)>();
        var from = first; gap = 0;
        for (var f = first; f <= last; f++)
        {
            if (!voiced[f]) { gap++; continue; }
            if (gap >= 12) { stretches.Add((from, f - gap)); from = f; }
            gap = 0;
        }
        stretches.Add((from, last + 1));
        var cumulative = new double[words.Length + 1];
        for (var i = 0; i < words.Length; i++) cumulative[i + 1] = cumulative[i] + words[i].Count(char.IsLetterOrDigit) + 1;
        double spoken = stretches.Sum(s => s.To - s.From);
        // each pause between stretch k and k+1 goes to a word boundary b (1..N-1), in order; a pause that finds no boundary left joins the stretches around it
        var bounds = new List<int> { 0 };
        var groups = new List<(int From, int To)> { stretches[0] };
        double before = 0;
        for (var k = 0; k + 1 < stretches.Count; k++)
        {
            before += stretches[k].To - stretches[k].From;
            var share = before / spoken;
            int best = -1; var bestCost = double.MaxValue;
            for (var b = bounds[^1] + 1; b < words.Length; b++)
            {
                var cost = Math.Abs(cumulative[b] / cumulative[^1] - share) - (",.;:!?".Contains(words[b - 1][^1]) ? 0.05 : 0);
                if (cost < bestCost) { bestCost = cost; best = b; }
            }
            if (best < 0) { groups[^1] = (groups[^1].From, stretches[k + 1].To); continue; }
            bounds.Add(best); groups.Add(stretches[k + 1]);
        }
        bounds.Add(words.Length);
        var times = new (double, double)[words.Length];
        for (var k = 0; k < groups.Count; k++)
        {
            var (a, z) = groups[k];
            int w0 = bounds[k], w1 = bounds[k + 1];
            var total = cumulative[w1] - cumulative[w0];
            for (var i = w0; i < w1; i++)
                times[i] = (Math.Round((a + (z - a) * (cumulative[i] - cumulative[w0]) / total) * hop / (double)rate, 3),
                            Math.Round((a + (z - a) * (cumulative[i + 1] - cumulative[w0]) / total) * hop / (double)rate, 3));
        }
        return times;
    }

    // The start and end of each word of the Text in the Machine Speech, from its duration alone (where the speech cannot be read). THE ALIGNMENT OF THE REFERENCE SOFTWARE until 2026-11-18: the duration of the speech shared among the words in proportion to
    // their letters (plus a short pause after a punctuation mark). It is the alignment M3308 Q4 leaves to A-User Formation; a forced aligner on the speech would replace it.
    public static (double Start, double End)[] WordTimes(string text, double seconds)
    {
        var words = Words(text);
        if (words.Length == 0) return [];
        double Weight(string w) => w.Count(char.IsLetterOrDigit) + 1 + (w.Length > 0 && ",.;:!?".Contains(w[^1]) ? 2 : 0);
        var total = words.Sum(Weight);
        var times = new (double, double)[words.Length];
        var t = 0.0;
        for (var i = 0; i < words.Length; i++)
        {
            var d = seconds * Weight(words[i]) / total;
            times[i] = (Math.Round(t, 3), Math.Round(t + d, 3));
            t += d;
        }
        return times;
    }
}

// PGM-BPL-V1.0 - Behaviour Planning, a Sub-AIM of A-User Formation. For each Communicative Function it chooses the Behaviour that realises it on this Persona (or checks the one given at
// Level 2), times it on the words of its Anchor in the Machine Speech, and reports Realised, Modified or Not Realised with the reason. The Persona's Behaviours are a setting of the
// implementation (Persona.Behaviours, all of the closed list if absent); the Avatar Model is received but not read for them in the reference software.
public sealed class BplAim : PgmAim
{
    public const string BehavioursSetting = "Persona.Behaviours";

    private readonly string _request, _functions, _text, _speech, _model, _out, _response;
    private readonly HashSet<string> _persona;

    public BplAim(string instanceId, AimPortReader ports, IEnumerable<string>? personaBehaviours = null) : base(instanceId, ports)
    {
        _persona = (personaBehaviours ?? Communicative.Behaviours).ToHashSet();
        _request = In(Auc.Auf); _functions = In(Communicative.Header); _text = In("OSD-BTO-V1.5"); _speech = In("OSD-BSO-V1.5"); _model = In("OSD-B3O-V1.5");
        _out = Out(Communicative.Header); _response = Out(Auc.Auf);
    }

    public IReadOnlyCollection<string> PersonaBehaviours => _persona;

    public override Task<Message> ProcessAsync(Message message)
    {
        var outputs = new List<(string, JsonNode?)>();
        if (Read(message, _functions) is not { } cfn) return Done(Give(message, outputs));

        var text = TextOf(Read(message, _text)) ?? "";
        var words = Communicative.Words(text);
        var speech = Read(message, _speech);
        var seconds = Communicative.SecondsOf(speech) ?? Math.Max(0.5, words.Length * 0.4);
        // the words aligned with the Machine Speech where its samples can be read; otherwise its duration shared by the letters
        var times = Communicative.SamplesOf(speech, out var rate) is { Length: > 0 } samples ? Communicative.WordTimes(text, samples, rate) : Communicative.WordTimes(text, seconds);

        var planned = new JsonArray();
        var status = new JsonArray();
        foreach (var f in cfn["Functions"] as JsonArray ?? [])
        {
            if (f is not JsonObject function) continue;
            var id = (string?)function["FunctionID"] ?? NewId("fn");
            var name = (string?)function["Function"] ?? "";
            var required = (string?)function["Requirement"] == "Required";
            var given = (string?)function["Behaviour"]?["Name"];
            var candidates = Communicative.BehavioursOf.TryGetValue(name, out var list) ? list : [];

            // The Behaviour: the one given if the Persona can perform it, otherwise the first of the list of the Function that the Persona can perform. A Deictic towards the User is
            // better a look than a pointing finger.
            var ordered = name == "Deictic" && (string?)function["Target"]?["Kind"] == "User" ? candidates.OrderBy(c => c == "LookAt" ? 0 : 1).ToArray() : candidates;
            string? chosen; string state; string? reason = null;
            if (given is not null && _persona.Contains(given)) { chosen = given; state = "Realised"; }
            else
            {
                chosen = ordered.FirstOrDefault(_persona.Contains);
                if (chosen is null) { state = "NotRealised"; reason = $"the Persona has none of the Behaviours of {name} ({string.Join(", ", candidates)})"; }
                else if (given is not null) { state = "Modified"; reason = $"the Persona cannot perform {given}; {chosen} instead"; }
                else state = "Realised";
            }

            var entry = new JsonObject { ["FunctionID"] = id, ["Status"] = state };
            if (chosen is not null) entry["Behaviour"] = chosen;
            if (reason is not null) entry["Reason"] = (required ? "" : "optional; ") + reason;
            status.Add(entry);
            if (chosen is null) continue;

            // The times: on the anchored words, a little ahead of them (a gesture anticipates its word); before or after the utterance otherwise.
            var position = (string?)function["Anchor"]?["Position"] ?? "During";
            double start, peak, end;
            if (position == "During" && times.Length > 0)
            {
                var first = Math.Clamp((int?)function["Anchor"]?["FirstWord"] ?? 0, 0, times.Length - 1);
                var last = Math.Clamp((int?)function["Anchor"]?["LastWord"] ?? first, first, times.Length - 1);
                var peakWord = Math.Clamp((int?)function["Behaviour"]?["PeakWord"] ?? first, first, last);
                start = Math.Max(0, times[first].Start - 0.2); peak = (times[peakWord].Start + times[peakWord].End) / 2; end = times[last].End + 0.2;
            }
            else if (position == "After") { start = seconds; peak = seconds + 0.4; end = seconds + 0.8; }
            else { start = -0.8; peak = -0.4; end = 0; }

            var p = (JsonObject)function.DeepClone();
            var intensity = (double?)function["Intensity"] ?? 0.5;
            p["Behaviour"] = new JsonObject
            {
                ["Name"] = chosen, ["BodyPart"] = Communicative.BodyPartOf(chosen), ["TowardsTarget"] = function.ContainsKey("Target"),
                ["Amplitude"] = Math.Round(0.3 + 0.7 * intensity, 2)
            };
            if ((int?)function["Behaviour"]?["PeakWord"] is { } pw) ((JsonObject)p["Behaviour"]!)["PeakWord"] = pw;
            p["Times"] = new JsonObject { ["Start"] = Math.Round(start, 3), ["Peak"] = Math.Round(peak, 3), ["End"] = Math.Round(end, 3) };
            planned.Add(p);
        }

        var result = new JsonObject { ["Header"] = Communicative.Header };
        foreach (var k in new[] { "MInstance", "UEnvironment", "SessionID" }) if (cfn[k] is { } v) result[k] = v.DeepClone();
        result["CommunicativeFunctionsID"] = NewId("cfn");
        result["CommunicativeFunctionsTime"] = Time();
        if (cfn["FinalResponseID"] is { } fr) result["FinalResponseID"] = fr.DeepClone();
        result["Level"] = 2;   // every function now carries its Behaviour
        result["Functions"] = planned;
        outputs.Add((_out, result));

        if (Read(message, _request) is { } rq && Auc.IsRequest(rq))
        {
            var r = AufResponse.Make(rq);
            r["FunctionStatus"] = status;
            if (status.Any(s => (string?)s!["Status"] == "NotRealised" && !((string?)s["Reason"] ?? "").StartsWith("optional")))
            {
                r["Outcome"] = "achieved_differently";
                r["Reason"] = new JsonObject { ["Code"] = "constraint_conflict", ["Description"] = "a Required Communicative Function was not realised" };
            }
            outputs.Add((_response, r));
        }
        return Done(Give(message, outputs));
    }
}
