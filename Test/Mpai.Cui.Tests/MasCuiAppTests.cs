using System.Text.Json.Nodes;

using AIF.Controller;
using Mpai.Aif.Api;
using Mpai.Aif.PortData;
using Mpai.Cui;
using Mpai.Ua;

namespace Mpai.Cui.Tests;

// COMPANY PERFORMANCE PREDICTION AS AN APP OF THE MAS APP (N-36, night 2026-11-18): one workflow for the console and the MAS App; the company the person chooses given by
// the device "Company" (ready-made companies of the App, or a file of one's own; the horizon; what-if), what the Module gives delivered to the device "Report".
[Collection("cui")]
public class MasCuiAppTests
{
    private static readonly string App = Path.Combine(Repo.Root, "Apps", "CUI", "CPP");

    // The workflow reads; it binds every input of the Module to the device "Company" and every output to the device "Report".
    [Fact]
    public void TheWorkflowBindsTheCompanyAndTheReport()
    {
        var workflow = new Mpai.Wdl.WorkflowReader().Read(File.ReadAllText(Path.Combine(App, "CUI-CPP.orch")));
        var steps = workflow.OnStart.ToList();
        Assert.Equal(["number", "CUI-FND-V2.1", "CUI-GVS-V2.1", "CUI-LRD-V2.1", "CUI-CRD-V2.1", "CUI-SRS-V2.1"],
                     steps.Where(s => s.Kind == Mpai.Wdl.StepKind.Stream && s.Text == "Company").Select(s => s.Port!.DataType));
        Assert.Equal(["CUI-ORD-V2.1", "CUI-PDD-V2.1", "CUI-PSD-V2.1", "number"],
                     steps.Where(s => s.Kind == Mpai.Wdl.StepKind.Deliver && s.Text == "Report").Select(s => s.Port!.DataType));
    }

    // The ready-made companies are files of the App, named in plain words, each a company the Module can take, each part valid against its schema.
    [Fact]
    public void TheReadyMadeCompaniesAreValidCompaniesOfTheApp()
    {
        var index = JsonNode.Parse(File.ReadAllText(Path.Combine(App, "Files", "companies", "index.json")))!["companies"]!.AsArray();
        Assert.Equal(11, index.Count);
        foreach (var c in index)
        {
            var file = (string)c!["file"]!;
            Assert.StartsWith("Apps/CPP/Files/companies/", file);
            var company = JsonNode.Parse(File.ReadAllText(Path.Combine(App, "Files", "companies", Path.GetFileName(file))))!.AsObject();
            Assert.Null(CompanyDevice.Problem(company));
            foreach (var (type, part) in CompanyDevice.Parts)
                if (company[part] is JsonObject datum) Assert.Empty(PortDataSchema.Violations(type, datum.ToJsonString()) ?? []);
        }
    }

    // A what-if moves the figures asked, in every period that gives them, and nothing else; the device gives the Financial Descriptors again.
    [Fact]
    public async Task AWhatIfMovesOnlyTheFiguresAsked()
    {
        var company = JsonNode.Parse(File.ReadAllText(Path.Combine(App, "Files", "companies", "average.json")))!.AsObject();
        var fnd = company["FinancialDescriptors"]!.AsObject();
        var debts = CompanyDevice.Figure(fnd, "SPPDTOT")!.Value; var revenues = CompanyDevice.Figure(fnd, "CEA1")!.Value;
        var changed = CompanyDevice.Changed(fnd, new Dictionary<string, double> { ["SPPDTOT"] = 0.5 });
        Assert.Equal(debts * 1.5, CompanyDevice.Figure(changed, "SPPDTOT")!.Value, 2);
        Assert.Equal(revenues, CompanyDevice.Figure(changed, "CEA1")!.Value, 2);
        Assert.Empty(PortDataSchema.Violations("CUI-FND-V2.1", changed.ToJsonString()) ?? []);

        var device = new CompanyDevice();
        Assert.Null(device.Give(company, "average", 24));
        device.WhatIf(new Dictionary<string, double> { ["SPPDTOT"] = 1.0 });
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var given = new List<(string Type, string Json)>();
        try { await foreach (var d in device.ReadAsync(stop.Token)) given.Add(d); } catch (OperationCanceledException) { }
        Assert.Equal("24", given[0].Json);                                                          // the horizon chosen
        Assert.Equal("CUI-FND-V2.1", given[^1].Type);                                               // the what-if, last
        Assert.Equal(debts * 2, CompanyDevice.Figure(JsonNode.Parse(given[^1].Json)!.AsObject(), "SPPDTOT")!.Value, 2);
    }

    // A ready-made company crosses the Service (the codecs) as the device gives it; the Module gives the three results, which the Report reads as the descriptors say; more
    // debts (what-if) raise the probability of default.
    [Fact]
    public async Task TheModuleGivesTheThreeResultsAndMoreDebtsRaiseTheDefault()
    {
        ReferenceModels.AsOf = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var work = Path.Combine(Path.GetTempPath(), "cui-mas-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("MPAI_ACCESS", Path.Combine(work, "access"));
        using var api = new ControllerApi(Repo.AmdsWithModel(work, null), Path.Combine(Repo.Root, "AIMs", "aim-settings.json"), new CuiProvider());
        Assert.Equal(AifError.OK, api.StartFlow("1CUI-CPP-V2.1-I01", out var id));
        var codecs = PortDataCodecs.Default();
        try
        {
            var device = new CompanyDevice();
            async Task Pass()
            {
                using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try
                {
                    await foreach (var (type, json) in device.ReadAsync(stop.Token))
                    {
                        Assert.True(codecs.Knows(type), type);
                        Assert.Equal(AifError.OK, api.InputWrite(id, type, 1, json, 5000));
                    }
                }
                catch (OperationCanceledException) { }
            }
            Dictionary<string, string> Latest()
            {
                var latest = new Dictionary<string, string>();
                foreach (var type in new[] { "CUI-ORD-V2.1", "CUI-PDD-V2.1", "CUI-PSD-V2.1" })
                    for (var r = api.OutputRead(id, type, 1, 4000); r is { Ok: true, Json: { } j }; r = api.OutputRead(id, type, 1, 800)) latest[type] = j;
                return latest;
            }
            var company = JsonNode.Parse(File.ReadAllText(Path.Combine(App, "Files", "companies", "leveraged.json")))!.AsObject();
            Assert.Null(device.Give(company, "leveraged", 12));
            await Pass();
            var first = Latest();
            Assert.Equal(3, first.Count);
            var report = CompanyReport.Of(first);
            var pdd = (double)JsonNode.Parse(first["CUI-PDD-V2.1"])!["PrimaryDefaultDescriptors"]![0]!["PrimaryDefaultProbability"]!;
            Assert.Equal(pdd, report.Default!.Value, 9);                                                 // the Report says what the descriptors say
            Assert.NotEmpty(report.DefaultWeights);
            Assert.NotEmpty(report.Discontinuity);

            device.WhatIf(new Dictionary<string, double> { ["SPPDTOT"] = 1.0 });                      // twice the debts
            await Pass();
            var after = CompanyReport.Of(Latest());
            Assert.True(after.Default > report.Default, $"twice the debts: default {report.Default:F4} -> {after.Default:F4}");
        }
        finally { api.StopFlow(id); Environment.SetEnvironmentVariable("MPAI_ACCESS", null); try { Directory.Delete(work, true); } catch { } }
    }
}
