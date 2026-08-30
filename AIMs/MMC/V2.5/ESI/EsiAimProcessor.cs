using System;
using System.Collections.Generic;
using System.Linq;

using AIF.Controller;

using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Mmc.Sir;   // WavReader (shared audio primitive, as ESD does)

namespace Mpai.Mmc.Esi;

// MMC-ESI-V2.5 - Entity Speech Interpretation, as an AIF IAimProcessor.
//
// Receives a Basic Speech Object (OSD-BSO) and produces the Speech Personal Status
// (MMC-SPS): the Cognitive State, Emotion, and Social Attitude Factors carried by
// the speech, each a chosen label + Degree. Fused description and interpretation:
// the AIM reads affect directly from the speech media.
//
// ENGINE (first pass, deliberately simple - proves the AIM and the SPS data shape
// end-to-end, then deepens without touching the interface): a prosodic heuristic
// over the PCM samples - overall energy (RMS) as an arousal proxy - mapped to an
// Emotion label + Degree. Cognitive State and Social Attitude are left null in this
// first pass. (The planned upgrade is a speech-emotion model, e.g. wav2vec2
// valence/arousal/dominance, run locally; the interface does not change.)
public sealed class EsiAimProcessor : IAimProcessor
{
    private readonly string _instanceId;
    private readonly string _inPort;   // OSD-BSO
    private readonly string _outPort;  // MMC-SPS

    public EsiAimProcessor(string instanceId, AimPortReader ports)
    {
        _instanceId = instanceId;
        _inPort  = ports.Input("OSD-BSO-V1.5");
        _outPort = ports.Output("MMC-SPS-V2.5");
    }

    public string InstanceId => _instanceId;

    public System.Threading.Tasks.Task<Message> ProcessAsync(Message message)
    {
        if (!message.Ports.TryGetValue(_inPort, out var bsoJson) || string.IsNullOrWhiteSpace(bsoJson))
            return System.Threading.Tasks.Task.FromResult(
                Message.Error(message.MessageId, _instanceId, "no Basic Speech Object on input port"));

        var speech = MpaiJson.FromJson<BasicSpeechObject>(bsoJson);
        if (speech is null || speech.Data.Length == 0)
            return System.Threading.Tasks.Task.FromResult(
                Message.Error(message.MessageId, _instanceId, "empty Basic Speech Object"));

        // First-pass prosodic arousal from sample energy (RMS), normalised to [0,1].
        // Decode the WAV to mono 16 kHz samples the same way the speech AIMs do.
        var samples = WavReader.ReadMono16k(speech.Data);
        double arousal = RmsEnergy(samples);

        // Map arousal to a coarse Emotion label. High energy -> aroused/positive
        // (HAPPINESS); low energy -> calm. Degree = the arousal magnitude.
        Emotion emotion = arousal >= 0.5
            ? Emotion.Of(FactorLabel.Of("HAPPINESS", "happy", null, arousal))
            : Emotion.Of(FactorLabel.Of("CALMNESS", "calm", null, 1.0 - arousal));

        var sps = new SpeechPersonalStatus { SpeechEmotion = emotion };

        return System.Threading.Tasks.Task.FromResult(new Message
        {
            MessageId = message.MessageId,
            MessageType = message.MessageType,
            Ports = new Dictionary<string, string> { [_outPort] = MpaiJson.ToJson(sps) }
        });
    }

    // Root-mean-square energy of the mono samples (assumed roughly [-1,1] float),
    // scaled to [0,1] as a coarse arousal proxy.
    private static double RmsEnergy(float[] samples)
    {
        if (samples.Length == 0) return 0.0;
        double sumSq = 0;
        foreach (var x in samples) sumSq += (double)x * x;
        double rms = Math.Sqrt(sumSq / samples.Length);
        return Math.Clamp(rms * 4.0, 0.0, 1.0);   // speech RMS is well below full-scale
    }
}
