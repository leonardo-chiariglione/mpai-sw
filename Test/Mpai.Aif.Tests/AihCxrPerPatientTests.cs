using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using AIF.Controller;
using AIF.SharedStorage;
using AIF.Store;
using Mpai.Aih.Hfl;
using Mpai.Nnw.Watermarking;
using Mpai.Onnx;
using Xunit.Abstractions;

namespace Mpai.Aif.Tests;

// AIH-HSP, NIGHT REQUEST N-33, EXPERIMENT 1 (run by day 2026-11-17 at the author's request): FEDERATED LEARNING ON NIH CHEST X-RAYS WITH ONE END USER PER PATIENT.
// NOT A CLINICAL TEST: the labels are mined by NLP from the radiology reports (noisy).
//
// The data: images_001 to images_004 of NIH ChestX-ray14 (34,999 images); the 1024 features of the frozen TorchXRayVision DenseNet-121 (CheXpert weights) of every
// image were computed once on 2026-11-15 (Test\Tools\FlLab\cxr_features_more.py -> Test\Data\ChestXray\features\cxr-all-*). The trained network is the head of N-22
// (Test\Data\Fl\cxr-head.onnx: a fixed gain, Gemm 1024 -> 64, Relu) with the final layer (64 weights and a bias).
//
// The split, BY PATIENT: the images of the official test list are the Back End's fixed check set; of the other patients, 10% (fixed seed) are the Back End's own; every
// other patient is ONE END USER (in AIH-HSP an End User is a person with a Health Front End). An End User with two images or more keeps its last one apart (held-out):
// the threshold of every model is fixed on the pooled held-out images (Youden's J), never on the check set.
//
// The Back End's first model is made by the During-Training Watermark Insertion AIM (NNW-DWI V2.0) on its own images, with its Key and Payload. Each round, every End
// User trains the current model on its own images (5 passes, head and final layer together, as N-22); the Watermark Extraction AIM (NNW-WEX V2.0) checks every
// returned model (no mark: refused); the models are averaged, weighted by their cases (what Health Federated Learning does in a round); the quality gate judges the
// AUC on images of the Back End kept apart from its training (kept unless it falls by more than 0.01), and what the accuracy-at-0.5 gate of HFL would have done is reported too. 20 rounds.
// Measured on the check set: AUC, accuracy, sensitivity, specificity at the threshold, with 95% intervals (bootstrap over PATIENTS), and the watermark.
public class AihCxrPerPatientTests(ITestOutputHelper output)
{
    private static string Features => Path.Combine(Repository.Root, "Test", "Data", "ChestXray", "features");
    private static string Head => Path.Combine(Repository.Root, "Test", "Data", "Fl", "cxr-head.onnx");
    private static bool Ready => File.Exists(Head) && File.Exists(Path.Combine(Features, "cxr-all-features.bin"));

    // A small subset (about 400 patients outside the check set, 60 in it), 2 rounds: the whole experiment runs, the mark is found, the measures are numbers.
    [SkippableFact]
    [Trait("Group", "Long")]
    public void TheExperimentRunsOnASmallSubset()
    {
        Skip.IfNot(Ready, "the head or the cached features of the four archives are not there");
        var report = CxrPerPatient.Run(CxrPerPatient.Load(Features, patients: 460), Head, AihCxrRoundTests.CxrTask.Any, rounds: 2, measureAt: [1, 2], draws: 100, localsSampled: 20, say: output.WriteLine);
        foreach (var m in report["Models"]!.AsArray()) { Assert.True((bool)m!["Watermark"]!["Found"]!, $"the mark in {m["Model"]}"); Assert.InRange((double)m["AUC"]![0]!, 0.0, 1.0); }
    }

    // EXPERIMENT 1: all the images, 20 rounds, the two tasks; writes Test\Reports\aih-cxr-per-patient.json.
    [SkippableFact]
    [Trait("Group", "Long")]
    public void ExperimentOneOnAllTheImages()
    {
        Skip.IfNot(Ready, "the head or the cached features of the four archives are not there");
        var data = CxrPerPatient.Load(Features);
        var tasks = new JsonArray();
        foreach (var task in new[] { AihCxrRoundTests.CxrTask.Any, AihCxrRoundTests.CxrTask.Effusion })
        {
            tasks.Add(CxrPerPatient.Run(data, Head, task, rounds: 20, measureAt: [1, 5, 10, 20], draws: 1000, localsSampled: 300, say: output.WriteLine));
            Write(tasks);   // written after each task, so a long run leaves its first half
        }
    }

    private static void Write(JsonArray tasks)
    {
        var file = Path.Combine(Repository.Root, "Test", "Reports", "aih-cxr-per-patient.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, new JsonObject { ["Request"] = "N-33 experiment 1", ["Made"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), ["Tasks"] = tasks.DeepClone() }
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal static class CxrPerPatient
{
    public const int FeatureCount = 1024, Bits = 16, ValuesPerBit = 16, FrontEndNetworkEpochs = 5;
    public const float NetworkRate = 0.0002f;
    public const double GateTolerance = 0.01;
    private const string Payload = "1010010111000011";

    public static List<CxrRound.Image> Load(string folder, int? patients = null)
    {
        var x = AihCxrRoundTests.Floats(Path.Combine(folder, "cxr-all-features.bin"));
        var lines = File.ReadAllLines(Path.Combine(folder, "cxr-all-index.csv")).Skip(1).ToList();
        if (x.Length != lines.Count * FeatureCount) throw new InvalidDataException($"{lines.Count} images in the index, {x.Length / FeatureCount} feature vectors.");
        var images = lines.Select((l, i) =>
        {
            var f = l.Split(',');
            return new CxrRound.Image(f[0], f[1], int.Parse(f[2], CultureInfo.InvariantCulture), f[4], f[5] == "1", f[6] == "1", f[7] == "1", x.AsSpan(i * FeatureCount, FeatureCount).ToArray());
        }).ToList();
        if (patients is not int n) return images;
        // a subset of whole patients, drawn with a fixed seed: an eighth of them from the check set
        var keep = new HashSet<string>();
        foreach (var (inTest, want) in new[] { (true, n / 8), (false, n - n / 8) })
            foreach (var p in Shuffled(images.Where(i => i.InTest == inTest).Select(i => i.Patient).Distinct().OrderBy(p => p, StringComparer.Ordinal), 5).Take(want)) keep.Add(p);
        return images.Where(i => keep.Contains(i.Patient)).ToList();
    }

    private static List<T> Shuffled<T>(IEnumerable<T> items, int seed)
    {
        var list = items.ToList(); var r = new Random(seed);
        for (var i = list.Count - 1; i > 0; i--) { var j = r.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
        return list;
    }

    private sealed record EndUser(string Patient, List<CxrRound.Image> Train, CxrRound.Image? Held);

    public static JsonObject Run(List<CxrRound.Image> images, string head, AihCxrRoundTests.CxrTask task, int rounds, int[] measureAt, int draws, int localsSampled, Action<string> say)
    {
        var work = Path.Combine(Path.GetTempPath(), "aih-cxr-pp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try { return RunIn(work, images, head, task, rounds, measureAt, draws, localsSampled, say); }
        finally { try { Directory.Delete(work, true); } catch { } }
    }

    private static JsonObject RunIn(string work, List<CxrRound.Image> images, string head, AihCxrRoundTests.CxrTask task, int rounds, int[] measureAt, int draws, int localsSampled, Action<string> say)
    {
        var clock = Stopwatch.StartNew();
        string T() => $"{clock.Elapsed.TotalMinutes:F1} min";
        bool Y(CxrRound.Image i) => i.Finding(task);

        // THE SPLIT
        var check = images.Where(i => i.InTest).ToList();
        var others = Shuffled(images.Where(i => !i.InTest).GroupBy(i => i.Patient).OrderBy(g => g.Key, StringComparer.Ordinal), 22);
        var backEndCount = Math.Max(1, others.Count / 10);
        var backEnd = others.Take(backEndCount).SelectMany(g => g).ToList();

        // the frozen part ends with a fixed standardisation of each feature: mean and spread on the Back End's own images
        var mean = new double[FeatureCount]; var spread = new double[FeatureCount];
        foreach (var i in backEnd) for (var k = 0; k < FeatureCount; k++) mean[k] += i.X[k] / backEnd.Count;
        foreach (var i in backEnd) for (var k = 0; k < FeatureCount; k++) spread[k] += (i.X[k] - mean[k]) * (i.X[k] - mean[k]) / backEnd.Count;
        CxrRound.Image Std(CxrRound.Image i) => i with { X = i.X.Select((v, k) => (float)((v - mean[k]) / (Math.Sqrt(spread[k]) + 1e-3))).ToArray() };
        check = check.Select(Std).ToList(); backEnd = backEnd.Select(Std).ToList();
        // the Back End's own patients: 80% train its first model, 20% (fixed seed) are kept apart for the quality gate (a model is not judged on what it learned)
        var gatePatients = new HashSet<string>(Shuffled(backEnd.Select(i => i.Patient).Distinct().OrderBy(p => p, StringComparer.Ordinal), 23).Take(Math.Max(1, backEnd.Select(i => i.Patient).Distinct().Count() / 5)));
        var gateSet = backEnd.Where(i => gatePatients.Contains(i.Patient)).ToList();
        backEnd = backEnd.Where(i => !gatePatients.Contains(i.Patient)).ToList();
        var endUsers = others.Skip(backEndCount).Select(g =>
        {
            var l = g.OrderBy(i => i.Name, StringComparer.Ordinal).Select(Std).ToList();
            return l.Count >= 2 ? new EndUser(g.Key, l.Take(l.Count - 1).ToList(), l[^1]) : new EndUser(g.Key, l, null);
        }).ToList();
        var held = endUsers.Where(e => e.Held is not null).Select(e => e.Held!).ToList();
        var trainImages = endUsers.Sum(e => e.Train.Count);
        string Count(List<CxrRound.Image> l) => $"{l.Count} images of {l.Select(i => i.Patient).Distinct().Count()} patients, {l.Count(Y)} with the finding";
        var taskName = task == AihCxrRoundTests.CxrTask.Any ? "Finding (any of the 14) against No Finding" : "Effusion against the rest";
        say($"== Task: {taskName}  [{T()}]");
        say($"   check set (official test list): {Count(check)}");
        say($"   Back End's own: {Count(backEnd)} for its first model; {Count(gateSet)} kept apart for the quality gate");
        say($"   End Users (one per patient): {endUsers.Count}; their training images {trainImages}, {endUsers.Sum(e => e.Train.Count(Y))} with the finding; held-out {Count(held)}");

        // THE AIMS: the Back End's DWI and WEX (NNW V2.0), its Key and Payload
        var amds = new AmdStore(Path.Combine(Repository.Root, "AIMs", "AMDs")); amds.Scan();
        var nnw = new NnwProvider(amds);
        var dwiPorts = AimPortReader.Load(amds, NnwProvider.Dwi); var wexPorts = AimPortReader.Load(amds, NnwProvider.Wex);
        var locator = NnwJson.LocatorObject("wlc-backend", NnwJson.NctMethod(Bits, ValuesPerBit), "key:backend");
        var key = NnwJson.KeyObject("key-backend", [0.4142, 0.7320, 0.2360]);
        int[] shape = [FeatureCount, 1, 1];
        var dwi = nnw.Create(NnwProvider.Dwi, new Dictionary<string, string> { ["NetworkEpochs"] = "10", ["HeadEpochs"] = "300" }, null);
        var made = dwi.ProcessAsync(new Message { MessageId = "dwi-first", Ports = new Dictionary<string, string> {
            [dwiPorts.Input(NnwJson.Model)] = NnwJson.UntrainedModel("model-untrained", File.ReadAllBytes(head), 64).ToJsonString(),
            [dwiPorts.Input(NnwJson.Dataset)] = NnwJson.DatasetObject("back-end-own", shape, backEnd.Select(i => (i.X, Y(i)))).ToJsonString(),
            [dwiPorts.Input(NnwJson.Payload)] = NnwJson.PayloadObject("pld-backend", Payload).ToJsonString(), [dwiPorts.Input(NnwJson.Key)] = key.ToJsonString(),
            [dwiPorts.Input(NnwJson.Locator)] = locator.ToJsonString() } }).GetAwaiter().GetResult();
        if (made.IsError) throw new InvalidOperationException(made.Payload);
        var start = JsonNode.Parse(made.Payload)!.AsObject(); start["MLModelObjectID"] = "model-0";
        say($"   start model made by DWI on the Back End's {backEnd.Count} images  [{T()}]");

        (bool Found, double Strength, double Threshold) Wex(IAimProcessor wex, JsonObject m)
        {
            var r = wex.ProcessAsync(new Message { MessageId = "wex", Ports = new Dictionary<string, string> {
                [wexPorts.Input(NnwJson.Model)] = m.ToJsonString(), [wexPorts.Input(NnwJson.Key)] = key.ToJsonString(), [wexPorts.Input(NnwJson.Locator)] = locator.ToJsonString() } }).GetAwaiter().GetResult();
            if (r.IsError) return (false, 0, 0);
            var o = JsonNode.Parse(r.Payload)!.AsObject(); var d = (string?)o["DescrMetadata"] ?? "";
            return ((string?)o["PayloadData"] == Payload && d.StartsWith("mark present", StringComparison.Ordinal), Num(d, "strength"), Num(d, "threshold"));
        }

        var threads = Environment.ProcessorCount;
        FrontEndTrainer NewTrainer() => new(head, new FileSharedStorage(Path.Combine(work, "t-" + Guid.NewGuid().ToString("N")), "t", "t"), FeatureCount);
        var judge = NewTrainer();
        var tensors = OnnxFloats.Weights(FrontEndTrainer.NetworkOf(start)!);
        var netLength = OnnxFloats.Read(FrontEndTrainer.NetworkOf(start)!, tensors).Length;
        var headLength = FrontEndTrainer.Weights(start).Length;
        double AucOn(JsonObject m, List<CxrRound.Image> l) => CxrRound.Auc(judge.Probabilities(m, l.Select(i => i.X)), l.Select(Y).ToArray());
        double AccAtHalf(JsonObject m, List<CxrRound.Image> l) { var p = judge.Probabilities(m, l.Select(i => i.X)); return l.Select((i, k) => (p[k] >= 0.5) == Y(i)).Count(b => b) / (double)l.Count; }

        var sampled = new HashSet<string>(Shuffled(endUsers.Select(e => e.Patient), 77).Take(localsSampled));
        var locals = new ConcurrentBag<JsonObject>();
        var current = start;
        var snapshots = new List<(string Name, JsonObject Model)> { ("start model (Back End, made by DWI)", start) };
        var roundRows = new JsonArray();
        for (var round = 1; round <= rounds; round++)
        {
            var r0 = clock.Elapsed;
            var headSum = new double[headLength]; var netSum = new double[netLength]; double cases = 0; int found = 0, refused = 0; var moved = new ConcurrentBag<double>();
            var currentHead = FrontEndTrainer.Weights(current); var currentNet = OnnxFloats.Read(FrontEndTrainer.NetworkOf(current)!, tensors);
            var sync = new object();
            Parallel.ForEach(endUsers, new ParallelOptions { MaxDegreeOfParallelism = threads },
                () => (Trainer: NewTrainer(), Wex: nnw.Create(NnwProvider.Wex, new Dictionary<string, string>(), null), H: new double[headLength], N: new double[netLength], C: 0.0, F: 0, R: 0),
                (e, _, s) =>
                {
                    var m = s.Trainer.TrainOn(current, e.Train.Select(i => (i.X, Y(i) ? 1f : 0f)).ToList(), FrontEndNetworkEpochs, 0, networkRate: NetworkRate);
                    if (round == 1 && sampled.Contains(e.Patient)) locals.Add(m);
                    if (!Wex(s.Wex, m).Found) { s.R++; return s; }
                    s.F++;
                    var h = FrontEndTrainer.Weights(m); var n = OnnxFloats.Read(FrontEndTrainer.NetworkOf(m)!, tensors); double c = e.Train.Count;
                    double d2 = 0;
                    for (var i = 0; i < h.Length; i++) { s.H[i] += h[i] * c; d2 += (h[i] - currentHead[i]) * (double)(h[i] - currentHead[i]); }
                    for (var i = 0; i < n.Length; i++) { s.N[i] += n[i] * c; d2 += (n[i] - currentNet[i]) * (double)(n[i] - currentNet[i]); }
                    moved.Add(Math.Sqrt(d2)); s.C += c;
                    return s;
                },
                s => { lock (sync) { for (var i = 0; i < headLength; i++) headSum[i] += s.H[i]; for (var i = 0; i < netLength; i++) netSum[i] += s.N[i]; cases += s.C; found += s.F; refused += s.R; } });
            var avgHead = headSum.Select(v => (float)(v / cases)).ToArray(); var avgNet = netSum.Select(v => (float)(v / cases)).ToArray();
            var candidate = FrontEndTrainer.ModelObject($"model-{round}", avgHead, $"round {round}: the average of {found} End Users' models, weighted by their cases", (int)cases,
                                                        OnnxFloats.Write(FrontEndTrainer.NetworkOf(start)!, tensors, avgNet));
            var aucNew = AucOn(candidate, gateSet); var aucOld = AucOn(current, gateSet);
            var accepted = aucNew >= aucOld - GateTolerance;
            var accNew = AccAtHalf(candidate, check); var accOld = AccAtHalf(current, check);
            var accGate = accNew >= accOld - 0.05;
            if (accepted) current = candidate;
            var checkAuc = AucOn(current, check);
            var mv = moved.OrderBy(v => v).ToList();
            say($"   round {round,2}: {found} models with the mark, {refused} without; moved median {Median(mv):F3}; Back End's AUC {aucOld:F3} -> {aucNew:F3}: {(accepted ? "kept" : "REFUSED")}" +
                $" (accuracy-at-0.5 gate: {(accGate ? "would keep" : "would refuse")}, {accOld:P1} -> {accNew:P1}); check-set AUC of the current model {checkAuc:F3}  [{(clock.Elapsed - r0).TotalSeconds:F0} s, {T()}]");
            roundRows.Add(new JsonObject
            {
                ["Round"] = round, ["Marked"] = found, ["Unmarked"] = refused, ["Cases"] = (int)cases, ["MovedMedian"] = Math.Round(Median(mv), 4),
                ["BackEndAUCBefore"] = Math.Round(aucOld, 4), ["BackEndAUCCandidate"] = Math.Round(aucNew, 4), ["Kept"] = accepted,
                ["AccuracyGate"] = accGate ? "would keep" : "would refuse", ["CheckAccuracyBefore"] = Math.Round(accOld, 4), ["CheckAccuracyCandidate"] = Math.Round(accNew, 4),
                ["CheckAUCOfCurrent"] = Math.Round(checkAuc, 4), ["Seconds"] = Math.Round((clock.Elapsed - r0).TotalSeconds, 1)
            });
            if (measureAt.Contains(round)) snapshots.Add(($"federated NN Model after round {round}", current));
        }

        // the central model (upper bound): all the End Users' training images together, trained like an End User
        var central = judge.TrainOn(start, endUsers.SelectMany(e => e.Train).Select(i => (i.X, Y(i) ? 1f : 0f)).ToList(), FrontEndNetworkEpochs, 0, networkRate: NetworkRate);
        snapshots.Add(("central model (upper bound: all End Users' training images)", central));
        say($"   central model trained on {trainImages} images  [{T()}]");

        // the End Users' local models of round 1 (a sample): the distribution of their AUC on the check set
        var localAucs = locals.AsParallel().WithDegreeOfParallelism(threads).Select(m => { var j = NewTrainer(); return CxrRound.Auc(j.Probabilities(m, check.Select(i => i.X)), check.Select(Y).ToArray()); })
                              .Where(v => !double.IsNaN(v)).OrderBy(v => v).ToList();
        say($"   local models of round 1 ({localAucs.Count} End Users drawn at random): check-set AUC median {Median(localAucs):F3}, quartiles {Quantile(localAucs, 0.25):F3}-{Quantile(localAucs, 0.75):F3}, range {localAucs.FirstOrDefault():F3}-{localAucs.LastOrDefault():F3}");

        var rows = new JsonArray();
        var wexFinal = nnw.Create(NnwProvider.Wex, new Dictionary<string, string>(), null);
        say($"   {"Model",-62} {"AUC [95%]",-22} {"thr",6} {"Accuracy [95%]",-22} {"Sensitivity [95%]",-22} {"Specificity [95%]",-22} {"held-out AUC",-12} mark");
        foreach (var (name, model) in snapshots)
        {
            var heldP = judge.Probabilities(model, held.Select(i => i.X)); var heldY = held.Select(Y).ToArray();
            var threshold = CxrRound.Youden(heldP, heldY);
            var p = judge.Probabilities(model, check.Select(i => i.X)); var y = check.Select(Y).ToArray();
            var m = CxrRound.Measures(p, y, threshold);
            var ci = Bootstrap(check, p, y, threshold, draws);
            var mark = Wex(wexFinal, model);
            string F(double v, (double Lo, double Hi) c) => $"{v:F3} [{c.Lo:F3}-{c.Hi:F3}]";
            say($"   {name,-62} {F(m.Auc, ci[0]),-22} {threshold,6:F3} {F(m.Acc, ci[1]),-22} {F(m.Sens, ci[2]),-22} {F(m.Spec, ci[3]),-22} {CxrRound.Auc(heldP, heldY),-12:F3} {(mark.Found ? "found" : "NOT found")} ({mark.Strength:F2}/{mark.Threshold:F2})");
            JsonArray A(double v, (double Lo, double Hi) c) => new(Math.Round(v, 4), Math.Round(c.Lo, 4), Math.Round(c.Hi, 4));
            rows.Add(new JsonObject
            {
                ["Model"] = name, ["Threshold"] = Math.Round(threshold, 4), ["AUC"] = A(m.Auc, ci[0]), ["Accuracy"] = A(m.Acc, ci[1]), ["Sensitivity"] = A(m.Sens, ci[2]), ["Specificity"] = A(m.Spec, ci[3]),
                ["HeldOutAUC"] = Math.Round(CxrRound.Auc(heldP, heldY), 4),
                ["Watermark"] = new JsonObject { ["Found"] = mark.Found, ["Strength"] = Math.Round(mark.Strength, 3), ["Threshold"] = Math.Round(mark.Threshold, 3) }
            });
        }
        say($"   done  [{T()}]");
        return new JsonObject
        {
            ["Task"] = taskName,
            ["Groups"] = new JsonObject
            {
                ["CheckSet"] = new JsonObject { ["Images"] = check.Count, ["Patients"] = check.Select(i => i.Patient).Distinct().Count(), ["WithFinding"] = check.Count(Y) },
                ["BackEndOwn"] = new JsonObject { ["Images"] = backEnd.Count, ["Patients"] = backEnd.Select(i => i.Patient).Distinct().Count(), ["WithFinding"] = backEnd.Count(Y) },
                ["BackEndGate"] = new JsonObject { ["Images"] = gateSet.Count, ["Patients"] = gateSet.Select(i => i.Patient).Distinct().Count(), ["WithFinding"] = gateSet.Count(Y) },
                ["EndUsers"] = endUsers.Count, ["EndUserTrainingImages"] = trainImages, ["EndUserTrainingWithFinding"] = endUsers.Sum(e => e.Train.Count(Y)),
                ["HeldOutImages"] = held.Count, ["HeldOutWithFinding"] = held.Count(Y), ["AllImages"] = images.Count
            },
            ["Models"] = rows, ["Rounds"] = roundRows,
            ["LocalModelsRound1"] = new JsonObject { ["Sampled"] = localAucs.Count, ["AUCMedian"] = Math.Round(Median(localAucs), 4), ["AUCQ1"] = Math.Round(Quantile(localAucs, 0.25), 4), ["AUCQ3"] = Math.Round(Quantile(localAucs, 0.75), 4), ["AUCMin"] = Math.Round(localAucs.FirstOrDefault(), 4), ["AUCMax"] = Math.Round(localAucs.LastOrDefault(), 4) },
            ["Minutes"] = Math.Round(clock.Elapsed.TotalMinutes, 1),
            ["Settings"] = new JsonObject
            {
                ["Network"] = "TorchXRayVision DenseNet-121, densenet121-res224-chex (CheXpert only), frozen, 1024 features", ["Head"] = "gain, Gemm 1024->64, Relu; final layer 64+1",
                ["Standardisation"] = "each feature: mean and spread on the Back End's own images", ["StartModel"] = "NNW-DWI V2.0 AIM, NetworkEpochs 10, HeadEpochs 300, NCT 16 bits x 16 values",
                ["EndUserTraining"] = $"{FrontEndNetworkEpochs} passes, head and final layer together, network rate {NetworkRate}", ["Check"] = "NNW-WEX V2.0 AIM on every returned model; no mark: refused",
                ["Aggregation"] = "the models with the mark averaged, weighted by their training images", ["Gate"] = $"AUC on the Back End's gate images (20% of its own patients, not used to train): kept unless it falls by more than {GateTolerance}",
                ["AccuracyGate"] = "reported only: HFL's rule, accuracy at 0.5 on the check set not more than 5 points below the current model's",
                ["Threshold"] = "Youden's J on the pooled held-out images (the last image of each End User with two or more)", ["Intervals"] = $"95% percentile bootstrap over the patients of the check set, {draws} draws, seed 1",
                ["LocalModels"] = $"round 1, {localsSampled} End Users drawn at random (seed 77)"
            }
        };
    }

    private static double Num(string s, string word)
    {
        var i = s.IndexOf(word, StringComparison.Ordinal); if (i < 0) return double.NaN;
        var j = i + word.Length; while (j < s.Length && !(char.IsDigit(s[j]) || s[j] == '-')) j++;
        var k = j; while (k < s.Length && (char.IsDigit(s[k]) || s[k] == '.' || s[k] == '-')) k++;
        return double.TryParse(s[j..k], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
    }

    private static double Median(List<double> v) => Quantile(v, 0.5);
    private static double Quantile(List<double> v, double q) => v.Count == 0 ? double.NaN : v[(int)Math.Round(q * (v.Count - 1))];

    private static (double Lo, double Hi)[] Bootstrap(List<CxrRound.Image> check, double[] p, bool[] y, double t, int draws)
    {
        var byPatient = check.Select((img, i) => (img.Patient, i)).GroupBy(x => x.Patient).Select(g => g.Select(x => x.i).ToArray()).ToArray();
        var r = new Random(1);
        var values = new List<double>[4] { new(), new(), new(), new() };
        for (var d = 0; d < draws; d++)
        {
            var idx = new List<int>();
            for (var k = 0; k < byPatient.Length; k++) idx.AddRange(byPatient[r.Next(byPatient.Length)]);
            var m = CxrRound.Measures(idx.Select(i => p[i]).ToArray(), idx.Select(i => y[i]).ToArray(), t);
            var all = new[] { m.Auc, m.Acc, m.Sens, m.Spec };
            for (var k = 0; k < 4; k++) if (!double.IsNaN(all[k])) values[k].Add(all[k]);
        }
        return values.Select(v => { v.Sort(); return v.Count == 0 ? (double.NaN, double.NaN) : (v[(int)(0.025 * (v.Count - 1))], v[(int)(0.975 * (v.Count - 1))]); }).ToArray();
    }
}
