using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Mpai.Ua;

// THE COMPANY OF COMPANY PERFORMANCE PREDICTION (MPAI-CUI CPP V2.1), a device of the User Agent: what the person chooses, given as it is chosen - a company file
// (PredictionHorizon, FinancialDescriptors CUI-FND, GovernanceDescriptors CUI-GVS and, where it has them, ClimateRiskDescriptors CUI-LRD, CyberRiskDescriptors CUI-CRD,
// SecondaryRiskStatements CUI-SRS) part by part, the Prediction Horizon (number, months), and WHAT-IF: five figures of the Financial Descriptors it holds moved by a
// fraction, the Financial Descriptors given again. The workflow binds it to the Module's Ports ("stream ... from device "Company""); the Module computes again on what changes.
public sealed class CompanyDevice : IDevice
{
    // The five figures a person may move, by their code in the Financial Descriptors, and what they are in plain words.
    public static readonly (string Code, string Name)[] KeyFigures =
    [
        ("CEA1", "Revenues"), ("CEBDIFFVAL", "Operating result (EBIT)"), ("SPPDTOT", "Total debts"), ("SPPATOT", "Equity"), ("CEC3", "Interest and other financial expenses")
    ];

    public static readonly (string DataType, string Part)[] Parts =
    [
        ("CUI-FND-V2.1", "FinancialDescriptors"), ("CUI-GVS-V2.1", "GovernanceDescriptors"), ("CUI-LRD-V2.1", "ClimateRiskDescriptors"),
        ("CUI-CRD-V2.1", "CyberRiskDescriptors"), ("CUI-SRS-V2.1", "SecondaryRiskStatements")
    ];

    private readonly Channel<(string DataType, string Json)> given = Channel.CreateUnbounded<(string, string)>();
    private JsonObject? financial;

    public string? Name { get; private set; }
    public JsonObject? Company { get; private set; }
    public double Months { get; private set; } = 12;

    // A company chosen: each part it has, and the horizon (the file's, unless one was chosen). Returns what is wrong with it, or null.
    public string? Give(JsonObject company, string name, double? months = null)
    {
        if (Problem(company) is { } why) return why;
        Company = (JsonObject)company.DeepClone(); Name = name;
        financial = (JsonObject)company["FinancialDescriptors"]!.DeepClone();
        Months = months ?? (company["PredictionHorizon"] is JsonValue h && h.TryGetValue<double>(out var m) && m > 0 ? m : Months);
        given.Writer.TryWrite(("number", Months.ToString("R", CultureInfo.InvariantCulture)));
        foreach (var (type, part) in Parts)
            if (Company[part] is JsonObject datum) given.Writer.TryWrite((type, datum.ToJsonString()));
        return null;
    }

    public void GiveHorizon(double months)
    {
        Months = months;
        if (Company is not null) given.Writer.TryWrite(("number", months.ToString("R", CultureInfo.InvariantCulture)));
    }

    // WHAT IF: each figure moved by its fraction (-0.5 is half, +1 is double) from the company as it was chosen; the Financial Descriptors given again.
    public void WhatIf(IReadOnlyDictionary<string, double> changes)
    {
        if (Company?["FinancialDescriptors"] is not JsonObject original) return;
        financial = Changed(original, changes);
        given.Writer.TryWrite(("CUI-FND-V2.1", financial.ToJsonString()));
    }

    // What is wrong with a company file for the Module, or null.
    public static string? Problem(JsonObject company)
    {
        if (company["FinancialDescriptors"] is not JsonObject f || (string?)f["Header"] != "CUI-FND-V2.1") return "The company file gives no FinancialDescriptors (CUI-FND-V2.1).";
        if (company["GovernanceDescriptors"] is not JsonObject g || (string?)g["Header"] != "CUI-GVS-V2.1") return "The company file gives no GovernanceDescriptors (CUI-GVS-V2.1).";
        return null;
    }

    // The value of a figure in the Financial Descriptors (the first period that gives it), or null.
    public static double? Figure(JsonObject financialDescriptors, string code) =>
        Inputs(financialDescriptors).Where(i => (string?)i["code"] == code).Select(i => Number(i["value"])).FirstOrDefault(v => v is not null);

    // The Financial Descriptors with each figure moved by its fraction, in every period that gives it; nothing else changes.
    public static JsonObject Changed(JsonObject financialDescriptors, IReadOnlyDictionary<string, double> changes)
    {
        var copy = (JsonObject)financialDescriptors.DeepClone();
        foreach (var input in Inputs(copy))
            if ((string?)input["code"] is { } code && changes.TryGetValue(code, out var fraction) && Number(input["value"]) is { } v)
                input["value"] = Math.Round(v * (1 + fraction), 2).ToString("R", CultureInfo.InvariantCulture);
        return copy;
    }

    private static IEnumerable<JsonObject> Inputs(JsonObject financialDescriptors) =>
        (financialDescriptors["FinancialDescriptors"]?["AQ"] as JsonArray ?? [])
            .OfType<JsonObject>().SelectMany(q => (q["input"] as JsonArray ?? []).OfType<JsonObject>());

    private static double? Number(JsonNode? value) => value switch
    {
        JsonValue v when v.TryGetValue<double>(out var d) => d,
        JsonValue v when v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null
    };

    public Task DeliverAsync(string dataType, string json, CancellationToken cancel) => Task.CompletedTask;

    // Called when a workflow binds the device (a client shows its panel then).
    public event Action? Opened;

    public async IAsyncEnumerable<(string DataType, string Json)> ReadAsync([EnumeratorCancellation] CancellationToken cancel)
    {
        Opened?.Invoke();
        while (await given.Reader.WaitToReadAsync(cancel))
            while (given.Reader.TryRead(out var datum)) yield return datum;
    }

    public Task SafeStopAsync() => Task.CompletedTask;
}

// WHAT COMPANY PERFORMANCE PREDICTION GAVE, as a person reads it: the probabilities and what weighs most in each (the explanation the descriptors carry).
public sealed record CompanyReport(double? Default, IReadOnlyList<(string What, double Weight)> DefaultWeights,
                                   double? Organisation, IReadOnlyList<(string What, double Weight)> OrganisationWeights,
                                   IReadOnlyList<(string Risk, double? Probability, IReadOnlyList<(string What, double Weight)> Weights)> Discontinuity,
                                   double? Secondary)
{
    public static CompanyReport Of(IReadOnlyDictionary<string, string> byDataType)
    {
        static double? Num(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;
        static JsonNode? Parse(IReadOnlyDictionary<string, string> d, string t) => d.TryGetValue(t, out var j) ? JsonNode.Parse(j) : null;
        var pdd = Parse(byDataType, "CUI-PDD-V2.1")?["PrimaryDefaultDescriptors"]?[0];
        var ord = Parse(byDataType, "CUI-ORD-V2.1")?["OrganisationDescriptors"]?[0];
        var psd = Parse(byDataType, "CUI-PSD-V2.1")?["PrimaryDiscontinuityDescriptors"] as JsonArray;
        return new CompanyReport(
            Num(pdd?["PrimaryDefaultProbability"]),
            ((pdd?["PrimaryDefaultDescriptorImpacts"] as JsonArray) ?? []).Select(i => ((string?)i?["PrimaryDefaultDescriptorID"] ?? "?", Num(i?["PrimaryDefaultDescriptorImpact"]) ?? 0)).OrderByDescending(x => x.Item2).ToList(),
            Num(ord?["ImpactOnDefault"]),
            ((ord?["GovernanceDescriptorsImpact"] as JsonArray) ?? []).Select(i => ((string?)i?["GovernaceDescriptorID"] ?? "?", Num(i?["GovernanceDescriptorImpact"]) ?? 0)).OrderByDescending(x => x.Item2).ToList(),
            (psd ?? []).Select(e => ((string?)e?["PrimaryRiskID"] ?? "?", Num(e?["PrimaryDiscontinuityProbability"]),
                (IReadOnlyList<(string, double)>)((e?["PrimaryDiscontinuityDescriptorImpacts"] as JsonArray) ?? []).Select(i => ((string?)i?["PrimaryRiskID"] ?? "?", Num(i?["PrimaryRiskDescriptorImpact"]) ?? 0)).OrderByDescending(x => x.Item2).ToList())).ToList(),
            byDataType.TryGetValue("number", out var s) ? Num(JsonNode.Parse(s)) : null);
    }
}

// THE REPORT OF COMPANY PERFORMANCE PREDICTION, a device of the User Agent: what the Module delivers, the latest of each Data Type, and a call when it changes.
public sealed class CompanyReportDevice(Action<IReadOnlyDictionary<string, string>> changed) : IDevice
{
    private readonly Dictionary<string, string> latest = new(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> Latest { get { lock (latest) return new Dictionary<string, string>(latest); } }
    public void Clear() { lock (latest) latest.Clear(); }

    public Task DeliverAsync(string dataType, string json, CancellationToken cancel)
    {
        lock (latest) latest[dataType] = json;
        changed(Latest);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<(string DataType, string Json)> ReadAsync([EnumeratorCancellation] CancellationToken cancel)
    {
        await Task.Delay(Timeout.Infinite, cancel).ContinueWith(_ => { });
        yield break;
    }

    public Task SafeStopAsync() => Task.CompletedTask;
}
