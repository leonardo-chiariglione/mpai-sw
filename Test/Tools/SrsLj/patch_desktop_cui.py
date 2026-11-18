"""N-36 (night 2026-11-18): the desktop client of the MAS App for the CUI-CPP App - a window "Company Performance Prediction": the companies of the App, a file of
one's own, the horizon, what-if sliders, and the report (three gauges with what weighs most) beside the company chosen before. Line endings kept."""
from pathlib import Path

P = Path(r"D:\AI\AIF\UserAgent\Clients\Desktop\src\MainWindow.xaml.cs")
raw = P.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")


def rep(old, new):
    global s
    assert s.count(old) == 1, (old[:80], s.count(old))
    s = s.replace(old, new)


rep("""            var text = appId == "MAS"
                ? await File.ReadAllTextAsync(MasWorkflowPath)
                : await directory.WorkflowAsync(appId);""",
"""            var text = appId == "MAS"
                ? await File.ReadAllTextAsync(MasWorkflowPath)
                : await directory.WorkflowAsync(appId);
            if (appId != "MAS") _runningApp = appId;""")

rep("""        // THE TABLE OF DAMAGED SECTIONS (Speech Restoration System): the Damaged List, and the words for later.""",
"""        // COMPANY PERFORMANCE PREDICTION (MPAI-CUI CPP): the company the person chooses, the horizon and what-if, given as they are chosen; the report beside the one before.
        _company = new CompanyDevice();
        _company.Opened += () => Dispatcher.InvokeAsync(ShowCompanyWindowAsync);
        devices.RegisterDevice("Company", _company);
        devices.RegisterDevice("Report", new CompanyReportDevice(latest => Dispatcher.InvokeAsync(() => ShowReport(CompanyReport.Of(latest)))));

        // THE TABLE OF DAMAGED SECTIONS (Speech Restoration System): the Damaged List, and the words for later.""")

rep("""    // THE TABLE OF DAMAGED SECTIONS (Speech Restoration System): a small window with a row for each section""",
"""    // ---- Company Performance Prediction ---------------------------------------------------------------------------------------------------------------------------
    private string? _runningApp;
    private CompanyDevice? _company;
    private Window? _companyWindow;
    private System.Windows.Controls.StackPanel? _reportNow, _reportBefore;
    private CompanyReport? _lastReport;
    private string? _lastName;

    private async Task ShowCompanyWindowAsync()
    {
        if (_companyWindow is not null || _company is null) return;
        var companies = new List<(string Name, string File)>();
        try
        {
            using var files = new AppDirectory(ServiceUrl, ServiceToken, MasClientIdentity.Id, Collection);
            using var doc = System.Text.Json.JsonDocument.Parse(await files.FileTextAsync($"Apps/{_runningApp}/Files/companies/index.json"));
            companies = doc.RootElement.GetProperty("companies").EnumerateArray().Select(c => (c.GetProperty("name").GetString() ?? "", c.GetProperty("file").GetString() ?? "")).ToList();
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Text.Json.JsonException or KeyNotFoundException) { }

        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(14) };
        var problem = new System.Windows.Controls.TextBlock { Foreground = System.Windows.Media.Brushes.DarkRed, TextWrapping = TextWrapping.Wrap };
        var list = new System.Windows.Controls.ComboBox { Width = 300, ItemsSource = companies.Select(c => c.Name).ToList(), Margin = new Thickness(0, 0, 8, 0) };
        var horizon = new System.Windows.Controls.ComboBox { Width = 70, ItemsSource = new[] { "12", "24", "36" }, SelectedIndex = 0 };
        double Months() => double.Parse((string)horizon.SelectedItem, System.Globalization.CultureInfo.InvariantCulture);
        void Give(System.Text.Json.Nodes.JsonObject? json, string name)
        {
            problem.Text = json is null ? "That is not a company file." : _company!.Give(json, name, Months()) ?? "";
            if (problem.Text.Length > 0) return;
            if (_lastReport is not null) { ShowReportIn(_reportBefore!, _lastReport, _lastName); }
            _lastReport = null; _lastName = name; _reportNow!.Children.Clear();
        }
        var choose = new System.Windows.Controls.Button { Content = "Choose", Width = 80 };
        choose.Click += async (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            var (name, file) = companies[list.SelectedIndex];
            try { using var files = new AppDirectory(ServiceUrl, ServiceToken, MasClientIdentity.Id, Collection); Give(System.Text.Json.Nodes.JsonNode.Parse(await files.FileTextAsync(file)) as System.Text.Json.Nodes.JsonObject, name); }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Text.Json.JsonException) { problem.Text = $"I could not read {name}."; }
        };
        var own = new System.Windows.Controls.Button { Content = "A file of your own...", Width = 150, Margin = new Thickness(8, 0, 0, 0) };
        own.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "Company file (*.json)|*.json" };
            if (dialog.ShowDialog(this) != true) return;
            try { Give(System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(dialog.FileName)) as System.Text.Json.Nodes.JsonObject, Path.GetFileNameWithoutExtension(dialog.FileName)); }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException) { problem.Text = "That is not a company file (see the README of the App)."; }
        };
        horizon.SelectionChanged += (_, _) => _company?.GiveHorizon(Months());
        var row1 = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        row1.Children.Add(list); row1.Children.Add(choose); row1.Children.Add(own);
        row1.Children.Add(new System.Windows.Controls.TextBlock { Text = "  within ", VerticalAlignment = VerticalAlignment.Center }); row1.Children.Add(horizon);
        row1.Children.Add(new System.Windows.Controls.TextBlock { Text = " months", VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "A company", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        panel.Children.Add(row1);
        panel.Children.Add(problem);

        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "What if (% change of the company as chosen)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) });
        var sliders = new Dictionary<string, System.Windows.Controls.Slider>();
        foreach (var (code, name) in CompanyDevice.KeyFigures)
        {
            var row = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            var value = new System.Windows.Controls.TextBlock { Width = 50, Text = "0 %" };
            var slider = new System.Windows.Controls.Slider { Minimum = -50, Maximum = 100, Width = 240, TickFrequency = 5, IsSnapToTickEnabled = true, ToolTip = name };
            slider.ValueChanged += (_, e) => value.Text = $"{e.NewValue:+0;-0;0} %";
            row.Children.Add(new System.Windows.Controls.TextBlock { Text = name, Width = 250 }); row.Children.Add(slider); row.Children.Add(value);
            panel.Children.Add(row); sliders[code] = slider;
        }
        var apply = new System.Windows.Controls.Button { Content = "Apply", Width = 80, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        apply.Click += (_, _) => _company?.WhatIf(sliders.ToDictionary(k => k.Key, k => k.Value.Value / 100.0));
        panel.Children.Add(apply);

        var reports = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        _reportBefore = new System.Windows.Controls.StackPanel { Width = 330, Margin = new Thickness(0, 0, 12, 0) };
        _reportNow = new System.Windows.Controls.StackPanel { Width = 330 };
        reports.Children.Add(_reportBefore); reports.Children.Add(_reportNow);
        panel.Children.Add(reports);
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Demonstration model trained on Polish companies (CC BY 4.0), not for real companies.", Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 10, 0, 0) });

        _companyWindow = new Window
        {
            Title = "Company Performance Prediction", Owner = this, Width = 760, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new System.Windows.Controls.ScrollViewer { Content = panel, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, MaxHeight = 900 }
        };
        _companyWindow.Closed += (_, _) => _companyWindow = null;
        _companyWindow.Show();
    }

    private void ShowReport(CompanyReport r)
    {
        _lastReport = r;
        if (_reportNow is not null) ShowReportIn(_reportNow, r, _lastName);
    }

    // A report: the company, three gauges and what weighs most in each.
    private static void ShowReportIn(System.Windows.Controls.StackPanel into, CompanyReport r, string? name)
    {
        into.Children.Clear();
        string Pct(double? p) => p is { } x ? (x * 100).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " %" : "-";
        void Gauge(string what, double? p)
        {
            var row = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(new System.Windows.Controls.TextBlock { Text = what, Width = 150 });
            row.Children.Add(new System.Windows.Controls.ProgressBar { Minimum = 0, Maximum = 1, Value = p ?? 0, Width = 110, Height = 12 });
            row.Children.Add(new System.Windows.Controls.TextBlock { Text = "  " + Pct(p), FontWeight = FontWeights.SemiBold });
            into.Children.Add(row);
        }
        void Weights(IEnumerable<(string What, double Weight)> weights, string unit) =>
            into.Children.Add(new System.Windows.Controls.TextBlock { Text = string.Join("\\n", weights.Select(w => $"   {w.What} {w.Weight:0.##}{unit}")), Foreground = System.Windows.Media.Brushes.DimGray, FontSize = 11 });
        into.Children.Add(new System.Windows.Controls.TextBlock { Text = name ?? "", FontWeight = FontWeights.Bold, FontSize = 14 });
        Gauge("Default within the horizon", r.Default); Weights(r.DefaultWeights.Take(5), " %");
        into.Children.Add(new System.Windows.Controls.TextBlock { Text = $"Organisation adds {Pct(r.Organisation)}", Margin = new Thickness(0, 6, 0, 0) }); Weights(r.OrganisationWeights.Take(3), " %");
        foreach (var (risk, p, weights) in r.Discontinuity.Take(1)) { Gauge("Discontinuity", p); Weights(weights.Take(4), ""); }
        if (r.Secondary is not null) Gauge("With secondary risks", r.Secondary);
    }

    // THE TABLE OF DAMAGED SECTIONS (Speech Restoration System): a small window with a row for each section""")
P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
