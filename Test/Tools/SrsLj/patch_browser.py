"""N-27: the browser client of the MAS App for the SRS App - ready-made speech at an address relative to the Service, its name kept (the Speech Segments for
Modelling of a network are named by it), the table of damaged sections (CAE-DLS-V2.5) filled in from the file beside a ready-made recording, the words of the
table given when the workflow asks for them. Line endings kept."""
from pathlib import Path

D = Path(r"D:\AI\AIF\UserAgent\Clients\Browser\Client")


def edit(name, pairs):
    p = D / name
    raw = p.read_bytes().decode("utf-8")
    crlf = "\r\n" in raw
    s = raw.replace("\r\n", "\n")
    for old, new in pairs:
        assert s.count(old) == 1, (name, old[:70], s.count(old))
        s = s.replace(old, new)
    p.write_bytes((s.replace("\n", "\r\n") if crlf else s).encode("utf-8"))


edit("RcaShell.razor.cs", [
("""                try
                {
                    using var web = new HttpClient();
                    var fetched = await web.GetByteArrayAsync(example.Uri);
                    if (SpeechWav.Pcm16kMono(fetched) is not { Length: > 0 } examplePcm) { Instruct($"I could not read the example {example.Label}."); return null; }
                    return MpaiJson.ToJson(SpeechPackaging.FromPcm16k(examplePcm, _sourceLanguage));
                }
                catch (HttpRequestException) { Instruct($"I could not fetch the example {example.Label}."); return null; }""",
"""                try
                {
                    // An address relative to the Service (Apps/<id>/Files/...) is a file of the App: fetched through this origin, as the Service is.
                    var relative = !Uri.TryCreate(example.Uri, UriKind.Absolute, out _);
                    using var web = new HttpClient();
                    var fetched = relative ? await Http.GetByteArrayAsync("MPAI/AIFU/" + example.Uri) : await web.GetByteArrayAsync(example.Uri);
                    if (SpeechWav.Pcm16kMono(fetched) is not { Length: > 0 } examplePcm) { Instruct($"I could not read the example {example.Label}."); return null; }
                    // THE SECTIONS IT COMES WITH, where the App gives them beside it: they fill the table of damaged sections, should the workflow ask for one.
                    readySections = null;
                    if (relative)
                        try { readySections = DamagedSections.FromSidecar(await Http.GetStringAsync("MPAI/AIFU/" + DamagedSections.SidecarOf(example.Uri))); }
                        catch (HttpRequestException) { }
                    // ITS NAME IS KEPT: speech named by the workflow (the Speech Segments for Modelling of a network, a ready-made recording) is that speech.
                    var packaged = SpeechPackaging.FromPcm16k(examplePcm, _sourceLanguage);
                    var named = System.Text.Json.Nodes.JsonNode.Parse(MpaiJson.ToJson(packaged))!.AsObject();
                    named["BasicSpeechObjectID"] = example.Label;
                    return named.ToJsonString();
                }
                catch (HttpRequestException) { Instruct($"I could not fetch the example {example.Label}."); return null; }"""),
("""        // TEXT: typed by the person, or the name of an App chosen from the list.
        devices.RegisterAcquire("OSD-BTO-V1.5", async (viaVad, wanted, abandon) =>
        {
            var stop = (_appStopping ?? _stopping)?.Token ?? CancellationToken.None;
""",
"""        // THE TABLE OF DAMAGED SECTIONS (Speech Restoration System): from, to and the words said there, filled in from the sections a ready-made recording comes
        // with. It gives the Damaged List; the words are kept for when the workflow asks for them.
        devices.RegisterAcquire("CAE-DLS-V2.5", async (viaVad, wanted, abandon) =>
        {
            var stop = (_appStopping ?? _stopping)?.Token ?? CancellationToken.None;
            sectionRows = (readySections ?? []).Select(r => new SectionRow { From = r.From, To = r.To, Words = r.Words }).ToList();
            if (sectionRows.Count == 0) sectionRows.Add(new SectionRow());
            sectionProblem = null;
            var chosen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            sectionsChosen = chosen; choosingSections = true;
            Refresh();
            using var c1 = stop.Register(() => chosen.TrySetResult(false));
            using var c2 = abandon.Register(() => chosen.TrySetResult(false));
            var ok = await chosen.Task;
            choosingSections = false; Refresh();
            if (!ok) return null;
            var rows = sectionRows.Select(r => new DamagedSections.Row(r.From, r.To, r.Words)).ToList();
            sectionWords = rows;
            return DamagedSections.DamagedList(rows);
        });

        // TEXT: typed by the person, or the name of an App chosen from the list.
        devices.RegisterAcquire("OSD-BTO-V1.5", async (viaVad, wanted, abandon) =>
        {
            var stop = (_appStopping ?? _stopping)?.Token ?? CancellationToken.None;

            // THE WORDS OF THE TABLE OF DAMAGED SECTIONS, kept when the table gave the Damaged List.
            if ((wanted ?? "").Contains(DamagedSections.WordsMarker, StringComparison.Ordinal) && sectionWords is { Count: > 0 } words)
                return DamagedSections.TextList(words);
"""),
("""    private void PickExample(TaskCompletionSource<bool> pick) => pick.TrySetResult(true);""",
"""    private void PickExample(TaskCompletionSource<bool> pick) => pick.TrySetResult(true);

    // The table of damaged sections, while the workflow waits for it.
    private sealed class SectionRow { public double From { get; set; } public double To { get; set; } public string Words { get; set; } = ""; }
    private List<SectionRow> sectionRows = new();
    private bool choosingSections;
    private string? sectionProblem;
    private TaskCompletionSource<bool>? sectionsChosen;
    private IReadOnlyList<DamagedSections.Row>? readySections, sectionWords;
    private void AddSection() { var last = sectionRows.LastOrDefault(); sectionRows.Add(new SectionRow { From = last?.To ?? 0, To = (last?.To ?? 0) + 0.5 }); }
    private void RemoveSection(SectionRow row) { if (sectionRows.Count > 1) sectionRows.Remove(row); }
    private void SectionsChosen()
    {
        sectionProblem = DamagedSections.Problem(sectionRows.Select(r => new DamagedSections.Row(r.From, r.To, r.Words)).ToList());
        if (sectionProblem is null) sectionsChosen?.TrySetResult(true);
    }"""),
])

edit("RcaShell.razor", [
("""  @if (choosingEmotion)""",
"""  @if (choosingSections)
  {
    <div class="languages sections">
      <table>
        <tr><th>From (s)</th><th>To (s)</th><th>Words said there</th><th></th></tr>
        @foreach (var r in sectionRows.ToList())
        {
          <tr>
            <td><input type="number" step="0.01" min="0" style="width:6em" @bind="r.From" /></td>
            <td><input type="number" step="0.01" min="0" style="width:6em" @bind="r.To" /></td>
            <td><input type="text" style="width:16em" @bind="r.Words" /></td>
            <td><button @onclick="() => RemoveSection(r)">Remove</button></td>
          </tr>
        }
      </table>
      <button @onclick="AddSection">Add a section</button>
      <button @onclick="SectionsChosen">OK</button>
      @if (sectionProblem is not null) { <span class="problem">@sectionProblem</span> }
    </div>
  }

  @if (choosingEmotion)"""),
])
print("ok")
