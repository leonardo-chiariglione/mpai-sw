// N-27: the test of the five LJ networks (and the baseline not fine-tuned).
//
//   SrsLjMeasure <out folder> <network> [<network> ...]          e.g. C:\Users\Thor\Downloads\SRS-LJ-test LJ-00min LJ-01min ...
//
// For each network and each held-out clip of Test\Data\Speech\LJSpeech (sections.json): the SRS App (SrsApp --network) restores the damaged clip -
// Speech Model Creation gives the network, Text To Speech speaks the words of the two sections with it, Speech Restoration Assembly puts them in.
// Measures: SPEAKER SIMILARITY - the cosine of the ECAPA embedding (Models\ecapa-tdnn.onnx) of the two restored sections (found in the restored file
// by matching the untouched speech around them) with the mean embedding of the speaker's real voice (the ten validation clips, never trained on);
// for reference, the same for the original sections (the real voice at those places) and for the other reader (the baseline). WORDS - Whisper
// (ggml-small) on the restored clip: how many of the four words of the two sections it hears, in order, and the word error rate of the whole clip
// against Whisper's words on the original clip. Writes <out>\<network>\<clip>.wav, <out>\originals, <out>\damaged, and Test\Reports\srs-lj-test.json.
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using Mpai.Mmc.Sir;
using Mpai.Osd.Speech;

var root = FindRoot();
var outDir = args[0];
var networks = args.Skip(1).ToArray();
var data = Path.Combine(root, "Test", "Data", "Speech", "LJSpeech");
var srs = Path.Combine(root, "Apps", "CAE", "SRS", "App", "bin", "Debug", "net10.0", "SrsApp.exe");
var whisper = Path.Combine(root, "Models", "Whisper", "bin", "whisper-cli.exe");
var whisperModel = Path.Combine(root, "Models", "Whisper", "models", "ggml-small.bin");
var voice = Directory.GetFiles(Path.Combine(data, "validation"), "*.wav").OrderBy(f => f).ToArray();
var clips = JsonNode.Parse(File.ReadAllText(Path.Combine(data, "sections.json")))!.AsArray().Select(n => n!.AsObject()).ToList();
using var ecapa = new SpeakerEmbedder(Path.Combine(root, "Models", "ecapa-tdnn.onnx"));

Directory.CreateDirectory(Path.Combine(outDir, "originals")); Directory.CreateDirectory(Path.Combine(outDir, "damaged"));
foreach (var c in clips)
{
    var id = (string)c["clip"]!;
    File.Copy(Path.Combine(data, "original16k", id + ".wav"), Path.Combine(outDir, "originals", id + ".wav"), true);
    File.Copy(Path.Combine(data, "damaged", id + ".wav"), Path.Combine(outDir, "damaged", id + ".wav"), true);
}

// the speaker's real voice: the mean embedding of the validation clips
var reference = Mean(voice.Select(v => ecapa.Embed(Floats(Read16(v)))).ToList());
var report = File.Exists(ReportPath()) ? JsonNode.Parse(File.ReadAllText(ReportPath()))!.AsObject() : new JsonObject();
var originalWords = new Dictionary<string, string[]>();

// the real voice at the places of the sections (the ceiling of the similarity)
if (report["original"] is null)
{
    var sims = new List<double>();
    foreach (var c in clips)
    {
        var a = Read16(Path.Combine(data, "original16k", (string)c["clip"]! + ".wav"));
        var parts = c["sections"]!.AsArray().Select(s => Slice(a, (double)s!["from"]!, (double)s["to"]!)).SelectMany(x => x).ToArray();
        sims.Add(SpeakerEmbedder.Cosine(ecapa.Embed(Floats(parts)), reference));
    }
    report["original"] = new JsonObject { ["similarity_mean"] = Math.Round(sims.Average(), 4), ["similarity_min"] = Math.Round(sims.Min(), 4), ["note"] = "the real voice at the places of the sections, against the validation clips" };
}

foreach (var c in clips)
    originalWords[(string)c["clip"]!] = Words(Transcribe(Path.Combine(data, "original16k", (string)c["clip"]! + ".wav")));

foreach (var network in networks)
{
    var folder = Path.Combine(outDir, network); Directory.CreateDirectory(folder);
    var rows = new JsonArray(); var clock = Stopwatch.StartNew();
    foreach (var c in clips)
    {
        var id = (string)c["clip"]!;
        var sections = c["sections"]!.AsArray().Select(s => ((double)s!["from"]!, (double)s["to"]!, (string)s["words"]!)).ToList();
        var output = Path.Combine(folder, id + ".wav");
        var arguments = new List<string> { "--network", network, "--voice" }; arguments.AddRange(voice);
        arguments.AddRange(["--damaged", Path.Combine(data, "damaged", id + ".wav"),
            "--sections", string.Join(",", sections.Select(s => $"{s.Item1.ToString(CultureInfo.InvariantCulture)}-{s.Item2.ToString(CultureInfo.InvariantCulture)}"))]);
        foreach (var s in sections) arguments.AddRange(["--say", s.Item3]);
        arguments.AddRange(["--out", output]);
        var t = Stopwatch.StartNew();
        var (code, text) = Run(srs, arguments, root);
        var row = new JsonObject { ["clip"] = id, ["seconds_to_restore"] = Math.Round(t.Elapsed.TotalSeconds, 1) };
        if (code != 0 || !File.Exists(output)) { row["error"] = text.Trim(); rows.Add(row); Console.WriteLine($"{network} {id}: FAILED {text.Trim()}"); continue; }
        var restored = Read16(output); var damaged = Read16(Path.Combine(data, "damaged", id + ".wav"));
        var pieces = Pieces(damaged, restored, sections.Select(s => (s.Item1, s.Item2)).ToList());
        if (pieces is null) row["error"] = "the restored sections could not be found in the restored file";
        else
        {
            row["similarity"] = Math.Round(SpeakerEmbedder.Cosine(ecapa.Embed(Floats(pieces.SelectMany(p => p).ToArray())), reference), 4);
            row["synthesised_seconds"] = Math.Round(pieces.Sum(p => p.Length) / 16000.0, 2);
        }
        var heard = Words(Transcribe(output));
        var expected = sections.SelectMany(s => s.Item3.Split(' ')).ToArray();
        row["section_words"] = expected.Length;
        row["section_words_heard"] = HeardInOrder(sections.Select(s => s.Item3.Split(' ')).ToList(), heard);
        row["wer_vs_original"] = Math.Round(Wer(originalWords[id], heard), 4);
        row["heard"] = string.Join(' ', heard);
        rows.Add(row);
        Console.WriteLine($"{network} {id}: sim {row["similarity"]} words {row["section_words_heard"]}/{expected.Length} wer {row["wer_vs_original"]}");
    }
    var ok = rows.OfType<JsonObject>().Where(r => r["error"] is null).ToList();
    report[network] = new JsonObject
    {
        ["clips"] = rows.Count, ["restored"] = ok.Count,
        ["similarity_mean"] = ok.Count > 0 ? Math.Round(ok.Average(r => (double)r["similarity"]!), 4) : null,
        ["similarity_min"] = ok.Count > 0 ? Math.Round(ok.Min(r => (double)r["similarity"]!), 4) : null,
        ["section_words_heard"] = ok.Sum(r => (int)r["section_words_heard"]!), ["section_words"] = ok.Sum(r => (int)r["section_words"]!),
        ["wer_mean"] = ok.Count > 0 ? Math.Round(ok.Average(r => (double)r["wer_vs_original"]!), 4) : null,
        ["minutes"] = Math.Round(clock.Elapsed.TotalMinutes, 1),
        ["rows"] = rows
    };
    Directory.CreateDirectory(Path.GetDirectoryName(ReportPath())!);
    File.WriteAllText(ReportPath(), report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}

Console.WriteLine();
Console.WriteLine("| Network | Clips restored | Speaker similarity (mean, min) | Section words heard | WER vs original |");
Console.WriteLine("|---|---|---|---|---|");
foreach (var (name, node) in report)
{
    if (name == "original") { Console.WriteLine($"| real voice at the sections | - | {node!["similarity_mean"]}, {node["similarity_min"]} | - | - |"); continue; }
    Console.WriteLine($"| {name} | {node!["restored"]}/{node["clips"]} | {node["similarity_mean"]}, {node["similarity_min"]} | {node["section_words_heard"]}/{node["section_words"]} | {node["wer_mean"]} |");
}
return 0;

string ReportPath() => Path.Combine(root, "Test", "Reports", "srs-lj-test.json");

static string FindRoot()
{
    for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        if (Directory.Exists(Path.Combine(d.FullName, "AIMs", "AMDs"))) return d.FullName;
    throw new InvalidOperationException("no root");
}

static short[] Read16(string path) { var a = Audio.FromWav(File.ReadAllBytes(path)); return (a.Rate == 16000 ? a : a.At(16000)).Samples; }
static float[] Floats(short[] s) => s.Select(v => v / 32768f).ToArray();
static short[] Slice(short[] a, double from, double to) => a.Skip((int)(from * 16000)).Take((int)((to - from) * 16000)).ToArray();

static float[] Mean(List<float[]> list)
{
    var m = new float[list[0].Length];
    foreach (var e in list) for (var i = 0; i < m.Length; i++) m[i] += e[i] / list.Count;
    var n = (float)Math.Sqrt(m.Sum(v => v * (double)v));
    return m.Select(v => v / n).ToArray();
}

// The synthesised pieces: the Assembly copies the untouched speech sample for sample, so the speech between and after the sections is found exactly in the restored file.
static List<short[]>? Pieces(short[] damaged, short[] restored, List<(double From, double To)> sections)
{
    var result = new List<short[]>(); var cursorD = 0; var cursorR = 0;
    for (var k = 0; k < sections.Count; k++)
    {
        var s = (int)Math.Round(sections[k].From * 16000); var e = (int)Math.Round(sections[k].To * 16000);
        cursorR += s - cursorD;                                       // the untouched speech before the section has the same length
        var nextStart = k + 1 < sections.Count ? (int)Math.Round(sections[k + 1].From * 16000) : damaged.Length;
        var gap = Math.Min(400, nextStart - e);
        int found;
        if (gap <= 0) found = k + 1 < sections.Count ? -1 : restored.Length;
        else found = Find(restored, cursorR, damaged.AsSpan(e, gap));
        if (found < cursorR) return null;
        result.Add(restored[cursorR..found]);
        cursorR = found; cursorD = e;
    }
    return result;
}

static int Find(short[] hay, int from, ReadOnlySpan<short> needle)
{
    for (var i = from; i + needle.Length <= hay.Length; i++)
        if (hay.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
    return -1;
}

string Transcribe(string wav)
{
    var (_, text) = Run(whisper, ["-m", whisperModel, "-f", wav, "-l", "en", "-nt", "-np"], root);
    return text;
}

static string[] Words(string text) => Regex.Replace(text.ToLowerInvariant().Replace("-", " "), "[^a-z' ]", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);

// the words of each section, heard in order in the transcript (each section's words looked for after the previous section's)
static int HeardInOrder(List<string[]> sections, string[] heard)
{
    var count = 0; var at = 0;
    foreach (var words in sections)
        foreach (var w in words)
        {
            var i = Array.IndexOf(heard, w, at);
            if (i >= 0) { count++; at = i + 1; }
        }
    return count;
}

static double Wer(string[] reference, string[] hypothesis)
{
    var d = new int[reference.Length + 1, hypothesis.Length + 1];
    for (var i = 0; i <= reference.Length; i++) d[i, 0] = i;
    for (var j = 0; j <= hypothesis.Length; j++) d[0, j] = j;
    for (var i = 1; i <= reference.Length; i++)
        for (var j = 1; j <= hypothesis.Length; j++)
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (reference[i - 1] == hypothesis[j - 1] ? 0 : 1));
    return reference.Length == 0 ? 0 : d[reference.Length, hypothesis.Length] / (double)reference.Length;
}

static (int Code, string Text) Run(string exe, IEnumerable<string> arguments, string cwd)
{
    var info = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = cwd };
    foreach (var a in arguments) info.ArgumentList.Add(a);
    using var p = Process.Start(info)!;
    var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
    if (!p.WaitForExit(TimeSpan.FromMinutes(5))) { p.Kill(true); return (-1, "timeout"); }
    return (p.ExitCode, o.Result + e.Result);
}
