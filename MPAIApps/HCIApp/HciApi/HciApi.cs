using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.Store;
using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Hci.Api;

// The HCI API (MPAI-HCI middleware API). A thin faÃ§ade the User Agent (UAD-MAD)
// uses to drive the MMC-MAD Middleware Module across the north API. MMC-MAD is ONE
// AIW: the Controller reads its L3, recurses its SubAIMs (Automatic Speech
// Recognition, Entity Dialogue Processing, Response and Scene Rendering) and runs
// the whole pipeline. The UA supplies a turn - the human's spoken Speech Object OR
// typed Text Object - at the boundary, and consumes the Speaking Avatar (Machine
// Speech + Machine Face Descriptors) the run produces. Acquisition (mic) and
// delivery (loudspeaker, screen) are the UA's real-world edges, outside the Module.
public sealed class HciApi : IDisposable
{
    private const string MadModule = "MMC-MAD-V2.5";   // Multimodal Anonymous Dialogue

    private readonly UserAgent    _ua;
    private readonly HciProvider  _provider;
    private readonly AimSettings  _settings;

    public HciApi(string amdDir, string settingsPath)
    {
        var store = new AmdStore(amdDir);
        store.Scan();
        _settings = AimSettings.Load(settingsPath);
        _provider = new HciProvider(store);
        _ua = new UserAgent(store);
        _ua.MPAI_AIFU_Controller_Initialize();
    }

    // Drive one dialogue turn through MMC-MAD. Supply the human's turn at the
    // boundary - a typed Text Object and/or a spoken Speech Object - and receive
    // the Speaking Avatar the Module produces. MAD recognises speech (if given),
    // processes the dialogue, and renders the response; the Machine's own Personal
    // Status is generated inside EDP.
    public SpeakingAvatar Converse(string? text = null, BasicSpeechObject? speech = null)
    {
        var boundary = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(text))
            boundary["TextObject"] = MpaiJson.ToJson(BasicTextObject.FromText(text));
        if (speech is not null && speech.Data.Length > 0)
            boundary["InputSpeech"] = MpaiJson.ToJson(speech);

        var outs = RunModule(MadModule, boundary);

        byte[] wav = Array.Empty<byte>(); FaceDescriptorsObject? fdo = null;
        if (outs.TryGetValue("MachineSpeech", out var sj) && !string.IsNullOrWhiteSpace(sj))
            wav = MpaiJson.FromJson<BasicSpeechObject>(sj)?.Data ?? Array.Empty<byte>();
        if (outs.TryGetValue("MachineFaceDescriptors", out var fj) && !string.IsNullOrWhiteSpace(fj))
            fdo = MpaiJson.FromJson<FaceDescriptorsObject>(fj);
        return new SpeakingAvatar(wav, fdo);
    }

    private IReadOnlyDictionary<string, string> RunModule(string module, Dictionary<string, string> boundary)
    {
        if (_ua.MPAI_AIFU_AIW_Start(module, _provider, _settings, out var id) != AifError.OK)
            return new Dictionary<string, string>();
        try
        {
            var (err, outcome) = _ua.RunAsync(id, boundary).GetAwaiter().GetResult();
            if (err != AifError.OK || outcome?.Completed is null || outcome.Completed.IsError)
                return new Dictionary<string, string>();
            return outcome.Completed.Ports;
        }
        finally { _ua.MPAI_AIFU_AIW_Stop(id); }
    }

    public void Dispose() => _provider.Dispose();
}

// The Speaking Avatar product: Machine Speech (WAV) + the Machine Face Descriptors
// (the facial-animation timeline). The UA presents it on its devices (loudspeaker,
// screen) - the real-world delivery edge.
public sealed record SpeakingAvatar(byte[] MachineSpeechWav, FaceDescriptorsObject? FaceDescriptors);
