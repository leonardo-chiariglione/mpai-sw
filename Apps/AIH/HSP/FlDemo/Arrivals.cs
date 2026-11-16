using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.SharedStorage;
using AIF.Store;
using Mpai.Aih;
using Mpai.Aih.Hfl;
using Mpai.Nnw.Watermarking;

namespace Mpai.Aih.FlDemo;

// --arrivals: HOW THE BACK END LEARNS FROM ONE TRAINED AIM AT A TIME, compared (2026-11-16). As in AIH-HSP, a Federated Learn Response carries ONE trained AIM; the
// Health Federated Learning AIM makes the Back End's new model from its current one and that AIM, each time one arrives. In each cycle every End User downloads the
// model published at the start of the cycle, trains it on its own cases (10 at the first cycle, 5 more at each later one; the same for any number of End Users) and
// returns it; the AIMs arrive one by one in a random order, so an AIM may have been trained from a model the Back End has since replaced (staleness). Every AIM is
// checked (the Back End's watermark, the distance), the new model is checked on the Back End's fixed set; scored on a test set no method sees. Synthetic images.
internal static partial class Program
{
    private sealed record Way(string Name, FlMethod Method, double Alpha = 0.5, bool VariedBank = false);

    private static readonly Way[] Ways =
    {
        new("Replace", FlMethod.Replace),
        new("FedAsync a=0.2", FlMethod.FedAsync, 0.2),
        new("FedAsync a=0.5", FlMethod.FedAsync, 0.5),
        new("Label bank, neutral inputs", FlMethod.LabelBank),
        new("Label bank, varied inputs", FlMethod.LabelBank, VariedBank: true),
    };

    // Images of no End User's device, each with a look of its own; the Back End holds them, unlabelled.
    private static List<float[]> VariedInputs(int count, int seed)
    {
        var rng = new Random(seed);
        return Enumerable.Range(0, count).Select(i =>
        {
            var d = new Device("varied", (float)(rng.NextDouble() * 0.08 - 0.04), (float)(0.04 + rng.NextDouble() * 0.05), (float)(0.75 + rng.NextDouble() * 0.35),
                               (float)(16 + rng.NextDouble() * 32), (float)(24 + rng.NextDouble() * 26), (float)(3 + rng.NextDouble() * 5));
            return Image(d, i % 2 == 0, rng);
        }).ToList();
    }

    // n End Users: End User i has the look of device i mod 3 (Alice, Bruno, Chiara) with small differences of its own.
    private static Device[] Population(int n, int seed)
    {
        var rng = new Random(seed * 7919 + n);
        return Enumerable.Range(0, n).Select(i =>
        {
            var b = EndUsers[i % EndUsers.Length];
            float J(double a) => (float)((rng.NextDouble() * 2 - 1) * a);
            return new Device($"{b.Name}{i / EndUsers.Length + 1}", b.Offset + J(0.015), b.Noise * (1 + J(0.15)), b.Contrast + J(0.05), b.Cx + J(3), b.Cy + J(3), b.Spread);
        }).ToArray();
    }

    private sealed record ArrivalStep(string Way, int Users, int Seed, int Cycle, int Arrived, int Accepted, double Test, double Loss, bool Mark, double Seconds);

    private static async Task CompareArrivals(string[] args)
    {
        string Opt(string name, string dflt) => args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? dflt;
        var userCounts = Opt("--users", "3,10,30").Split(',').Select(int.Parse).ToArray();
        var seeds = int.Parse(Opt("--seeds", "3")); var cycles = int.Parse(Opt("--cycles", "4")); var bankSize = int.Parse(Opt("--bank", "300"));
        var only = Opt("--ways", "");
        var ways = Ways.Where(w => only.Length == 0 || only.Split(',').Any(o => w.Name.StartsWith(o, StringComparison.OrdinalIgnoreCase))).ToList();

        Directory.CreateDirectory(Work);
        var networkPath = Path.Combine(Root, "Test", "Data", "Fl", "frozen-backbone.onnx");
        var checkSet = new[] { Neutral }.Concat(EndUsers).SelectMany((d, i) => Cases(d, 50, 9000 + i)).ToList();
        var testSet = new[] { Neutral }.Concat(EndUsers).SelectMany((d, i) => Cases(d, 100, 7000 + i)).ToList();
        var neutralInputs = Cases(Neutral, bankSize, 31).Select(c => c.Image).ToList();
        var variedInputs = VariedInputs(bankSize, 32);
        var amds = new AmdStore(Path.Combine(Root, "AIMs", "AMDs")); amds.Scan();
        var nnw = new NnwProvider(amds);
        var dwiPorts = AimPortReader.Load(amds, NnwProvider.Dwi); var wexPorts = AimPortReader.Load(amds, NnwProvider.Wex);
        const string payload = "1010010111000011";
        var locator = NnwJson.LocatorObject("wlc-backend", NnwJson.NctMethod(Bits, ValuesPerBit), "key:backend");
        using var judge = new FrontEndTrainer(networkPath, new FileSharedStorage(Path.Combine(Work, "judge"), "j", "j"));
        var clock = Stopwatch.StartNew();
        var gate = new SemaphoreSlim(Environment.ProcessorCount);
        JsonObject KeyOf(int seed) { var r = new Random(4242 + seed); return NnwJson.KeyObject("key-backend-" + seed, Enumerable.Range(0, 8).Select(_ => r.NextDouble())); }

        // the first model of each seed: the Back End's DWI with that seed's key, on 200 neutral cases
        var firsts = await Task.WhenAll(Enumerable.Range(0, seeds).Select(seed => Task.Run(async () =>
        {
            var dwi = nnw.Create(NnwProvider.Dwi, new Dictionary<string, string> { ["NetworkEpochs"] = FirstModelEpochs.ToString(), ["HeadEpochs"] = "80" }, null);
            var r = await dwi.ProcessAsync(new Message { MessageId = "dwi-first", Ports = new Dictionary<string, string> {
                [dwiPorts.Input(NnwJson.Model)] = NnwJson.UntrainedModel("model-untrained", File.ReadAllBytes(networkPath), judge.FeatureCount).ToJsonString(),
                [dwiPorts.Input(NnwJson.Dataset)] = NnwJson.DatasetObject("neutral-200", [1, FrontEndTrainer.ImageSide, FrontEndTrainer.ImageSide], Cases(Neutral, 200, 1 + 100 * seed).Select(c => (FrontEndTrainer.Standardise(c.Image), c.Finding))).ToJsonString(),
                [dwiPorts.Input(NnwJson.Payload)] = NnwJson.PayloadObject("pld-backend", payload).ToJsonString(), [dwiPorts.Input(NnwJson.Key)] = KeyOf(seed).ToJsonString(),
                [dwiPorts.Input(NnwJson.Locator)] = locator.ToJsonString() } });
            if (r.IsError) throw new InvalidOperationException(r.Payload);
            var m = JsonNode.Parse(r.Payload)!.AsObject(); m["MLModelObjectID"] = "model-0"; return m;
        })));
        var start = firsts.Select(m => judge.Score(m, testSet)).ToList();
        Say($"first models (one per seed), test set: {string.Join(", ", start.Select(s => s.Accuracy.ToString("P1")))}; {clock.Elapsed.TotalSeconds:F0} s");

        async Task<bool> Marked(IAimProcessor wex, JsonObject m, JsonObject key)
        {
            var r = await wex.ProcessAsync(new Message { MessageId = "wex", Ports = new Dictionary<string, string> {
                [wexPorts.Input(NnwJson.Model)] = m.ToJsonString(), [wexPorts.Input(NnwJson.Key)] = key.ToJsonString(), [wexPorts.Input(NnwJson.Locator)] = locator.ToJsonString() } });
            if (r.IsError) return false;
            var o = JsonNode.Parse(r.Payload)!.AsObject();
            return (string?)o["PayloadData"] == payload && ((string?)o["DescrMetadata"] ?? "").StartsWith("mark present", StringComparison.Ordinal);
        }

        var steps = new ConcurrentBag<ArrivalStep>();
        async Task Run(Way w, int users, int seed)
        {
            await gate.WaitAsync();
            try
            {
                var t0 = clock.Elapsed.TotalSeconds;
                var key = KeyOf(seed); var people = Population(users, seed); var rng = new Random(seed * 31 + users);
                var wex = nnw.Create(NnwProvider.Wex, new Dictionary<string, string>(), null);
                var ports = AimPortReader.Load(amds, AihProvider.Hfl);
                using var runner = new FrontEndTrainer(networkPath, new FileSharedStorage(Path.Combine(Work, "r-" + Guid.NewGuid().ToString("N")), "r", "r"));
                var scores = new ConcurrentDictionary<string, (double, double)>();
                var hfl = new HflAimProcessor("hfl", ports) { Method = w.Method, Alpha = w.Alpha,
                    Bank = w.Method == FlMethod.LabelBank ? new FlLabelBank(runner, w.VariedBank ? variedInputs : neutralInputs, ownCases: 200) : null,
                    Policy = new RoundPolicy { MaxDistance = MaxDistance,
                        Evaluate = m => { var id = (string?)m["MLModelObjectID"] ?? ""; return id == "candidate" ? judge.Score(m, checkSet) : scores.GetOrAdd(id, _ => judge.Score(m, checkSet)); } } };
                hfl.StartFrom(firsts[seed]);
                var vaults = people.Select(_ => Path.Combine(Work, "v-" + Guid.NewGuid().ToString("N"))).ToArray();
                JsonObject Fdl(JsonObject item) => new() { ["Header"] = "AIH-FDL-V1.1", ["FederatedLearnID"] = AihJson.NewId("fdl"), ["FederatedLearnTime"] = AihJson.Time(), ["FederatedLearnData"] = new JsonArray(item) };
                var arrived = 0; var accepted = 0;
                for (var cycle = 1; cycle <= cycles; cycle++)
                {
                    var published = hfl.CurrentModel!.DeepClone().AsObject();
                    var trained = new List<JsonObject>();
                    for (var u = 0; u < people.Length; u++)
                    {
                        using var fe = new FrontEndTrainer(networkPath, new FileSharedStorage(vaults[u], "u", "u"));
                        var k = 0;
                        foreach (var (img, f) in Cases(people[u], cycle == 1 ? 10 : 5, 100000 * seed + 1000 * cycle + u)) fe.KeepCase($"case-{cycle}-{k++:D3}", img, f);
                        trained.Add(fe.Train(published, epochs: 0, networkEpochs: FrontEndEpochs));
                    }
                    foreach (var m in trained.OrderBy(_ => rng.Next()))                    // they arrive one by one
                    {
                        arrived++;
                        if (!await Marked(wex, m, key)) continue;
                        var before = hfl.Outcomes.Count;
                        await hfl.ProcessAsync(new Message { MessageId = Guid.NewGuid().ToString("N"), Ports = new Dictionary<string, string> {
                            [ports.Input(AihJson.FederatedLearn)] = Fdl(new JsonObject { ["Response"] = new JsonObject { ["AIHDataID"] = "aih-data", ["PostLearnNNModel"] = m } }).ToJsonString() } });
                        if (hfl.Outcomes.Count > before && hfl.Outcomes[^1].Accepted) accepted++;
                    }
                    var t = judge.Score(hfl.CurrentModel!, testSet);
                    steps.Add(new ArrivalStep(w.Name, users, seed, cycle, arrived, accepted, t.Accuracy, t.LogLoss, await Marked(wex, hfl.CurrentModel!, key), clock.Elapsed.TotalSeconds - t0));
                }
                foreach (var d in vaults) try { Directory.Delete(d, true); } catch { }
                Say($"   done: {w.Name}, {users} End Users, seed {seed}: test {steps.Where(s => s.Way == w.Name && s.Users == users && s.Seed == seed).MaxBy(s => s.Cycle)!.Test:P1}; {clock.Elapsed.TotalMinutes:F1} min");
            }
            catch (Exception ex) { Say($"   FAILED: {w.Name}, {users}, seed {seed}: {ex.Message}"); }
            finally { gate.Release(); }
        }

        var jobs = (from users in userCounts.OrderByDescending(n => n) from seed in Enumerable.Range(0, seeds) from w in ways select (w, users, seed)).ToList();
        Say($"{jobs.Count} runs ({ways.Count} ways x {userCounts.Length} numbers of End Users x {seeds} seeds), {cycles} cycles, {Environment.ProcessorCount} at a time");
        await Task.WhenAll(jobs.Select(j => Task.Run(() => Run(j.w, j.users, j.seed))));

        string Ms(IEnumerable<double> xs, string f) { var a = xs.ToArray(); if (a.Length == 0) return "-"; var m = a.Average(); var sd = Math.Sqrt(a.Sum(x => (x - m) * (x - m)) / Math.Max(1, a.Length - 1)); return $"{m.ToString(f)} ± {sd.ToString(f)}"; }
        var report = new StringBuilder();
        report.AppendLine($"# One trained AIM at a time: how the Back End learns ({DateTime.Now:yyyy-MM-dd HH:mm})");
        report.AppendLine();
        report.AppendLine($"{seeds} runs per cell; {cycles} cycles; each End User 10 cases at the first cycle and 5 more at each later one; the AIMs arrive one by one in a random order; label bank of {bankSize} of the Back End's own inputs. First models on the test set: {Ms(start.Select(s => s.Accuracy * 100), "F1")} %, loss {Ms(start.Select(s => s.LogLoss), "F3")}. Test set: 400 cases no method sees. Mean ± standard deviation over the runs.");
        report.AppendLine();
        report.AppendLine("| Way | End Users | AIMs arrived | Accepted | Test accuracy after cycle 1 (%) | Test accuracy at the end (%) | Test loss at the end | Mark at the end | Minutes per run |");
        report.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var users in userCounts)
            foreach (var w in ways)
            {
                var cell = steps.Where(s => s.Way == w.Name && s.Users == users).ToList();
                if (cell.Count == 0) continue;
                var end = cell.Where(s => s.Cycle == cycles).ToList(); var c1 = cell.Where(s => s.Cycle == 1).ToList();
                report.AppendLine($"| {w.Name} | {users} | {(end.Count > 0 ? end.Average(s => s.Arrived) : 0):F0} | {(end.Count > 0 ? end.Average(s => (double)s.Accepted / Math.Max(1, s.Arrived)) : 0):P0} | {Ms(c1.Select(s => s.Test * 100), "F1")} | {Ms(end.Select(s => s.Test * 100), "F1")} | {Ms(end.Select(s => s.Loss), "F3")} | {end.Count(s => s.Mark)} of {end.Count} | {(end.Count > 0 ? end.Average(s => s.Seconds) / 60 : 0):F1} |");
            }
        report.AppendLine();
        report.AppendLine($"Time: {clock.Elapsed.TotalMinutes:F1} min.");
        var outFile = Path.Combine(Root, "Test", "Results", DateTime.Now.ToString("yyyy-MM-dd"), $"fl-arrivals-{string.Join("-", userCounts)}.md");
        Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
        File.WriteAllText(outFile, report.ToString());
        Say(""); Say(report.ToString()); Say("written: " + outFile);
    }
}
