"""N-34 step 3 (night 2026-11-18): IDR of Television Media Analysis confirms a voice by a face of the same person seen during the utterance."""
from pathlib import Path

P = Path(r"D:\AI\AIMs\OSD\V1.5\TMA\TmaAim.cs")
raw = P.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")
old = """    public override IEnumerable<(string DataType, int Port, JsonObject Json)> Handle(string dataType, int port, JsonObject json)
    {
        if (port == 1) { lastFace = json; yield return (TmaTypes.Iid, 1, (JsonObject)json.DeepClone()); yield break; }
        var reconciled = (JsonObject)json.DeepClone();
        var (voice, a) = Top(json);
        var (face, b) = Top(lastFace);
        if (voice is not null && voice == face && reconciled["InstanceIdentifierData"]?[0] is JsonObject top) top["LabelConfidenceLevel"] = 1 - (1 - a) * (1 - b);
        yield return (TmaTypes.Iid, 1, reconciled);
    }"""
new = """    // The faces seen, with their programme time (2026-11-18: a picture now has every face, frame by frame): a voice is confirmed by a face of the same person seen during
    // its utterance (from half a second before to half a second after); an identity without a time is compared with the face last seen, as before.
    private readonly List<(string Label, double Time, double Confidence)> seen = [];

    private static (double From, double To)? Span(JsonNode? time) =>
        time?["SimpleTimeData"]?[0] is JsonObject d && d["StartTime"] is JsonValue a && a.TryGetValue<double>(out var f) && d["EndTime"] is JsonValue b && b.TryGetValue<double>(out var t) ? (f, t) : null;

    public override IEnumerable<(string DataType, int Port, JsonObject Json)> Handle(string dataType, int port, JsonObject json)
    {
        if (port == 1)
        {
            lastFace = json;
            var (who, c) = Top(json);
            if (who is not null && Span(json["InstanceTime"]) is { } at) seen.Add((who, at.From, c));
            yield return (TmaTypes.Iid, 1, (JsonObject)json.DeepClone()); yield break;
        }
        var reconciled = (JsonObject)json.DeepClone();
        var (voice, a) = Top(json);
        var (face, b) = Top(lastFace);
        if (voice is not null && Span(json["InstanceTime"]) is { } spoken)
        {
            var during = seen.Where(f => f.Label == voice && f.Time >= spoken.From - 0.5 && f.Time <= spoken.To + 0.5).ToList();
            (face, b) = during.Count > 0 ? (voice, during.Max(f => f.Confidence)) : (null, 0);
        }
        if (voice is not null && voice == face && reconciled["InstanceIdentifierData"]?[0] is JsonObject top) top["LabelConfidenceLevel"] = 1 - (1 - a) * (1 - b);
        yield return (TmaTypes.Iid, 1, reconciled);
    }"""
assert s.count(old) == 1
s = s.replace(old, new)
P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
