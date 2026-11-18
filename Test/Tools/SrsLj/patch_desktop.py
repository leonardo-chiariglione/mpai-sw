"""N-27: the desktop client of the MAS App for the SRS App (as patch_browser.py for the browser). Line endings kept."""
from pathlib import Path

P = Path(r"D:\AI\AIF\UserAgent\Clients\Desktop\src\MainWindow.xaml.cs")
raw = P.read_bytes().decode("utf-8")
crlf = "\r\n" in raw
s = raw.replace("\r\n", "\n")


def rep(old, new):
    global s
    assert s.count(old) == 1, (old[:80], s.count(old))
    s = s.replace(old, new)


rep("""                try
                {
                    using var web = new System.Net.Http.HttpClient();
                    if (SpeechWav.Pcm16kMono(await web.GetByteArrayAsync(example.Uri)) is not { Length: > 0 } examplePcm) { Instruct($"I could not read the example {example.Label}."); return null; }
                    return MpaiJson.ToJson(BasicSpeechObject.FromData(examplePcm, new SpeechQualifier
                    {
                        SpeechQualifierID = Guid.NewGuid().ToString(),
                        Format = new SpeechFormat { ContentFormats = new SpeechContentFormats { RawData = new Pcm { SamplingFrequency = 16000, Precision = 16 } } },
                        Attributes = new SpeechAttributes { Source = SpeechSource.Real }
                    }));
                }""",
"""                try
                {
                    // An address relative to the Service (Apps/<id>/Files/...) is a file of the App, fetched from the Service that offers it.
                    var relative = !Uri.TryCreate(example.Uri, UriKind.Absolute, out _);
                    using var files = new AppDirectory(ServiceUrl, ServiceToken, MasClientIdentity.Id, Collection);
                    using var web = new System.Net.Http.HttpClient();
                    var fetched = relative ? await files.FileAsync(example.Uri) : await web.GetByteArrayAsync(example.Uri);
                    if (SpeechWav.Pcm16kMono(fetched) is not { Length: > 0 } examplePcm) { Instruct($"I could not read the example {example.Label}."); return null; }
                    // THE SECTIONS IT COMES WITH, where the App gives them beside it: they fill the table of damaged sections, should the workflow ask for one.
                    _readySections = null;
                    if (relative)
                        try { _readySections = DamagedSections.FromSidecar(await files.FileTextAsync(DamagedSections.SidecarOf(example.Uri))); }
                        catch (System.Net.Http.HttpRequestException) { }
                    // ITS NAME IS KEPT: speech named by the workflow (the Speech Segments for Modelling of a network, a ready-made recording) is that speech.
                    var named = System.Text.Json.Nodes.JsonNode.Parse(MpaiJson.ToJson(BasicSpeechObject.FromData(examplePcm, new SpeechQualifier
                    {
                        SpeechQualifierID = Guid.NewGuid().ToString(),
                        Format = new SpeechFormat { ContentFormats = new SpeechContentFormats { RawData = new Pcm { SamplingFrequency = 16000, Precision = 16 } } },
                        Attributes = new SpeechAttributes { Source = SpeechSource.Real }
                    })))!.AsObject();
                    named["BasicSpeechObjectID"] = example.Label;
                    return named.ToJsonString();
                }""")

rep("""        devices.RegisterAcquire("OSD-BTO-V1.5", async (_, wanted, abandon) =>
        {
""",
"""        devices.RegisterAcquire("OSD-BTO-V1.5", async (_, wanted, abandon) =>
        {
            // THE WORDS OF THE TABLE OF DAMAGED SECTIONS, kept when the table gave the Damaged List.
            if ((wanted ?? "").Contains(DamagedSections.WordsMarker, StringComparison.Ordinal) && _sectionWords is { Count: > 0 } sectionWords)
                return DamagedSections.TextList(sectionWords);

""")

rep("""    // READY-MADE SPEECH TO PICK: one small window holds a button for each example the workflow waits for; it closes when none is waited for.""",
"""    // THE TABLE OF DAMAGED SECTIONS (Speech Restoration System): a small window with a row for each section - from, to, the words said there - filled in from
    // the sections a ready-made recording comes with. OK gives the Damaged List; the words are kept for when the workflow asks for them.
    private IReadOnlyList<DamagedSections.Row>? _readySections, _sectionWords;

    private async Task<string?> AcquireSectionsAsync(CancellationToken abandon)
    {
        var done = new TaskCompletionSource<IReadOnlyList<DamagedSections.Row>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Window? window = null;
        await Dispatcher.InvokeAsync(() =>
        {
            var rows = new System.Windows.Controls.StackPanel();
            var problem = new System.Windows.Controls.TextBlock { Foreground = System.Windows.Media.Brushes.DarkRed, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
            void AddRow(double from, double to, string words)
            {
                var row = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                row.Children.Add(new System.Windows.Controls.TextBox { Text = from.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), Width = 60, Margin = new Thickness(0, 0, 6, 0), ToolTip = "From (s)" });
                row.Children.Add(new System.Windows.Controls.TextBox { Text = to.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), Width = 60, Margin = new Thickness(0, 0, 6, 0), ToolTip = "To (s)" });
                row.Children.Add(new System.Windows.Controls.TextBox { Text = words, Width = 240, ToolTip = "Words said there" });
                rows.Children.Add(row);
            }
            foreach (var r in _readySections ?? []) AddRow(r.From, r.To, r.Words);
            if (rows.Children.Count == 0) AddRow(0, 0.5, "");
            IReadOnlyList<DamagedSections.Row>? Read()
            {
                var list = new List<DamagedSections.Row>();
                foreach (var row in rows.Children.OfType<System.Windows.Controls.StackPanel>())
                {
                    var boxes = row.Children.OfType<System.Windows.Controls.TextBox>().ToList();
                    if (!double.TryParse(boxes[0].Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f) ||
                        !double.TryParse(boxes[1].Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var t))
                    { problem.Text = "Write the times in seconds, for instance 1.25."; return null; }
                    list.Add(new DamagedSections.Row(f, t, boxes[2].Text));
                }
                if (DamagedSections.Problem(list) is { } why) { problem.Text = why; return null; }
                return list;
            }
            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Damaged sections: from (s), to (s), the words said there", Margin = new Thickness(0, 0, 0, 8) });
            panel.Children.Add(rows);
            var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var add = new System.Windows.Controls.Button { Content = "Add a section", Width = 110, Height = 26, Margin = new Thickness(0, 0, 8, 0) };
            add.Click += (_, _) => AddRow(0, 0.5, "");
            var ok = new System.Windows.Controls.Button { Content = "OK", Width = 80, Height = 26, IsDefault = true };
            ok.Click += (_, _) => { if (Read() is { } list) { done.TrySetResult(list); window?.Close(); } };
            buttons.Children.Add(add); buttons.Children.Add(ok);
            panel.Children.Add(buttons);
            panel.Children.Add(problem);
            window = new Window
            {
                Title = "Damaged sections", Owner = this, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel
            };
            window.Closed += (_, _) => done.TrySetResult(null);
            window.Show();
        });
        using var r = abandon.Register(() => { done.TrySetResult(null); Dispatcher.InvokeAsync(() => { try { window?.Close(); } catch { } }); });
        var chosen = await done.Task;
        if (chosen is null) return null;
        _sectionWords = chosen;
        return DamagedSections.DamagedList(chosen);
    }

    // READY-MADE SPEECH TO PICK: one small window holds a button for each example the workflow waits for; it closes when none is waited for.""")

# the acquisition of the table, registered beside the Text
rep("""        // THE NAME OF AN APPLICATION, CHOSEN BY THE PERSON. A Text Object whose""",
"""        // THE TABLE OF DAMAGED SECTIONS (Speech Restoration System): the Damaged List, and the words for later.
        devices.RegisterAcquire("CAE-DLS-V2.5", async (_, _, abandon) =>
            await AcquireSectionsAsync(CancellationTokenSource.CreateLinkedTokenSource((_appStopping ?? _stopping)?.Token ?? CancellationToken.None, abandon).Token));

        // THE NAME OF AN APPLICATION, CHOSEN BY THE PERSON. A Text Object whose""")

P.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))
print("ok")
