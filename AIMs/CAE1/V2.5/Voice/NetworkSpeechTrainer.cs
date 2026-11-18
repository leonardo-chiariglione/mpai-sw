// NOTICE (third-party models and data). This source file is released under the BSD 3-Clause licence of this repository.
// The models and the databases it uses, and the data derived from them, are not part of it: each has its own licence, and some
// restrict use (for example to non-commercial research). Read THIRD-PARTY-NOTICES.md and the Terms of the AccessSources of the AIM.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Mpai.Cae.Usc.Smc;
using Mpai.Osd.Speech;

namespace Mpai.Cae.Usc.Voice;

// SPEECH MODEL CREATION DONE OFFLINE: a real training of a speech generator (a VITS network, Piper's, fine-tuned on the speech of the speaker) takes about an hour on a GPU, far too long
// for an App that a person uses. The network is trained beforehand on a known set of Speech Segments for Modelling and set aside in a folder of networks, one sub-folder per set, named
// as the set is (for instance LJ-05min: five minutes of LJ Speech): <folder>\<name>\<name>.onnx and <name>.onnx.json, with times.txt saying how it was trained. Given the Speech Segments
// for Modelling whose Speech Object has that ID, this trainer gives that network as the Neural Network Speech Model, named by its ID (MLModelData DataID): model data of the deployment,
// which the Text To Speech that speaks with it holds in its own folder of networks (no AIM opens a file named in data).
// A set of segments for which no network was trained is refused: this trainer never gives another voice in its place.
public sealed class NetworkSpeechTrainer(string folder) : ISpeechModelTrainer
{
    public const string MethodName = "a VITS network (Piper) fine-tuned offline on the speech of the speaker";

    public TrainedSpeechModel Train(IReadOnlyList<Audio> segments) => Train(segments, null);

    public TrainedSpeechModel Train(IReadOnlyList<Audio> segments, string? segmentsId)
    {
        if (segments.Count == 0) throw new ArgumentException("There are no Speech Segments for Modelling.");
        var names = Networks(folder);
        if (string.IsNullOrWhiteSpace(segmentsId) || !names.Contains(segmentsId))
            throw new ArgumentException($"No network was trained on the Speech Segments for Modelling '{segmentsId}': the networks trained offline are {string.Join(", ", names)}.");
        var record = Path.Combine(folder, segmentsId, "times.txt");
        var how = File.Exists(record) ? " (" + File.ReadLines(record).First().Trim() + ")" : "";
        return new TrainedSpeechModel([], $"{MethodName}, network {segmentsId}{how}", segments.Count, segments.Sum(s => s.Seconds), segmentsId);
    }

    public bool Has(string? segmentsId) => !string.IsNullOrWhiteSpace(segmentsId) && Networks(folder).Contains(segmentsId);

    // The networks set aside in the folder: each sub-folder holding <name>.onnx and its <name>.onnx.json.
    public static IReadOnlyList<string> Networks(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateDirectories(folder).Select(Path.GetFileName).OfType<string>()
                       .Where(n => File.Exists(Path.Combine(folder, n, n + ".onnx")) && File.Exists(Path.Combine(folder, n, n + ".onnx.json"))).OrderBy(n => n).ToList()
            : [];
}

// Speech Model Creation as deployed: Speech Segments for Modelling for which a network was trained offline (by the name of their Speech Object) are given that network;
// any other segments are modelled by the trainer the deployment sets (the stand-in, or OpenVoice).
public sealed class OfflineNetworkOr(string folder, ISpeechModelTrainer otherwise) : ISpeechModelTrainer
{
    private readonly NetworkSpeechTrainer _networks = new(folder);

    public TrainedSpeechModel Train(IReadOnlyList<Audio> segments) => otherwise.Train(segments);

    public TrainedSpeechModel Train(IReadOnlyList<Audio> segments, string? segmentsId) =>
        _networks.Has(segmentsId) ? _networks.Train(segments, segmentsId) : otherwise.Train(segments, segmentsId);
}
