// NOTICE. The NoConstraint watermarking method and its code is made available here by Institut Mines-Telecom for demonstration purposes.
// Any other use, notably any commercial or industrial use of the software, shall be subject to specific and written agreement with Institut Mines Telecom.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.SharedStorage;
using AIF.Store;
using Mpai.Onnx;

namespace Mpai.Nnw.Watermarking;

// THE THREE AIMs OF MPAI-NNW V2.0 OF THIS IMPLEMENTATION, with the method NCT, No-Constraint Traceability (Institut Mines-Telecom; Nct.cs; MPAI-NNW TEC V1.0, Evaluations,
// section 2; the notice of NOTICE.txt applies):
//   During-Training Watermark Insertion (NNW-DWI-V2.0): trains the model with the Training Dataset; the mark is inserted in the network before it trains, and the weights
//     it set are kept at their values after every update - the network learns around the mark.
//   Post-Training Watermark Insertion (NNW-PWI-V2.0): inserts the mark in a trained model without training it.
//   Watermark Extraction (NNW-WEX-V2.0): retrieves the Payload; its DescrMetadata says whether the mark is present.
// A Locator (NNW-WLC), where given, must name NCT (NnwJson.NctMethod: the number of bits and of values a bit) and may name the tensors (Targets); the insertion AIMs given no
// Locator use NCT with its defaults; Extraction needs the Locator. The Key (NNW-KEY) is a sequence of reals from which the positions and the values come. The models and
// the datasets these AIMs read are described in Model.cs.

internal sealed record MethodChoice(string Name, int Bits, int ValuesPerBit, IReadOnlyCollection<string>? Targets);

internal static class Methods
{
    // The method a Locator names (no Locator: NCT with its defaults, for the number of bits of the Payload).
    public static MethodChoice Of(string? method, IReadOnlyCollection<string>? targets, int payloadBits)
    {
        if (method is not null && !NoConstraint.Names(method))
            throw new FormatException($"the Locator names the method '{method}', which this Implementation does not implement (it implements NCT).");
        var bits = NoConstraint.BitsOf(method) ?? payloadBits;
        var vpb = NoConstraint.ValuesPerBitOf(method) ?? NoConstraint.ValuesPerBit;
        return new MethodChoice(NoConstraint.MethodName(bits, vpb), bits, vpb, targets);
    }
}

// NCT DURING TRAINING: the mark inserted in the network before it trains; the weights it set kept at their values after every update.
internal sealed class NctMark(byte[] seed, bool[] payload, int valuesPerBit, IReadOnlyCollection<string>? targets) : ITrainingMark
{
    private readonly List<(int P, int I, float V)> _held = new();
    public int Held => _held.Count;

    public void Begin(Network net)
    {
        var before = net.Weights();
        var tensors = OnnxFloats.Weights(net.ToOnnx());
        var marked = OnnxFloats.Read(NoConstraint.InsertOnnx(net.ToOnnx(), seed, payload, targets, valuesPerBit), tensors);
        var parameters = net.Parameters().ToList();
        for (int p = 0, at = 0; p < parameters.Count; at += parameters[p].Values.Length, p++)
            for (var i = 0; i < parameters[p].Values.Length; i++)
                if (marked[at + i] != before[at + i]) _held.Add((p, i, marked[at + i]));
        if (_held.Count == 0) throw new InvalidOperationException("the mark changed no weight of the network.");
        AfterUpdate(net);
    }

    public void AfterUpdate(Network net)
    {
        var parameters = net.Parameters().ToList();
        foreach (var (p, i, v) in _held) parameters[p].Values[i] = v;
    }
}

public sealed class DwiAimProcessor : IAimProcessor
{
    private readonly string _model, _dataset, _payload, _key, _locator, _out;
    private readonly Trainer.Settings _settings;
    public string InstanceId { get; }

    public DwiAimProcessor(string instanceId, AimPortReader ports, IReadOnlyDictionary<string, string>? settings = null)
    {
        InstanceId = instanceId;
        _model = ports.InputOrDefault(NnwJson.Model, "UntrainedModel"); _dataset = ports.Input(NnwJson.Dataset); _payload = ports.Input(NnwJson.Payload); _key = ports.Input(NnwJson.Key);
        _locator = ports.InputOrDefault(NnwJson.Locator, "Locator"); _out = ports.Output(NnwJson.Model);
        _settings = Trainer.From(settings ?? new Dictionary<string, string>());
    }

    public Task<Message> ProcessAsync(Message message)
    {
        try
        {
            if (!message.Ports.TryGetValue(_model, out var mj) || string.IsNullOrWhiteSpace(mj))
                return Fail(message, "this Implementation trains the model it is given: the Untrained Model (its architecture and starting weights) is needed.");
            var start = NnwJson.Required(message, _model, "UntrainedModel");
            var (shape, cases) = Dataset.Read(NnwJson.Required(message, _dataset, "TrainingDataset"));
            var payload = NoConstraint.Bits(NnwJson.Required(message, _payload, "Payload")["PayloadData"]?.GetValue<string>() ?? "");
            var seed = NnwJson.Seed(NnwJson.Required(message, _key, "Key"));
            var targets = NnwJson.Targets(message, _locator, out var method);
            var choice = Methods.Of(method, targets, payload.Length);
            if (choice.Bits != payload.Length) return Fail(message, $"the Locator's method carries {choice.Bits} bits, the Payload has {payload.Length}.");

            var model = HspModel.From(start);
            var net = Trainer.NetworkOf(model.NetworkFile, shape);
            var head = model.Head.Length == net.FeatureCount + 1 ? (float[])model.Head.Clone() : new float[net.FeatureCount + 1];
            Trainer.Train(net, head, cases, _settings, new NctMark(seed, payload, choice.ValuesPerBit, targets));
            var trained = new HspModel { Head = head, Cases = cases.Count, NetworkFile = net.ToOnnx() };
            var from = start["MLModelObjectID"]?.GetValue<string>() ?? "model";
            var result = trained.Into(start, from + ">dwi", $"trained on {cases.Count} cases with the watermark inserted during training: {choice.Name}");
            return Task.FromResult(new Message { MessageId = message.MessageId, MessageType = "WatermarkedModel", DataType = NnwJson.Model, Payload = result.ToJsonString(), Ports = new() { [_out] = result.ToJsonString() } });
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException or NotSupportedException) { return Fail(message, e.Message); }
    }

    private Task<Message> Fail(Message m, string why) => Task.FromResult(Message.Error(m.MessageId, InstanceId, "NNW-DWI-V2.0: " + why));
}

public sealed class PwiAimProcessor : IAimProcessor
{
    private readonly string _model, _payload, _key, _locator, _out;
    public string InstanceId { get; }

    public PwiAimProcessor(string instanceId, AimPortReader ports)
    {
        InstanceId = instanceId;
        _model = ports.Input(NnwJson.Model); _payload = ports.Input(NnwJson.Payload); _key = ports.Input(NnwJson.Key);
        _locator = ports.InputOrDefault(NnwJson.Locator, "Locator"); _out = ports.Output(NnwJson.Model);
    }

    public Task<Message> ProcessAsync(Message message)
    {
        try
        {
            var model = NnwJson.Required(message, _model, "UnwatermarkedModel");
            var payload = NoConstraint.Bits(NnwJson.Required(message, _payload, "Payload")["PayloadData"]?.GetValue<string>() ?? "");
            var seed = NnwJson.Seed(NnwJson.Required(message, _key, "Key"));
            var targets = NnwJson.Targets(message, _locator, out var method);
            var choice = Methods.Of(method, targets, payload.Length);
            if (choice.Bits != payload.Length) return Fail(message, $"the Locator's method carries {choice.Bits} bits, the Payload has {payload.Length}.");

            var marked = model.DeepClone().AsObject();
            var items = (marked["MLModelData"] as JsonArray)?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
            var done = 0;
            foreach (var item in items)
            {
                if (item["Data"]?.GetValue<string>() is not { Length: > 0 } b64) continue;
                byte[] bytes; try { bytes = Convert.FromBase64String(b64); } catch (FormatException) { continue; }
                if (!OnnxFloats.IsOnnx(bytes)) continue;
                item["Data"] = Convert.ToBase64String(NoConstraint.InsertOnnx(bytes, seed, payload, targets, choice.ValuesPerBit));
                done++;
            }
            if (done == 0) return Fail(message, "the model carries no ONNX file with float tensors: a format this Implementation does not support.");
            marked["MLModelObjectID"] = (model["MLModelObjectID"]?.GetValue<string>() ?? "model") + "-wm";
            marked["DescrMetadata"] = $"{model["DescrMetadata"]?.GetValue<string>()} - watermarked: {choice.Name}".TrimStart(' ', '-');
            return Task.FromResult(new Message { MessageId = message.MessageId, MessageType = "WatermarkedModel", DataType = NnwJson.Model, Payload = marked.ToJsonString(), Ports = new() { [_out] = marked.ToJsonString() } });
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException or NotSupportedException) { return Fail(message, e.Message); }
    }

    private Task<Message> Fail(Message m, string why) => Task.FromResult(Message.Error(m.MessageId, InstanceId, "NNW-PWI-V2.0: " + why));
}

public sealed class WexAimProcessor : IAimProcessor
{
    private readonly string _model, _key, _locator, _out;
    public string InstanceId { get; }

    public WexAimProcessor(string instanceId, AimPortReader ports)
    {
        InstanceId = instanceId;
        _model = ports.Input(NnwJson.Model); _key = ports.Input(NnwJson.Key);
        _locator = ports.InputOrDefault(NnwJson.Locator, "Locator"); _out = ports.Output(NnwJson.Payload);
    }

    public Task<Message> ProcessAsync(Message message)
    {
        try
        {
            var model = NnwJson.Required(message, _model, "ModifiedModel");
            var seed = NnwJson.Seed(NnwJson.Required(message, _key, "Key"));
            var targets = NnwJson.Targets(message, _locator, out var method);
            if (method is null) return Fail(message, "no Locator: it names the method and so the number of bits of the Payload.");
            var choice = Methods.Of(method, targets, 0);
            var onnx = HspModel.OnnxFiles(model).FirstOrDefault() ?? throw new FormatException("the model carries no ONNX file with float tensors.");
            var found = NoConstraint.ExtractOnnx(onnx, seed, choice.Bits, targets, choice.ValuesPerBit);
            var result = new JsonObject
            {
                ["Header"] = NnwJson.Payload, ["PayloadID"] = "pld-" + Guid.NewGuid().ToString("N")[..12], ["PayloadTime"] = DateTime.UtcNow.ToString("o"),
                ["PayloadData"] = found.Payload,
                ["DescrMetadata"] = $"{(found.Present ? "mark present" : "mark NOT present")}: strength {found.Strength:F2}, threshold {found.Threshold:F2}, correlation {found.Correlation:F4} ({choice.Name})"
            };
            return Task.FromResult(new Message { MessageId = message.MessageId, MessageType = "RetrievedPayload", DataType = NnwJson.Payload, Payload = result.ToJsonString(), Ports = new() { [_out] = result.ToJsonString() } });
        }
        catch (Exception e) when (e is FormatException or InvalidOperationException or ArgumentException or NotSupportedException) { return Fail(message, e.Message); }
    }

    private Task<Message> Fail(Message m, string why) => Task.FromResult(Message.Error(m.MessageId, InstanceId, "NNW-WEX-V2.0: " + why));
}

// The Data these AIMs take and give, made and read (for the User Agents and the parties that send them Data).
public static class NnwJson
{
    public const string Model = "AIP-MLO-V1.0", Payload = "NNW-PLD-V2.0", Key = "NNW-KEY-V2.0", Locator = "NNW-WLC-V2.0", Dataset = "NNW-DST-V2.0";

    // The notice of Institut Mines-Telecom on NCT (NOTICE.txt): a program that uses these AIMs shows it.
    public const string NctNotice = NoConstraint.Notice;

    public static JsonObject Required(Message m, string port, string name) =>
        m.Ports.TryGetValue(port, out var json) && !string.IsNullOrWhiteSpace(json) && JsonNode.Parse(json) is JsonObject o ? o : throw new FormatException($"no {name} on its input Port.");

    public static byte[] Seed(JsonObject key)
    {
        var data = (key["KeyData"] as JsonArray)?.Select(n => n!.GetValue<double>()).ToArray() ?? throw new FormatException("the Key has no KeyData.");
        if (data.Length == 0) throw new FormatException("the Key is empty.");
        return NoConstraint.Seed(data);
    }

    // The tensors a Locator names (null: the method chooses), and the method it names (null: no Locator).
    public static IReadOnlyCollection<string>? Targets(Message m, string port, out string? method)
    {
        method = null;
        if (!m.Ports.TryGetValue(port, out var json) || string.IsNullOrWhiteSpace(json) || JsonNode.Parse(json) is not JsonObject locator) return null;
        method = locator["Method"]?.GetValue<string>();
        var names = (locator["Targets"] as JsonArray)?.Select(n => n!.GetValue<string>()).ToArray();
        return names is { Length: > 0 } ? names : null;
    }

    // A Key (NNW-KEY-V2.0) of `count` reals drawn by the system's random generator.
    public static JsonObject NewKey(string id, int count = 8)
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(count * 8);
        var reals = Enumerable.Range(0, count).Select(i => (double)BitConverter.ToUInt64(bytes, i * 8) / ulong.MaxValue).ToArray();
        return KeyObject(id, reals);
    }

    public static JsonObject KeyObject(string id, IEnumerable<double> reals) =>
        new() { ["Header"] = Key, ["KeyID"] = id, ["Precision"] = "Double", ["KeyData"] = new JsonArray(reals.Select(r => (JsonNode)r).ToArray()) };

    public static JsonObject PayloadObject(string id, string bits) => new() { ["Header"] = Payload, ["PayloadID"] = id, ["PayloadData"] = bits };

    // The name of the method, as a Locator gives it.
    public static string NctMethod(int bits, int valuesPerBit = NoConstraint.ValuesPerBit) => NoConstraint.MethodName(bits, valuesPerBit);

    public static JsonObject LocatorObject(string id, string method, string? keyReference = null, IEnumerable<string>? targets = null)
    {
        var o = new JsonObject { ["Header"] = Locator, ["LocatorID"] = id, ["Method"] = method };
        if (targets is not null) o["Targets"] = new JsonArray(targets.Select(t => (JsonNode)t).ToArray());
        if (keyReference is not null) o["KeyReference"] = keyReference;
        return o;
    }

    // A Training Dataset (NNW-DST-V2.0) of cases (the inputs as the network takes them, and a label), in this Implementation's format (Model.cs).
    public static JsonObject DatasetObject(string id, int[] shape, IEnumerable<(float[] X, bool Y)> cases) =>
        new() { ["Header"] = Dataset, ["DatasetID"] = id, ["DatasetData"] = Watermarking.Dataset.Write(shape, cases) };

    // An untrained model in this Implementation's format: the network file and a final layer of zeros.
    public static JsonObject UntrainedModel(string id, byte[] network, int featureCount) =>
        new HspModel { Head = new float[featureCount + 1], Cases = 0, NetworkFile = network }.Into(new JsonObject { ["Header"] = Model }, id, "untrained");
}

// Builds the three AIMs for a Controller.
public sealed class NnwProvider : IAimProvider
{
    public const string Dwi = "1NNW-DWI-V2.0-I01", Pwi = "1NNW-PWI-V2.0-I01", Wex = "1NNW-WEX-V2.0-I01";
    private readonly AmdStore _amds;
    public NnwProvider(AmdStore amds) => _amds = amds;

    public bool CanCreate(string aimName) => aimName is Dwi or Pwi or Wex;
    public string? ImplementationOf(string aimName) => CanCreate(aimName) ? typeof(NnwProvider).Assembly.Location : null;

    public IAimProcessor Create(string aimName, IReadOnlyDictionary<string, string> settings, ISharedStorage? storage) => aimName switch
    {
        Dwi => new DwiAimProcessor(aimName, AimPortReader.Load(_amds, aimName), settings),
        Pwi => new PwiAimProcessor(aimName, AimPortReader.Load(_amds, aimName)),
        Wex => new WexAimProcessor(aimName, AimPortReader.Load(_amds, aimName)),
        _ => throw new InvalidOperationException($"{aimName} is not an AIM of this provider.")
    };
}
