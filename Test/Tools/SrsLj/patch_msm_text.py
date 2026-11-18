"""N-34 (night 2026-11-18): the Text Qualifier of a recognised text recorded in an utterance is given the schema's SubTypes and Formats (the typed model of Mpai.Core writes
SubType and Format, which the schema TFA-TXQ-V1.5 does not know)."""
from pathlib import Path

P = Path(r"D:\AI\AIMs\OSD\V1.5\TMA\TmaAim.cs")
raw = P.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")
old = """        content["TextObject"] = new JsonObject
        {
            ["Header"] = "OSD-TXO-V1.5", ["TextObjectID"] = TmaTypes.NewId("txo"), ["TextObjectSpaceTime"] = spaceTime.DeepClone(), ["BasicTextObjectCount"] = 1,
            ["BasicTextObjects"] = new JsonArray(new JsonObject { ["BasicTextObjectSpaceTime"] = spaceTime, ["BTObjectIDOrBTObject"] = new JsonArray(what.DeepClone()) })
        };"""
new = """        content["TextObject"] = new JsonObject
        {
            ["Header"] = "OSD-TXO-V1.5", ["TextObjectID"] = TmaTypes.NewId("txo"), ["TextObjectSpaceTime"] = spaceTime.DeepClone(), ["BasicTextObjectCount"] = 1,
            ["BasicTextObjects"] = new JsonArray(new JsonObject { ["BasicTextObjectSpaceTime"] = spaceTime, ["BTObjectIDOrBTObject"] = new JsonArray(SchemaText(what)) })
        };
    }

    // THE TEXT AS ITS SCHEMA HAS IT (2026-11-18). The Text Qualifier written by the typed model of Mpai.Core (Speech Recognition gives it) names its parts SubType and Format,
    // where TFA-TXQ-V1.5 requires SubTypes and Formats: they are written under the schema's names (the content of each is the same; a SubType that holds nothing is an empty
    // SubTypes). Nothing else of the text changes. The model's names are a defect reported in MKG\\pending-changes.md.
    private static JsonObject SchemaText(JsonObject text)
    {
        var copy = (JsonObject)text.DeepClone();
        if (copy["TextQualifier"] is JsonObject q)
        {
            if (q["SubTypes"] is null) { var sub = q["SubType"]; q.Remove("SubType"); q["SubTypes"] = sub is JsonObject o ? o.DeepClone() : new JsonObject(); }
            if (q["Formats"] is null) { var format = q["Format"]; q.Remove("Format"); q["Formats"] = format is JsonObject f ? f.DeepClone() : new JsonObject(); }
        }
        return copy;"""
assert s.count(old) == 1
s = s.replace(old, new)
P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
