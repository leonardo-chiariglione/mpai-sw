using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using AIF.Controller;

namespace Mpai.Aih.Hfl;

// AIH-HFL-V1.1 - Health Federated Learning: "requests trained NN Models and produces a new NN Model". A round is one Federated Learn holding a Request for each Front End
// (to which Front End, which model): each Front End is sent its own Request, and the round has as many answers to wait for; a Federated Learn response (the model the Front End trained, as an ML Model Object) comes in and
// is kept; when every answer asked for is in, the models are averaged (federated averaging) and the new NN Model leaves (AIP-MLO-V1.0).
//
// The weights of a model are read as 32-bit floats, little endian, in the first Data item of the ML Model Object (base64): the specification does not say what a
// model holds, and an average needs that. Where the model also carries its network (an ONNX file, in the third Data item: see FrontEndTrainer), the weights of the
// network are averaged too, and the new model carries the network with the averaged weights. A model of another length than the first answer is refused.
public sealed class HflAimProcessor : IAimProcessor
{
    private readonly string _fdlIn, _fdlOut, _modelOut;
    private int _expected, _round;
    private string _pre = "";
    private readonly List<float[]> _answers = new();
    private readonly List<int> _cases = new();                                      // the number of cases each accepted answer learned from
    private int _refused;                                                            // updates of this round that were refused
    private readonly List<RoundRefusal> _refusals = new();

    // The checks of a round (step 2): null means none (the first slice's behaviour). The model the round started from; what was refused and why.
    public RoundPolicy? Policy { get; set; }
    public float[]? Current { get; private set; }                                   // all the weights: the final layer, then the network's (Vector)
    public JsonObject? CurrentModel { get; private set; }
    private byte[]? _network;                                                        // the network file the averaged weights are written into
    public IReadOnlyList<RoundRefusal> Refusals => _refusals;
    public int Round => _round;
    private readonly List<RoundOutcome> _outcomes = new();
    public IReadOnlyList<RoundOutcome> Outcomes => _outcomes;                          // what each round made (or did not make), without who sent what

    // HOW THE NEW NN MODEL IS MADE: an Implementer's choice (the specification says only "produces a new NN Model"); see FlMethods. Average waits for the answers of a
    // round (the first slice's behaviour); the other methods make a new model from the current one and EACH trained AIM as it arrives, as AIH-HSP works.
    public FlMethod Method { get; set; } = FlMethod.Average;
    public double Alpha { get; set; } = 0.5;                                         // FedAsync: the share of a fresh AIM
    public FlLabelBank? Bank { get; set; }                                           // LabelBank: the Back End's inputs and their labels
    public string LastNote { get; private set; } = "";
    private readonly List<string> _made = new();                                     // the Back End's models, oldest first (for the staleness of an AIM)

    private readonly Dictionary<string, float[]> _weightsOf = new();               // AddLearning: every model the Back End made, to find the one an AIM started from

    private static string StartedFrom(JsonObject? post)
    {
        var id = post?["MLModelObjectID"]?.GetValue<string>() ?? "";
        return id.Contains('>') ? id[..id.IndexOf('>')] : id;
    }

    private int Staleness(JsonObject? post)
    {
        var at = _made.LastIndexOf(StartedFrom(post));
        return at < 0 ? 0 : _made.Count - 1 - at;
    }

    // ONE TRAINED AIM HAS ARRIVED: the check of the update, the new model by the method, the check on the fixed set, and the model out (or the old one stays).
    private void Arrived(JsonObject? post, float[] weights, Dictionary<string, string> outputs)
    {
        var cases = Cases(post); var stale = Staleness(post);
        var why = Policy?.CheckUpdate(weights, Current);
        if (why is not null || Current is null || CurrentModel is null)
        {
            _refusals.Add(new RoundRefusal(_round + 1, "update", why ?? "the Back End has no model to start from."));
            _outcomes.Add(new RoundOutcome(_round + 1, null, _pre, 0, cases, false)); LastNote = why ?? ""; return;
        }
        float[] next;
        switch (Method)
        {
            case FlMethod.Replace: next = weights; LastNote = "the trained AIM taken as it is"; break;
            case FlMethod.FedAsync:
            {
                var share = FlMethods.MixShare(Alpha, stale);
                next = FlMethods.Mix(Current, weights, share); LastNote = $"mixed in with a share of {share:P1} (trained from a model {stale} behind)"; break;
            }
            case FlMethod.LabelBank:
            {
                var bank = Bank ?? throw new InvalidOperationException("LabelBank needs the Back End's label bank.");
                next = Vector(bank.Learn(CurrentModel, post!, cases)); LastNote = bank.Note; break;
            }
            case FlMethod.AddLearning:
            {
                if (!_weightsOf.TryGetValue(StartedFrom(post), out var from))
                {
                    const string unknown = "the Back End's model this AIM started from is not known: its learning cannot be measured.";
                    _refusals.Add(new RoundRefusal(_round + 1, "update", unknown));
                    _outcomes.Add(new RoundOutcome(_round + 1, null, _pre, 0, cases, false)); LastNote = unknown; return;
                }
                var step = FlMethods.MixShare(Alpha, stale);
                next = FlMethods.AddLearning(Current, weights, from, step); LastNote = $"its learning added with a step of {step:P1} (started from a model {stale} behind)"; break;
            }
            default: throw new InvalidOperationException("not a per-arrival method: " + Method);
        }
        var candidate = ModelOf("candidate", next, cases);
        var gate = Policy?.CheckResult(CurrentModel, candidate);
        if (gate is not null)
        {
            _refusals.Add(new RoundRefusal(_round + 1, "result", gate));
            _outcomes.Add(new RoundOutcome(_round + 1, null, _pre, 1, cases, false)); LastNote += "; REFUSED: " + gate; return;
        }
        var model = ModelOf($"hbe-model-{++_round}", next, cases);
        model["DescrMetadata"] = $"{FlMethods.Describe(Method)}: {LastNote}" + (_network is null ? "" : "; the final layer and the network");
        outputs[_modelOut] = model.ToJsonString();
        Current = next; CurrentModel = model; _made.Add(model["MLModelObjectID"]!.GetValue<string>()); _weightsOf[_made[^1]] = next;
        _outcomes.Add(new RoundOutcome(_round, model["MLModelObjectID"]!.GetValue<string>(), _pre, 1, cases, true));
    }

    // Sets the model the next round starts from (the Back End's current NN Model).
    public void StartFrom(JsonObject model) { Current = Vector(model); CurrentModel = model; _network = FrontEndTrainer.NetworkOf(model) ?? _network; if (model["MLModelObjectID"]?.GetValue<string>() is string id) { _made.Add(id); _weightsOf[id] = Current; } }

    public string InstanceId { get; }

    public HflAimProcessor(string instanceId, AimPortReader ports)
    {
        InstanceId = instanceId;
        _fdlIn    = ports.Input(AihJson.FederatedLearn);
        _fdlOut   = ports.Output(AihJson.FederatedLearn);
        _modelOut = ports.Output("AIP-MLO-V1.0");
    }

    public Task<Message> ProcessAsync(Message message)
    {
        if (!message.Ports.TryGetValue(_fdlIn, out var json) || string.IsNullOrWhiteSpace(json))
            return Task.FromResult(Message.Error(message.MessageId, InstanceId, $"AIH-HFL-V1.1: no Federated Learn on input port '{_fdlIn}'."));
        try
        {
            var outputs = new Dictionary<string, string>();
            var more = new Dictionary<string, List<string>>();
            var fdl = AihJson.Parse(json, "Federated Learn");
            var item = (fdl["FederatedLearnData"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault() ?? throw new FormatException("the Federated Learn holds nothing.");

            if (item["Request"] is JsonObject)
            {
                // A ROUND: one Federated Learn with a Request for each Front End (FederatedLearnData is an array). The round has as many answers to wait for as Requests, known
                // before any request goes out; each Front End is sent a Federated Learn with its own Request only (it learns nothing of the others), all from this one input.
                var requests = (fdl["FederatedLearnData"] as JsonArray)!.OfType<JsonObject>().Select(o => o["Request"] as JsonObject).OfType<JsonObject>().ToList();
                _pre = requests[0]["PreLearnNNModelID"]?.GetValue<string>() ?? _pre;
                _expected += requests.Count;
                var each = requests.Select(r =>
                {
                    var one = (JsonObject)fdl.DeepClone();                           // the round's Federated Learn, each addressed by its own Request (the Front End it names)
                    one["FederatedLearnData"] = new JsonArray(new JsonObject { ["Request"] = r.DeepClone() });
                    return one.ToJsonString();
                }).ToList();
                outputs[_fdlOut] = each[0];                                          // to the Front Ends
                if (each.Count > 1) more[_fdlOut] = each.Skip(1).ToList();
            }
            else if (item["Response"] is JsonObject response)
            {
                var post = response["PostLearnNNModel"] as JsonObject;
                var weights = Vector(post);
                if (_network is null && post is not null) _network = FrontEndTrainer.NetworkOf(post);
                if (Method != FlMethod.Average) { Arrived(post, weights, outputs); return Task.FromResult(new Message { MessageId = message.MessageId, MessageType = "FederatedLearn", DataType = AihJson.FederatedLearn, Payload = outputs.Values.FirstOrDefault() ?? "", Ports = outputs, More = more }); }
                var why = Policy is null ? null : Policy.CheckUpdate(weights, Current ?? (_answers.Count > 0 ? _answers[0] : null));
                if (Policy is null && _answers.Count > 0 && _answers[0].Length != weights.Length) throw new FormatException("a model of another size than the first answer.");
                if (why is not null) { _refused++; _refusals.Add(new RoundRefusal(_round + 1, "update", why)); }
                else { _answers.Add(weights); _cases.Add(Cases(response["PostLearnNNModel"] as JsonObject)); }
                if (_expected > 0 && _answers.Count + _refused >= _expected)
                {
                    var taken = _answers.Count;
                    float[]? mean = null;
                    if (taken > 0)
                    {
                        mean = FlMethods.Average(_answers, _cases);
                        var gate = Policy is null || CurrentModel is null ? null : Policy.CheckResult(CurrentModel, ModelOf("evaluation", mean, _cases.Sum()));
                        if (gate is not null) { _refusals.Add(new RoundRefusal(_round + 1, "result", gate)); mean = null; }
                    }
                    else _refusals.Add(new RoundRefusal(_round + 1, "result", "no update of the round was acceptable."));
                    if (mean is not null)
                    {
                        var model = ModelOf($"{_pre}-federated-{++_round}", mean, _cases.Sum());
                        model["DescrMetadata"] = $"Federated averaging of {taken} models, weighted by the number of cases they learned from ({_cases.Sum()})"
                                                 + (_network is null ? "" : "; the final layer and the network");
                        outputs[_modelOut] = model.ToJsonString();
                        Current = mean; CurrentModel = model;
                        _outcomes.Add(new RoundOutcome(_round, model["MLModelObjectID"]!.GetValue<string>(), _pre, taken, _cases.Sum(), true));
                    }
                    else { _round++; _outcomes.Add(new RoundOutcome(_round, null, _pre, taken, _cases.Sum(), false)); }   // the round is over; the old model stays
                    _answers.Clear(); _cases.Clear(); _expected = 0; _refused = 0;
                }
            }
            return Task.FromResult(new Message { MessageId = message.MessageId, MessageType = "FederatedLearn", DataType = AihJson.FederatedLearn, Payload = outputs.Values.FirstOrDefault() ?? "", Ports = outputs, More = more });
        }
        catch (Exception ex) { return Task.FromResult(Message.Error(message.MessageId, InstanceId, "AIH-HFL-V1.1: " + ex.Message)); }
    }

    // THE NUMBER OF CASES a model learned from. The Federated Learn schema (AIH-FDL-V1.1) and the ML Model Object (AIP-MLO-V1.0) have no field for it; the schemas allow
    // more than one Data item in MLModelData, so the SECOND Data item carries it, as decimal text. A model without it counts as one case (a plain average).
    public static int Cases(JsonObject? model)
    {
        var items = (model?["MLModelData"] as JsonArray)?.OfType<JsonObject>().Select(o => o["Data"]?.GetValue<string>()).Where(d => !string.IsNullOrEmpty(d)).ToList() ?? [];
        if (items.Count < 2) return 1;
        if (!int.TryParse(items[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) || n < 1) throw new FormatException("the number of cases of the model (second Data item) is not a positive whole number.");
        return n;
    }

    // The ML Model Object of a vector of weights: the final layer in the first Data item and, where the round has a network, the number of cases and the network with
    // its weights replaced (the rest of the vector, in the order of the file).
    private JsonObject ModelOf(string id, float[] vector, int cases)
    {
        if (_network is null) return new JsonObject { ["Header"] = "AIP-MLO-V1.0", ["MLModelObjectID"] = id, ["MLModelData"] = new JsonArray(new JsonObject { ["Data"] = Convert.ToBase64String(vector.SelectMany(BitConverter.GetBytes).ToArray()) }) };
        var tensors = Mpai.Onnx.OnnxFloats.Weights(_network); var n = tensors.Sum(t => t.Count);
        var head = vector[..^n]; var net = Mpai.Onnx.OnnxFloats.Write(_network, tensors, vector[^n..]);
        return FrontEndTrainer.ModelObject(id, head, "", cases, net);
    }

    // All the weights of a model: those of the final layer (first Data item), then those of its network where it carries one (third Data item, in the order of the file).
    public static float[] Vector(JsonObject? model)
    {
        var head = Weights(model);
        var net = model is null ? null : FrontEndTrainer.NetworkOf(model);
        if (net is null) return head;
        var tensors = Mpai.Onnx.OnnxFloats.Weights(net);
        return head.Concat(Mpai.Onnx.OnnxFloats.Read(net, tensors)).ToArray();
    }

    public static float[] Weights(JsonObject? model)
    {
        var data = (model?["MLModelData"] as JsonArray)?.OfType<JsonObject>().Select(o => o["Data"]?.GetValue<string>()).FirstOrDefault(d => !string.IsNullOrEmpty(d))
                   ?? throw new FormatException("the model carries no data.");
        var bytes = Convert.FromBase64String(data);
        if (bytes.Length % 4 != 0) throw new FormatException("the model is not 32-bit floats.");
        var weights = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, weights, 0, bytes.Length);
        return weights;
    }
}

// WHAT A ROUND MADE: its number, the NN Model it made (null if the result was refused), the model it started from, how many updates and cases it took.
public sealed record RoundOutcome(int Round, string? ModelId, string StartedFrom, int Updates, int Cases, bool Accepted);

// WHAT A ROUND REFUSED: the round (1, 2, ...), what was refused ("update" or "result") and why.
public sealed record RoundRefusal(int Round, string What, string Reason);

// THE CHECKS OF A ROUND (federated learning, step 2). Before an update is averaged: it has the size of the model, its values are finite, and it has not moved further than
// MaxDistance (Euclidean) from the model the round started from. After averaging: the new model is evaluated (Evaluate: accuracy and log loss on a FIXED set) and
// replaces the old one only if the accuracy has not fallen by more than MaxAccuracyDrop and the loss has not risen by more than MaxLossRise. The sizes and distances are
// of all the weights (Vector): the final layer and, where the models carry it, the network.
public sealed class RoundPolicy
{
    public int? WeightCount { get; init; }
    public double? MaxDistance { get; init; }
    public Func<JsonObject, (double Accuracy, double LogLoss)>? Evaluate { get; init; }
    public double MaxAccuracyDrop { get; init; } = 0.05;
    public double MaxLossRise { get; init; } = 0.10;

    public string? CheckUpdate(float[] update, float[]? start)
    {
        var size = WeightCount ?? start?.Length;
        if (size is not null && update.Length != size) return $"the update holds {update.Length} values, the model {size}.";
        if (update.Any(v => !float.IsFinite(v))) return "the update holds values that are not finite.";
        if (MaxDistance is double limit && start is not null)
        {
            var d = Math.Sqrt(update.Zip(start, (a, b) => (double)(a - b) * (a - b)).Sum());
            if (d > limit) return $"the update moved {d:F3} from the model it started from (limit {limit:F3}).";
        }
        return null;
    }

    public string? CheckResult(JsonObject old, JsonObject result)
    {
        if (Evaluate is null) return null;
        var before = Evaluate(old); var after = Evaluate(result);
        if (after.Accuracy < before.Accuracy - MaxAccuracyDrop) return $"the new model's accuracy {after.Accuracy:P1} is more than {MaxAccuracyDrop:P1} below the old model's {before.Accuracy:P1}.";
        if (after.LogLoss > before.LogLoss + MaxLossRise) return $"the new model's loss {after.LogLoss:F3} is more than {MaxLossRise:F3} above the old model's {before.LogLoss:F3}.";
        return null;
    }
}
