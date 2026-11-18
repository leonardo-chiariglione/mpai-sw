using System.Collections.Generic;
using Mpai.Osd.Speech;

namespace Mpai.Cae.Usc.Smc;

// What creates a speech model from Speech Segments for Modelling. The Basic API of AIF has no function to train a model: how a model is created is for the
// Implementer. This is the seam at which a real trainer (a voice model fitted to the segments) replaces the stand-in without touching what the AIM does with its Ports.
public interface ISpeechModelTrainer
{
    TrainedSpeechModel Train(IReadOnlyList<Audio> segments);

    // The same, knowing the ID of the Speech Object that holds the segments (BasicSpeechObjectID): a trainer whose training was done offline on a known set of segments finds its
    // network by it. The others ignore it.
    TrainedSpeechModel Train(IReadOnlyList<Audio> segments, string? segmentsId) => Train(segments);
}

// A trained model: its weights (32-bit floats), how it was made, and from how many segments and how many seconds of speech. A model that is model data the deployment holds
// (a whole network trained offline) is named by its ID (the MLModelData form {DataID} of AIP-MLO-V1.0), and Weights is empty.
public sealed record TrainedSpeechModel(float[] Weights, string Method, int SegmentCount, double Seconds, string? DataId = null);
