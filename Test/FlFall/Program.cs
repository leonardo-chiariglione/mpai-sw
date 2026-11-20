using System.Globalization;
using System.Text.Json;
using Mpai.Aih.FlFall;
using Mpai.Aih.Hfl;

// dotnet run -c Release -- <method> <seed> [--rounds 25] [--epochs 100] [--csv path] [--out file.jsonl]
//   local     each End User trains alone (rounds x epochs passes, from the same start model); no Back End model
//   central   all the End Users' training windows together, at one place (rounds x epochs passes / 10, it sees 13 times the data a pass)
//   fedavg    the thesis's baseline: rounds; every End User trains from the Back End's model; the Back End takes the UNWEIGHTED mean (NOT how HSP works)
//   addlearn  HSP: in a round every End User starts from the model it downloaded; the trained models arrive one at a time in random order; the Back End adds each one's
//             learning: new = current + step (arrived - started from), step = 0.5 / sqrt(1 + staleness) (FlMethods.AddLearning, AIMs\AIH\V1.1\HFL)
//   teacher   HSP: the same arrivals; each arrived model is a TEACHER and the Back End's model the STUDENT: the Back End makes up inputs (no End User data, none of its
//             own), the teacher answers them, the student is trained on the answers. Nothing is averaged.
// Split per seed: of the 17 subjects 13 are End Users (training), 2 development (unused), 2 test (the "global" evaluation), as in the thesis (UP-Fall: 13/2/2).
// Each End User keeps its trial 3 of every activity as its own held-out data ("local" evaluation) and trains on trials 1 and 2. Every End User standardises its own
// features with its own mean and deviation (nothing about its data leaves it).
var a = args.ToList();
string Opt(string n, string d) { var i = a.IndexOf(n); return i >= 0 && i + 1 < a.Count ? a[i + 1] : d; }
var method = a[0]; var seed = int.Parse(a[1]);
int rounds = int.Parse(Opt("--rounds", "25")), epochs = int.Parse(Opt("--epochs", "100"));
var csvPath = Opt("--csv", @"D:\AI\Test\Data\Fall\upfall-windows.csv");
var outPath = Opt("--out", @"D:\AI\Test\Results\2026-11-19\fl-fall\runs.jsonl");
Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
var clock = System.Diagnostics.Stopwatch.StartNew();

// ---- the windows, by subject ----
var rows = File.ReadLines(csvPath).Skip(1).Select(l => l.Split(',')).Select(f => (S: int.Parse(f[0]), Trial: int.Parse(f[2]), Y: float.Parse(f[3]),
    X: f.Skip(4).Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray())).ToList();
var subjects = rows.Select(r => r.S).Distinct().OrderBy(s => s).ToList();
var rng = new Random(seed);
var shuffled = subjects.OrderBy(_ => rng.Next()).ToList();
var train = shuffled.Take(13).ToList(); var test = shuffled.Skip(15).Take(2).ToList();

User MakeUser(int s, bool all)
{
    var mine = rows.Where(r => r.S == s).ToList();
    var fit = all ? mine : mine.Where(r => r.Trial != 3).ToList();
    int d = mine[0].X.Length; var mu = new double[d]; var sd = new double[d];
    for (var j = 0; j < d; j++) { mu[j] = fit.Average(r => r.X[j]); sd[j] = Math.Sqrt(fit.Average(r => (r.X[j] - mu[j]) * (r.X[j] - mu[j]))) + 1e-6; }
    float[] Z(float[] x) => x.Select((v, j) => (float)((v - mu[j]) / sd[j])).ToArray();
    var tr = all ? mine : mine.Where(r => r.Trial != 3).ToList(); var ho = all ? new() : mine.Where(r => r.Trial == 3).ToList();
    return new User(s, tr.Select(r => Z(r.X)).ToList(), tr.Select(r => r.Y).ToList(), ho.Select(r => Z(r.X)).ToList(), ho.Select(r => r.Y).ToList());
}
var users = train.Select(s => MakeUser(s, false)).ToList();
var testers = test.Select(s => MakeUser(s, true)).ToList();
var testX = testers.SelectMany(u => u.TrainX).ToList(); var testY = testers.SelectMany(u => u.TrainY).ToList();

var start = Mlp.Init(new Random(seed * 31 + 7));
var localModels = new float[users.Count][];
float[]? backEnd = null;
var arrivals = 0;
var trainRng = new Random(seed * 1009 + 1);

switch (method)
{
    case "local":
        Parallel.For(0, users.Count, i => localModels[i] = Mlp.Train(start, users[i].TrainX, users[i].TrainY, Math.Min(rounds * epochs, 500), new Random(seed * 97 + i)));
        break;
    case "central":
        {
            var xs = users.SelectMany(u => u.TrainX).ToList(); var ys = users.SelectMany(u => u.TrainY).ToList();
            backEnd = Mlp.Train(start, xs, ys, Math.Max(1, rounds * epochs / 10), trainRng);
            Parallel.For(0, users.Count, i => localModels[i] = backEnd);              // the central model is everybody's model
            break;
        }
    case "fedavg":
    case "addlearn":
    case "teacher":
        {
            backEnd = start;
            var version = 0;
            for (var r = 0; r < rounds; r++)
            {
                var downloaded = backEnd; var downloadedVersion = version;
                var trained = new float[users.Count][];
                Parallel.For(0, users.Count, i => trained[i] = Mlp.Train(downloaded, users[i].TrainX, users[i].TrainY, epochs, new Random(seed * 7919 + r * 131 + i)));
                for (var i = 0; i < users.Count; i++) localModels[i] = trained[i];
                if (method == "fedavg") { backEnd = FlMethods.Average(trained, trained.Select(_ => 1).ToArray()); version++; continue; }
                foreach (var i in Enumerable.Range(0, users.Count).OrderBy(_ => trainRng.Next()))      // the arrivals, one at a time
                {
                    var staleness = version - downloadedVersion;
                    backEnd = method == "addlearn"
                        ? FlMethods.AddLearning(backEnd, trained[i], downloaded, FlMethods.MixShare(0.5, staleness))
                        : Teach(backEnd, trained[i], new Random(seed * 65537 + r * 257 + i));
                    version++; arrivals++;
                }
            }
            break;
        }
    default: throw new ArgumentException(method);
}

// THE TEACHER: 2,048 made-up inputs (standardised feature space: a normal sample, half of them spread wider to reach the rare region of falls); the teacher's answers
// (probabilities) are the targets; the student is trained 3 passes on them. Neither the Back End nor this step sees any End User's windows.
static float[] Teach(float[] student, float[] teacher, Random r)
{
    var xs = new List<float[]>();
    for (var k = 0; k < 2048; k++) { var spread = k % 2 == 0 ? 1.0 : 2.5; xs.Add(Enumerable.Range(0, Mlp.Sizes[0]).Select(_ => (float)(Normal(r) * spread)).ToArray()); }
    var ys = Mlp.Probabilities(teacher, xs).Select(p => (float)p).ToList();
    return Mlp.Train(student, xs, ys, 3, r, dropout: 0f);
}
static double Normal(Random r) => Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());

// ---- the measures ----
// LOCAL: every End User's own model (after its last training) on its own held-out trial 3; the mean over the End Users.
// GLOBAL: the Back End's model on the two test subjects' windows (local-only: the mean of the End Users' models on them).
var local = users.Select((u, i) => Metrics.Of(Mlp.Probabilities(localModels[i], u.HoldX), u.HoldY)).ToList();
var global = backEnd is not null ? Metrics.Of(Mlp.Probabilities(backEnd, testX), testY)
    : Metrics.Mean(localModels.Select(m => Metrics.Of(Mlp.Probabilities(m, testX), testY)).ToList());
var result = new
{
    Method = method, Seed = seed, Rounds = rounds, Epochs = epochs, Train = train, Test = test, Arrivals = arrivals,
    Local = Metrics.Mean(local), Global = global, Seconds = Math.Round(clock.Elapsed.TotalSeconds, 1)
};
// A measure that cannot be computed (no fall among the test subjects' windows: no PR-AUC, no miss rate) is written as no value (null), not a number;
// the run is kept and the summary leaves it out of that measure only.
var line = JsonSerializer.Serialize(result, new JsonSerializerOptions { Converters = { new NoValueForNaN() } });
lock (typeof(Program)) File.AppendAllText(outPath, line + Environment.NewLine);
Console.WriteLine(line);

public sealed record User(int Subject, List<float[]> TrainX, List<float> TrainY, List<float[]> HoldX, List<float> HoldY);

public sealed record Metrics(double PrAuc, double RocAuc, double F1, double MissRate, double FalseAlarmRate, int Positives, int Cases)
{
    public static Metrics Of(double[] p, IReadOnlyList<float> y)
    {
        var pos = y.Count(v => v > 0.5); var neg = y.Count - pos;
        var order = Enumerable.Range(0, p.Length).OrderByDescending(i => p[i]).ToArray();
        // average precision (the PR-AUC of scikit-learn's average_precision_score)
        double ap = 0, tp = 0, fp = 0, prevRecall = 0;
        foreach (var i in order) { if (y[i] > 0.5) tp++; else fp++; if (y[i] > 0.5) { var rec = tp / Math.Max(1, pos); ap += (rec - prevRecall) * tp / (tp + fp); prevRecall = rec; } }
        // ROC-AUC by ranks
        double rankSum = 0; var ranks = order.Reverse().ToArray();
        for (var k = 0; k < ranks.Length; k++) if (y[ranks[k]] > 0.5) rankSum += k + 1;
        var roc = pos == 0 || neg == 0 ? double.NaN : (rankSum - pos * (pos + 1) / 2.0) / ((double)pos * neg);
        int TP = 0, FP = 0, FN = 0, TN = 0;
        for (var i = 0; i < p.Length; i++) { var hit = p[i] >= 0.5; var f = y[i] > 0.5; if (hit && f) TP++; else if (hit) FP++; else if (f) FN++; else TN++; }
        var f1 = 2.0 * TP / Math.Max(1, 2 * TP + FP + FN);
        return new Metrics(pos == 0 ? double.NaN : ap, roc, f1, pos == 0 ? double.NaN : (double)FN / pos, neg == 0 ? double.NaN : (double)FP / neg, pos, y.Count);
    }
    public static Metrics Mean(IReadOnlyList<Metrics> ms)
    {
        double M(Func<Metrics, double> f) => ms.Select(f).Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Average();
        return new Metrics(M(m => m.PrAuc), M(m => m.RocAuc), M(m => m.F1), M(m => m.MissRate), M(m => m.FalseAlarmRate), ms.Sum(m => m.Positives), ms.Sum(m => m.Cases));
    }
}

public sealed class NoValueForNaN : System.Text.Json.Serialization.JsonConverter<double>
{
    public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.Null ? double.NaN : reader.GetDouble();
    public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) writer.WriteNullValue(); else writer.WriteNumberValue(value);
    }
}
