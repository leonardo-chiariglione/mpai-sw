"""N-36 (night 2026-11-18): the browser client of the MAS App for the CUI-CPP App - the Company device (ready-made companies of the App, a file of one's own, the horizon,
what-if) and the Report device (three gauges with what weighs most, the company before it side by side). Line endings kept."""
from pathlib import Path

D = Path(r"D:\AI\AIF\UserAgent\Clients\Browser\Client")
U = Path(r"D:\AI\AIF\UserAgent\Ua\CompanyDevice.cs")


def edit(path, pairs):
    raw = path.read_bytes().decode("utf-8")
    crlf = "\r\n" in raw
    s = raw.replace("\r\n", "\n")
    for old, new in pairs:
        assert s.count(old) == 1, (path.name, old[:70], s.count(old))
        s = s.replace(old, new)
    path.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))


# the device says when the workflow binds it
edit(U, [("""    public async IAsyncEnumerable<(string DataType, string Json)> ReadAsync([EnumeratorCancellation] CancellationToken cancel)
    {
        while (await given.Reader.WaitToReadAsync(cancel))""",
"""    // Called when a workflow binds the device (a client shows its panel then).
    public event Action? Opened;

    public async IAsyncEnumerable<(string DataType, string Json)> ReadAsync([EnumeratorCancellation] CancellationToken cancel)
    {
        Opened?.Invoke();
        while (await given.Reader.WaitToReadAsync(cancel))""")])

edit(D / "RcaShell.razor.cs", [
("""            AppTitle = app?.Name ?? (appId == "MAS" ? "" : appId);""",
 """            AppTitle = app?.Name ?? (appId == "MAS" ? "" : appId);
            if (appId != "MAS") runningApp = appId;"""),
("""        // TEXT: typed by the person, or the name of an App chosen from the list.""",
"""        // COMPANY PERFORMANCE PREDICTION (MPAI-CUI CPP): the company the person chooses - ready-made, of the App's files, or their own file - the horizon and what-if,
        // given to the Module as they are chosen; the Report shows what the Module gives, as it gives it, beside the company chosen before.
        company = new CompanyDevice();
        company.Opened += () => { choosingCompany = true; _ = LoadCompaniesAsync(); Refresh(); };
        devices.RegisterDevice("Company", company);
        devices.RegisterDevice("Report", new CompanyReportDevice(latest => { report = CompanyReport.Of(latest); Refresh(); }));

        // TEXT: typed by the person, or the name of an App chosen from the list."""),
("""    // The table of damaged sections, while the workflow waits for it.""",
"""    // ---- Company Performance Prediction ------------------------------------------------------------------------------------------------------------------------
    private string? runningApp;
    private CompanyDevice? company;
    private bool choosingCompany;
    private List<(string Name, string File)> companies = new();
    private string? companyProblem;
    private double horizon = 12;
    private CompanyReport? report, previousReport;
    private string? reportName, previousName;
    private readonly Dictionary<string, double> whatIf = CompanyDevice.KeyFigures.ToDictionary(f => f.Code, _ => 0.0);

    private async Task LoadCompaniesAsync()
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(await Http.GetStringAsync(Directory().FileUrl($"Apps/{runningApp}/Files/companies/index.json")));
            companies = doc.RootElement.GetProperty("companies").EnumerateArray().Select(c => (c.GetProperty("name").GetString() ?? "", c.GetProperty("file").GetString() ?? "")).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or KeyNotFoundException) { companies = new(); }
        Refresh();
    }

    private async Task PickCompany(string name, string file)
    {
        try { GiveCompany(System.Text.Json.Nodes.JsonNode.Parse(await Http.GetStringAsync(Directory().FileUrl(file))) as System.Text.Json.Nodes.JsonObject, name); }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException) { companyProblem = $"I could not read {name}."; }
    }

    private async Task CompanyFileChosen(InputFileChangeEventArgs e)
    {
        try
        {
            using var stream = e.File.OpenReadStream(5_000_000);
            using var reader = new System.IO.StreamReader(stream);
            GiveCompany(System.Text.Json.Nodes.JsonNode.Parse(await reader.ReadToEndAsync()) as System.Text.Json.Nodes.JsonObject, System.IO.Path.GetFileNameWithoutExtension(e.File.Name));
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.Text.Json.JsonException) { companyProblem = "That is not a company file (see the README of the App)."; }
    }

    private void GiveCompany(System.Text.Json.Nodes.JsonObject? json, string name)
    {
        if (json is null || company is null) { companyProblem = "That is not a company file."; return; }
        companyProblem = company.Give(json, name, horizon);
        if (companyProblem is not null) return;
        if (report is not null) { previousReport = report; previousName = reportName; }        // the company before, beside the new one
        report = null; reportName = name;
        foreach (var k in whatIf.Keys.ToList()) whatIf[k] = 0;
        Refresh();
    }

    private void HorizonChosen(ChangeEventArgs e)
    {
        if (double.TryParse(e.Value?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var m)) { horizon = m; company?.GiveHorizon(m); }
    }

    private void ApplyWhatIf() => company?.WhatIf(whatIf.ToDictionary(k => k.Key, k => k.Value / 100.0));

    private static string Pct(double? p) => p is { } x ? (x * 100).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " %" : "-";

    // The table of damaged sections, while the workflow waits for it."""),
])

edit(D / "RcaShell.razor", [
("""  @if (choosingSections)""",
"""  @if (choosingCompany)
  {
    <div class="company">
      <div class="languages">
        <span>A company:</span>
        @foreach (var c in companies) { <button @onclick="() => PickCompany(c.Name, c.File)">@c.Name</button> }
      </div>
      <div class="languages">
        <label>Or a file of your own: <InputFile OnChange="CompanyFileChosen" accept=".json,application/json" /></label>
        <label>Within <select @onchange="HorizonChosen"><option value="12" selected>12</option><option value="24">24</option><option value="36">36</option></select> months</label>
      </div>
      @if (companyProblem is not null) { <span class="problem">@companyProblem</span> }
      @if (company?.Company is not null)
      {
        <div class="whatif">
          <span>What if, for @company.Name:</span>
          @foreach (var (code, name) in CompanyDevice.KeyFigures)
          {
            <label>@name <input type="range" min="-50" max="100" step="5" @bind="whatIf[code]" @bind:event="oninput" /> @(whatIf[code] > 0 ? "+" : "")@whatIf[code] %</label>
          }
          <button @onclick="ApplyWhatIf">Apply</button>
        </div>
      }
    </div>
  }

  @if (report is not null || previousReport is not null)
  {
    <div class="reports">
      @foreach (var (r, name) in new[] { (previousReport, previousName), (report, reportName) })
      {
        @if (r is not null)
        {
          <div class="report">
            <h3>@name</h3>
            <div class="gauge"><span>Default within the horizon</span><meter min="0" max="1" low="0.1" high="0.3" optimum="0" value="@(r.Default ?? 0)"></meter><b>@Pct(r.Default)</b></div>
            <ul>@foreach (var (what, weight) in r.DefaultWeights.Take(5)) { <li><span class="bar" style="width:@(Math.Max(2, weight * 2))px"></span>@what @weight.ToString("0") %</li> }</ul>
            <div class="gauge"><span>Organisation adds</span><b>@Pct(r.Organisation)</b></div>
            <ul>@foreach (var (what, weight) in r.OrganisationWeights.Take(3)) { <li><span class="bar" style="width:@(Math.Max(2, weight * 2))px"></span>@what @weight.ToString("0") %</li> }</ul>
            @foreach (var (risk, p, weights) in r.Discontinuity.Take(1))
            {
              <div class="gauge"><span>Discontinuity</span><meter min="0" max="1" low="0.1" high="0.3" optimum="0" value="@(p ?? 0)"></meter><b>@Pct(p)</b></div>
              <ul>@foreach (var (what, weight) in weights.Take(4)) { <li><span class="bar" style="width:@(Math.Max(2, weight * 120))px"></span>@what @weight.ToString("0.##")</li> }</ul>
            }
            @if (r.Secondary is not null)
            {
              <div class="gauge"><span>With secondary risks</span><meter min="0" max="1" low="0.1" high="0.3" optimum="0" value="@r.Secondary"></meter><b>@Pct(r.Secondary)</b></div>
            }
          </div>
        }
      }
    </div>
    <p class="note">Demonstration model trained on Polish companies (CC BY 4.0), not for real companies.</p>
  }

  @if (choosingSections)"""),
])

css = D / "wwwroot" / "css" / "rca.css"
raw = css.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n").rstrip("\n") + """
/* Company Performance Prediction (2026-11-18): the company chosen, what-if, and the report beside the one before */
.company { margin: 8px 0; display: flex; flex-direction: column; gap: 8px; }
.whatif { display: flex; flex-wrap: wrap; gap: 10px 18px; align-items: center; }
.whatif label { display: flex; gap: 6px; align-items: center; }
.reports { display: flex; gap: 16px; flex-wrap: wrap; margin: 8px 0; }
.report { background: #151A21; border: 1px solid #2A323D; border-radius: 6px; padding: 8px 12px; min-width: 300px; }
.report h3 { margin: 0 0 6px 0; font-size: 16px; }
.report .gauge { display: flex; gap: 8px; align-items: center; margin-top: 6px; }
.report .gauge span { width: 170px; }
.report meter { width: 140px; height: 14px; }
.report ul { margin: 2px 0 2px 0; padding-left: 16px; font-size: 13px; color: #9FB0C6; }
.report .bar { display: inline-block; height: 8px; background: #5B8DEF; margin-right: 6px; vertical-align: middle; }
.note { font-size: 12px; color: #8899AA; }
"""
css.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
