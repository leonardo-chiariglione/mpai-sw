using System.Collections.Concurrent;
using System.Text.Json;
using Mpai.Cav.Paper;

// N-35. dotnet run -c Release -- <mode> [options]
//   --probe                         each scenario once (core, dry, nominal sensing): the count of scenarios and the time of a run
//   --matrix  [--seeds 5] [--motions core,idm,planner] [--sensing all|nominal]   experiment 1 (and 3): scenarios x surfaces x sensing x seeds x motions
//   --failures [--seeds 3]          experiment 2: the driving core, dry, nominal sensing, every injection
//   --fast                          one scenario: each injection and each motion once (the Fast test does the same in-process)
//   --out <file.jsonl>              where the runs go (one JSON line each; appended)
var argv = args.ToList();
string Opt(string name, string dflt) { var i = argv.IndexOf(name); return i >= 0 && i + 1 < argv.Count ? argv[i + 1] : dflt; }
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var reports = Path.Combine(root, "Test", "Reports"); Directory.CreateDirectory(reports);
var mode = argv.FirstOrDefault(a => a is "--probe" or "--matrix" or "--failures" or "--fast") ?? "--probe";
var seeds = int.Parse(Opt("--seeds", mode == "--failures" ? "3" : "5"));
var motions = Opt("--motions", "core,idm,planner").Split(',');
var outFile = Opt("--out", Path.Combine(reports, $"cav-paper-runs{mode.Replace("--", "-")}.jsonl"));
var n = Runner.Count;
var surfaces = new[] { Surface.Dry, Surface.Wet, Surface.Ice };
var sensing = Opt("--sensing", "all") == "nominal" ? new[] { (150.0, 0.15) } : new[] { (150.0, 0.15), (150.0, 0.5), (60.0, 0.15), (60.0, 0.5) };
string[] injections = ["none", "miss-p1", "miss-p5", "miss-p20", "miss-stretch0.5", "miss-stretch2",
                       "phantom-pedestrian-frame", "phantom-pedestrian-1s", "phantom-car-frame", "phantom-car-1s", "phantom-obstacle-frame", "phantom-obstacle-1s"];

var jobs = new List<(int Index, Config C)>();
switch (mode)
{
    case "--probe": for (var i = 0; i < n; i++) jobs.Add((i, new Config("core", Surface.Dry, 150, 0.15, 1))); break;
    case "--matrix":
        foreach (var m in motions) foreach (var s in surfaces) foreach (var (r, z) in sensing) for (var seed = 1; seed <= seeds; seed++) for (var i = 0; i < n; i++)
            jobs.Add((i, new Config(m, s, r, z, seed)));
        break;
    case "--failures":
        foreach (var inj in injections) for (var seed = 1; seed <= seeds; seed++) for (var i = 0; i < n; i++) jobs.Add((i, new Config("core", Surface.Dry, 150, 0.15, seed, inj)));
        break;
    case "--fast":
        foreach (var inj in injections) jobs.Add((0, new Config("core", Surface.Dry, 150, 0.15, 1, inj)));
        foreach (var m in motions) jobs.Add((0, new Config(m, Surface.Dry, 150, 0.15, 1)));
        break;
}
// done already (a run stopped and started again): skipped
var done = new HashSet<string>();
if (File.Exists(outFile))
    foreach (var line in File.ReadLines(outFile))
        try { var r = JsonSerializer.Deserialize<RunResult>(line)!; done.Add(Key(r.Context, r.Variant, r.Motion, r.Surface, r.Range, r.Noise, r.Seed, r.Injection)); } catch { }
static string Key(string c, string v, string m, string s, double r, double z, int seed, string inj) => $"{c}|{v}|{m}|{s}|{r}|{z}|{seed}|{inj}";
var names = Enumerable.Range(0, n).Select(i => { var sc = Runner.Make(i); return (sc.Context, sc.Variant); }).ToArray();
jobs = jobs.Where(j => !done.Contains(Key(names[j.Index].Context, names[j.Index].Variant, j.C.Motion, j.C.Surface.Name, j.C.Range, j.C.Noise, j.C.Seed, j.C.Injection))).ToList();
Console.WriteLine($"{mode}: {n} scenarios, {jobs.Count} runs to do ({done.Count} done before), {Environment.ProcessorCount} processors; out {outFile}");

var clock = System.Diagnostics.Stopwatch.StartNew();
var writer = new StreamWriter(outFile, append: true) { AutoFlush = true };
var gate = new object(); var finished = 0; var errors = 0;
Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, job =>
{
    RunResult r;
    try { r = Runner.Run(job.Index, job.C); }
    catch (Exception e)
    {
        Interlocked.Increment(ref errors);
        var (ctx, variant) = names[job.Index];
        r = new RunResult(ctx, variant, job.C.Motion, job.C.Surface.Name, job.C.Range, job.C.Noise, job.C.Seed, job.C.Injection, "error: " + e.GetType().Name + ": " + e.Message.Split('\n')[0],
            false, null, 0, false, -1, -1, 0, 0, false, 0, 0, 0, 0, 0, 0, 0);
    }
    lock (gate)
    {
        writer.WriteLine(JsonSerializer.Serialize(r));
        if (++finished % 200 == 0 || finished == jobs.Count)
            Console.WriteLine($"{finished}/{jobs.Count} runs, {clock.Elapsed.TotalMinutes:F1} min, errors {errors}");
    }
});
writer.Dispose();
Console.WriteLine($"done: {jobs.Count} runs in {clock.Elapsed.TotalMinutes:F1} min, errors {errors}");
