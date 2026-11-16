using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

using Mpai.Onnx;

namespace Mpai.Nnw.Watermarking;

// THE MODELS AND THE DATASETS THESE AIMs UNDERSTAND (the Implementer's formats: MPAI-NNW leaves them open).
//
// A model (AIP-MLO-V1.0) whose MLModelData holds, in this order: the weights of a final layer (base64 of float32: one weight a feature and a bias, logistic), the number of
// cases it learned from (decimal text), and the network (base64 of an ONNX file: a chain that Mpai.Onnx.Network trains). It is the model of the AIH-HSP reference software.
// A model whose MLModelData holds only ONNX files is enough for Post-Training Insertion and Extraction, which read the weights only.
//
// A Training Dataset (NNW-DST-V2.0): DatasetData is base64 of UTF-8 JSON: { "Shape": [channels, height, width], "Cases": [ { "X": base64 of float32, "Y": 0 or 1 } ] },
// the inputs as the network takes them.
internal sealed class HspModel
{
    public float[] Head = [];
    public int? Cases;
    public byte[] NetworkFile = [];

    public static bool IsHsp(JsonObject model) => Items(model).Count >= 3 && Items(model)[2] is { } n && OnnxFloats.IsOnnx(Decode(n) ?? []);

    public static HspModel From(JsonObject model)
    {
        var items = Items(model);
        if (items.Count < 3) throw new FormatException("the model is not of a format this Implementation trains: a final layer, the number of cases and an ONNX network.");
        var head = Decode(items[0]) ?? throw new FormatException("the final layer of the model is not base64.");
        var net = Decode(items[2]) ?? throw new FormatException("the network of the model is not base64.");
        if (!OnnxFloats.IsOnnx(net)) throw new FormatException("the third item of the model is not an ONNX file.");
        var w = new float[head.Length / 4]; Buffer.BlockCopy(head, 0, w, 0, w.Length * 4);
        return new HspModel { Head = w, Cases = int.TryParse(items[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null, NetworkFile = net };
    }

    // The model as an ML Model Object: the given one with its data replaced (ID and description given).
    public JsonObject Into(JsonObject original, string id, string description)
    {
        var m = original.DeepClone().AsObject();
        m["MLModelObjectID"] = id;
        m["MLModelData"] = new JsonArray(
            new JsonObject { ["Data"] = Convert.ToBase64String(Head.SelectMany(BitConverter.GetBytes).ToArray()) },
            new JsonObject { ["Data"] = (Cases ?? 0).ToString(CultureInfo.InvariantCulture) },
            new JsonObject { ["Data"] = Convert.ToBase64String(NetworkFile) });
        m["DescrMetadata"] = description;
        return m;
    }

    private static List<string?> Items(JsonObject model) =>
        (model["MLModelData"] as JsonArray)?.OfType<JsonObject>().Select(o => o["Data"]?.GetValue<string>()).ToList() ?? [];

    private static byte[]? Decode(string? s) { if (string.IsNullOrEmpty(s)) return null; try { return Convert.FromBase64String(s); } catch (FormatException) { return null; } }

    // The ONNX files of any model (for the methods that read weights only).
    public static List<byte[]> OnnxFiles(JsonObject model) =>
        Items(model).Select(Decode).Where(b => b is not null && OnnxFloats.IsOnnx(b)).Select(b => b!).ToList();
}

internal sealed record TrainingCase(float[] X, float Y);

internal static class Dataset
{
    public static (int[] Shape, List<TrainingCase> Cases) Read(JsonObject dataset)
    {
        var b64 = dataset["DatasetData"]?.GetValue<string>() ?? throw new FormatException("the Training Dataset has no DatasetData.");
        JsonObject json;
        try { json = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b64))) as JsonObject ?? throw new FormatException("not a JSON object"); }
        catch (Exception e) when (e is FormatException or System.Text.Json.JsonException) { throw new FormatException("the Training Dataset is not of the format this Implementation reads: " + e.Message); }
        var shape = (json["Shape"] as JsonArray)?.Select(n => n!.GetValue<int>()).ToArray() ?? throw new FormatException("the Training Dataset has no Shape.");
        if (shape.Length != 3 || shape.Any(v => v <= 0)) throw new FormatException("the Shape of the Training Dataset is not channels, height, width.");
        var cases = new List<TrainingCase>();
        foreach (var c in json["Cases"] as JsonArray ?? [])
        {
            var bytes = Convert.FromBase64String(c!["X"]!.GetValue<string>());
            var x = new float[bytes.Length / 4]; Buffer.BlockCopy(bytes, 0, x, 0, x.Length * 4);
            if (x.Length != shape[0] * shape[1] * shape[2]) throw new FormatException("a case of the Training Dataset does not have the size of its Shape.");
            cases.Add(new TrainingCase(x, c["Y"]!.GetValue<float>()));
        }
        if (cases.Count == 0) throw new FormatException("the Training Dataset has no case.");
        return (shape, cases);
    }

    public static string Write(int[] shape, IEnumerable<(float[] X, bool Y)> cases)
    {
        var json = new JsonObject
        {
            ["Shape"] = new JsonArray(shape.Select(v => (JsonNode)v).ToArray()),
            ["Cases"] = new JsonArray(cases.Select(c => (JsonNode)new JsonObject { ["X"] = Convert.ToBase64String(c.X.SelectMany(BitConverter.GetBytes).ToArray()), ["Y"] = c.Y ? 1 : 0 }).ToArray())
        };
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json.ToJsonString()));
    }
}

// WHAT A METHOD DOES WHILE THE MODEL TRAINS (During-Training Insertion): before the training, after the gradients of each batch, after each update, and the extra cases
// it trains with.
internal interface ITrainingMark
{
    void Begin(Network net);
    IEnumerable<TrainingCase> ExtraCases(int epoch) => [];
    void AddGradients(Network net) { }
    void AfterUpdate(Network net) { }
    bool Done(Network net, float[] head) => true;     // the mark is in: no more epochs are needed for it
}

// The training of the network and the final layer together (Adam on the logistic loss, batches of 10), then the final layer alone; the same as the AIH-HSP trainer.
internal static class Trainer
{
    public sealed record Settings(int NetworkEpochs, int HeadEpochs, float NetworkRate, float HeadRate, int MaxExtraEpochs);

    public static Settings From(IReadOnlyDictionary<string, string> s)
    {
        int I(string k, int d) => s.TryGetValue(k, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : d;
        float F(string k, float d) => s.TryGetValue(k, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : d;
        return new Settings(I("NetworkEpochs", 5), I("HeadEpochs", 300), F("NetworkRate", 0.0002f), F("HeadRate", 0.01f), I("MaxExtraEpochs", 0));
    }

    public static Network NetworkOf(byte[] file, int[] shape) => new(file, shape[0], shape[1], shape[2]);

    public static void Train(Network net, float[] w, List<TrainingCase> cases, Settings s, ITrainingMark mark)
    {
        mark.Begin(net);
        var parameters = net.Parameters().ToList();
        var m = parameters.Select(p => new double[p.Values.Length]).ToList(); var v = parameters.Select(p => new double[p.Values.Length]).ToList();
        var hm = new double[w.Length]; var hv = new double[w.Length]; var hg = new double[w.Length];
        const int batch = 10; var t = 0; var f = w.Length - 1;
        for (var e = 0; e < s.NetworkEpochs + s.MaxExtraEpochs; e++)
        {
            if (e >= s.NetworkEpochs && mark.Done(net, w)) break;
            var epochCases = cases.Concat(mark.ExtraCases(e)).ToList();
            Shuffle(epochCases, e);
            for (var at = 0; at < epochCases.Count; at += batch)
            {
                net.ClearGradients(); Array.Clear(hg);
                var n = Math.Min(batch, epochCases.Count - at);
                for (var c = at; c < at + n; c++)
                {
                    var acts = net.Run(epochCases[c].X); var x = acts[^1];
                    var err = (float)((Sigmoid(Dot(w, x)) - epochCases[c].Y) / n);
                    var dx = new float[f];
                    for (var i = 0; i < f; i++) { hg[i] += err * x[i]; dx[i] = err * w[i]; }
                    hg[f] += err;
                    net.Backward(acts, dx);
                }
                mark.AddGradients(net);
                t++;
                double c1 = 1 - Math.Pow(0.9, t), c2 = 1 - Math.Pow(0.999, t);
                for (var i = 0; i <= f; i++)
                {
                    var g = hg[i] + (i < f ? 1e-3 * w[i] : 0);
                    hm[i] = 0.9 * hm[i] + 0.1 * g; hv[i] = 0.999 * hv[i] + 0.001 * g * g;
                    w[i] -= (float)(s.HeadRate * (hm[i] / c1) / (Math.Sqrt(hv[i] / c2) + 1e-8));
                }
                for (var p = 0; p < parameters.Count; p++)
                {
                    var (values, grads) = parameters[p]; var pm = m[p]; var pv = v[p];
                    for (var i = 0; i < values.Length; i++)
                    {
                        double g = grads[i];
                        pm[i] = 0.9 * pm[i] + 0.1 * g; pv[i] = 0.999 * pv[i] + 0.001 * g * g;
                        values[i] -= (float)(s.NetworkRate * (pm[i] / c1) / (Math.Sqrt(pv[i] / c2) + 1e-8));
                    }
                }
                mark.AfterUpdate(net);
            }
        }
        // the final layer alone, on the features of the trained network (with the extra cases of the method, so that it keeps what they taught)
        var data = cases.Concat(mark.ExtraCases(int.MaxValue)).Select(c => (net.Features(c.X), c.Y)).ToList();
        FitHead(w, data, s.HeadEpochs, s.HeadRate);
    }

    public static double Probability(Network net, float[] w, float[] x) => Sigmoid(Dot(w, net.Features(x)));

    private static void FitHead(float[] w, List<(float[] X, float Y)> data, int epochs, float lr)
    {
        var m = new double[w.Length]; var v = new double[w.Length]; var g = new double[w.Length];
        for (var t = 1; t <= epochs; t++)
        {
            Array.Clear(g);
            foreach (var (x, y) in data)
            {
                var err = Sigmoid(Dot(w, x)) - y;
                for (var i = 0; i < x.Length; i++) g[i] += err * x[i];
                g[^1] += err;
            }
            for (var i = 0; i < w.Length; i++)
            {
                g[i] = g[i] / data.Count + (i < w.Length - 1 ? 1e-3 * w[i] : 0);
                m[i] = 0.9 * m[i] + 0.1 * g[i]; v[i] = 0.999 * v[i] + 0.001 * g[i] * g[i];
                w[i] -= (float)(lr * (m[i] / (1 - Math.Pow(0.9, t))) / (Math.Sqrt(v[i] / (1 - Math.Pow(0.999, t))) + 1e-8));
            }
        }
    }

    // A fixed order for each epoch, so that the same inputs give the same model.
    private static void Shuffle<T>(List<T> list, int epoch)
    {
        var rng = new Random(1000 + epoch);
        for (var i = list.Count - 1; i > 0; i--) { var j = rng.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
    }

    public static double Dot(float[] w, float[] x) { double s = w[^1]; for (var i = 0; i < x.Length; i++) s += w[i] * x[i]; return s; }
    public static double Sigmoid(double z) => 1.0 / (1.0 + Math.Exp(-z));
}
