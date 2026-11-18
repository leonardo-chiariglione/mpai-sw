using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Mpai.Ua;

// THE TABLE OF DAMAGED SECTIONS a person fills in for the Speech Restoration System (CAE-USC V2.5): each row a section of the damaged recording - from and to, in
// seconds - and the words said there. The table gives two data of the Module's boundary: the Damaged List (CAE-DLS-V2.5: the Time of each section as a Simple Time,
// from left to right) and the Text List (OSD-BTO-V1.5: one text for each section, in the same order). A client keeps the words when it gives the Damaged List
// and gives them when the workflow then asks for the words (BasicTextObjectID "DamagedSectionWords").
public static class DamagedSections
{
    public sealed record Row(double From, double To, string Words);

    // The marker of the workflow's acquisition of the words of the table.
    public const string WordsMarker = "DamagedSectionWords";

    // The rows that make a table, or why they do not: each from before to, each with words, from left to right without overlap.
    public static string? Problem(IReadOnlyList<Row> rows)
    {
        if (rows.Count == 0) return "Mark at least one damaged section.";
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.From < 0 || r.To <= r.From) return $"Section {i + 1}: it must end after it starts (from {r.From:0.##} s, to {r.To:0.##} s).";
            if (string.IsNullOrWhiteSpace(r.Words)) return $"Section {i + 1}: write the words said there.";
            if (i > 0 && r.From < rows[i - 1].To) return $"Section {i + 1} starts before section {i} ends: the sections go from left to right.";
        }
        return null;
    }

    public static string DamagedList(IReadOnlyList<Row> rows) => new JsonObject
    {
        ["Header"] = "CAE-DLS-V2.5", ["DamagedListID"] = "damaged",
        ["DamagedListData"] = new JsonArray(rows.Select(r => (JsonNode)new JsonObject
        {
            ["DamagedSectionTime"] = new JsonObject
            {
                ["Header"] = "OSD-TIM-V1.5", ["TimeID"] = "t", ["Data"] = $"{r.From.ToString(CultureInfo.InvariantCulture)} {r.To.ToString(CultureInfo.InvariantCulture)}",
                ["Qualifier"] = new JsonObject
                {
                    ["Header"] = "TFA-TIQ-V1.5", ["TimeQualifierID"] = "tq", ["SubTypes"] = new JsonObject(),
                    ["Formats"] = new JsonObject { ["MPAIFormat"] = new JsonObject
                    {
                        ["Header"] = "OSD-STM-V1.5", ["SimpleTimeID"] = "st",
                        ["SimpleTimeData"] = new JsonArray(new JsonObject { ["FlagsByte"] = 0, ["StartTime"] = r.From, ["EndTime"] = r.To, ["AccuracyMode"] = "single", ["AccuracyPlusMinus"] = 0 })
                    } }
                }
            }
        }).ToArray())
    }.ToJsonString();

    public static string TextList(IReadOnlyList<Row> rows) => new JsonObject
    {
        ["Header"] = "OSD-BTO-V1.5", ["BasicTextObjectID"] = "texts",
        ["BasicTextData"] = new JsonArray(rows.Select(r => (JsonNode)new JsonObject { ["Data"] = r.Words.Trim() }).ToArray())
    }.ToJsonString();

    // The sections a ready-made recording comes with (the file beside it, <name>.json: { "sections": [ { "from", "to", "words" } ] }), or none.
    public static IReadOnlyList<Row> FromSidecar(string json)
    {
        try
        {
            return (JsonNode.Parse(json)?["sections"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Select(s => new Row(s["from"]!.GetValue<double>(), s["to"]!.GetValue<double>(), s["words"]?.GetValue<string>() ?? ""))
                .ToList();
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or NullReferenceException) { return []; }
    }

    // The address of the file beside a ready-made recording that holds its sections.
    public static string SidecarOf(string uri) => uri.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? uri[..^4] + ".json" : uri + ".json";

    // AN ADDRESS A WORKFLOW GIVES RELATIVE (Apps/SRS/Files/...) is a file the Service that offers the App serves: it is resolved against the Service's address.
    public static string Resolve(string uri, string? service) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https" or "file"
            ? uri
            : service is { Length: > 0 } ? new Uri(new Uri(service.EndsWith('/') ? service : service + "/"), uri).ToString() : uri;
}
