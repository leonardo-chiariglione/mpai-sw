using System;
using System.Linq;
using System.Text.Json.Nodes;

namespace Mpai.Xrv;

// WHEN DOES A STATUS MEET THE TARGET OF A CUE? The simplest reading: every element the target names is in the Status, and equal. An object of the target is met by an
// object of the Status that has all its properties met; an array of the target is met by an array of the Status that has, for each element of the target, an element that meets it;
// a value by an equal value (numbers by their value). What identifies an instance of a nested Object (its Header, its own ID, its Time, the MInstanceID, the UEnvironmentID, the
// DataXMData and the DescrMetadata) is not a Status element and is not compared.
// AN INTENSITY (N-26, 2026-11-15): an element whose name ends in "Intensity" is on a scale of 0 to 100 (Clapping, Booing, Speaking, a Gesture...); a target names how strong it must at
// least be, so it is met by a value at least as high. The Status schemas name the scale, not a way to name a threshold: this is the simplest reading.
// A LIST OF NUMBERS (N-26) is a tuple - a CartPosition, an Orientation - and is met element by element, in order; other lists as above.
public static class JsonMatch
{
    public static bool Meets(JsonNode? target, JsonNode? current) => Meets(null, target, current);

    private static bool Meets(string? key, JsonNode? target, JsonNode? current)
    {
        switch (target)
        {
            case null: return current is null;
            case JsonObject t:
                if (current is not JsonObject c) return false;
                var headered = t.ContainsKey("Header");
                foreach (var (name, value) in t)
                {
                    if (Ignored(name, headered)) continue;
                    if (!c.TryGetPropertyValue(name, out var there) || !Meets(name, value, there)) return false;
                }
                return true;
            case JsonArray ta:
                if (current is not JsonArray ca) return false;
                // A list of numbers is a tuple (a position, an orientation): met element by element, in order (N-26: [0,0,0] is not met by [180,0,0]).
                if (ta.Count > 0 && ta.All(x => x is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.Number))
                    return ca.Count == ta.Count && ta.Select((x, i) => Meets(key, x, ca[i])).All(m => m);
                return ta.All(x => ca.Any(y => Meets(key, x, y)));
            default:
                if (current is not JsonValue) return false;
                if (target.GetValueKind() == System.Text.Json.JsonValueKind.Number && current.GetValueKind() == System.Text.Json.JsonValueKind.Number)
                    return key is not null && key.EndsWith("Intensity", StringComparison.Ordinal)
                        ? current.GetValue<double>() >= target.GetValue<double>()
                        : target.GetValue<double>() == current.GetValue<double>();
                return JsonNode.DeepEquals(target, current);
        }
    }

    private static bool Ignored(string key, bool headered) =>
        key is "Header" or "MInstanceID" or "UEnvironmentID" or "DataXMData" or "DescrMetadata"
        || (headered && (key.EndsWith("Time") || key.EndsWith("SpaceTime") || (key.EndsWith("ID") && key != "ControllerID")));
}
