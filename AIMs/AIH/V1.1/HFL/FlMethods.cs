using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Mpai.Aih.Hfl;

// HOW HEALTH FEDERATED LEARNING MAKES ITS NEW NN MODEL - the Implementer's choices of this implementation of AIH-HFL-V1.1. In AIH-HSP a Federated Learn Response carries
// ONE trained AIM (the PostLearnNNModel, trained by one Health Front End on its AIH Data): the Back End makes its new model from its current one and that one AIM, each
// time one arrives (author, 2026-11-16). The AIMs that arrive are, over time, the elements of a training set (author: "FL as starting from a Model and each Model
// received from an End User as an element of a training set").
//   Average    the models of a round averaged, weighted by the number of cases (the first slice's behaviour, kept for its tests: NOT how HSP works)
//   Replace    the arrived AIM becomes the Back End's model (the simplest per-arrival rule, a yardstick)
//   FedAsync   new = (1 - a) current + a arrived, a smaller when the AIM was trained from an older model of the Back End (Xie et al. 2019)
//   LabelBank  incremental distillation: the Back End holds its own inputs (never an End User's data) and a label for each; an arrived AIM's answers move the labels by
//              its share of all the cases received so far; the Back End's model is then trained on the labels. The bank is the training set made of every AIM received.
public enum FlMethod { Average, Replace, FedAsync, LabelBank }

public static class FlMethods
{
    public static string Describe(FlMethod m) => m switch
    {
        FlMethod.Average => "Federated averaging", FlMethod.Replace => "The trained AIM taken as it is",
        FlMethod.FedAsync => "Asynchronous mixing of the trained AIM into the Back End's model", FlMethod.LabelBank => "Incremental distillation through the Back End's label bank",
        _ => m.ToString()
    };

    public static float[] Average(IReadOnlyList<float[]> models, IReadOnlyList<int> cases)
    {
        var mean = new float[models[0].Length]; var total = (double)cases.Sum();
        for (var a = 0; a < models.Count; a++) { var k = cases[a] / total; for (var i = 0; i < mean.Length; i++) mean[i] += (float)(models[a][i] * k); }
        return mean;
    }

    // staleness: how many models the Back End has made since the one the AIM was trained from.
    public static double MixShare(double alpha, int staleness) => alpha / Math.Sqrt(1 + staleness);

    public static float[] Mix(float[] current, float[] arrived, double share)
    {
        var next = new float[current.Length];
        for (var i = 0; i < next.Length; i++) next[i] = (float)((1 - share) * current[i] + share * arrived[i]);
        return next;
    }
}

// THE LABEL BANK. Starts with the Back End's own model's answers on its inputs, counted as the cases that model learned from; each arrived AIM moves every label by its
// share of the cases received so far (a running average over all the AIMs, weighted by their cases); the Back End's model is then trained on the bank (a few passes,
// starting from its current model).
public sealed class FlLabelBank(FrontEndTrainer runner, IReadOnlyList<float[]> inputs, int ownCases, int networkEpochs = 1, int headEpochs = 60)
{
    private double[]? _labels;
    private double _weight;
    public string Note { get; private set; } = "";

    public JsonObject Learn(JsonObject current, JsonObject arrived, int cases)
    {
        if (_labels is null) { _labels = runner.Probabilities(current, inputs); _weight = ownCases; }
        var p = runner.Probabilities(arrived, inputs);
        var share = cases / (_weight + cases); var moved = 0;
        for (var i = 0; i < p.Length; i++) { var before = _labels[i] >= 0.5; _labels[i] += share * (p[i] - _labels[i]); if ((_labels[i] >= 0.5) != before) moved++; }
        _weight += cases;
        Note = $"the label bank of {inputs.Count} inputs moved by {share:P1} towards the AIM ({moved} labels changed side)";
        return runner.TrainOn(current, inputs.Select((x, i) => (x, (float)_labels[i])).ToList(), networkEpochs, headEpochs);
    }
}
