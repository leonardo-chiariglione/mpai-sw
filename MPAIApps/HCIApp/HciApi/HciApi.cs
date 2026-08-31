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
// AIW (ASR -> EDP -> RSR). The Module is started ONCE and kept alive across turns:
// each turn is one RunAsync on the same instance, so the AIM tree is instantiated
// once (speed) and the running dialogue Summary threads from each turn's
// EditedSummary into the next turn's Summary (context - the dialogue remembers).
// Acquisition (mic) and delivery (loudspeaker, screen) are the UA's real-world
// edges, outside the Module.
public sealed class HciApi : IDisposable
{
    private const string MadModule = "MMC-MAD-V2.5";   // Multimodal Anonymous Dialogue

    private readonly UserAgent    _ua;
    private readonly HciProvider  _provider;
    private readonly AimSettings  _settings;

    private int?    _aiwId;         // the started MMC-MAD instance, kept alive across turns
    private string? _lastSummary;   // the running dialogue Summary (threaded turn to turn)

    public HciApi(string amdDir, string settingsPath)
    {
        var store = new AmdStore(amdDir);
        store.Scan();
        _settings = AimSettings.Load(settingsPath);
        _provider = new HciProvider(store);
        _ua = new UserAgent(store);
        _ua.MPAI_AIFU_Controller_Initialize();
    }

    // Start MMC-MAD once and keep it alive; subsequent turns reuse the instance.
    private bool EnsureStarted()
    {
        if (_aiwId is not null) return true;
        if (_ua.MPAI_AIFU_AIW_Start(MadModule, _provider, _settings, out var id) != AifError.OK)
            return false;
        _aiwId = id;
        return true;
    }

    // Drive one dialogue turn through MMC-MAD. Supply the human's turn - typed text
    // and/or a spoken Speech Object - and receive the Speaking Avatar. The running
    // Summary is threaded automatically, so the dialogue keeps context across turns.
    public SpeakingAvatar Converse(string? text = null, BasicSpeechObject? speech = null)
    {
        if (!EnsureStarted()) return new SpeakingAvatar(Array.Empty<byte>(), null);

        var boundary = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(text))
            boundary["TextObject"] = MpaiJson.ToJson(BasicTextObject.FromText(text));
        if (speech is not null && speech.Data.Length > 0)
            boundary["InputSpeech"] = MpaiJson.ToJson(speech);
        if (!string.IsNullOrWhiteSpace(_lastSummary))
            boundary["Summary"] = _lastSummary;

        var (err, outcome) = _ua.RunAsync(_aiwId!.Value, boundary).GetAwaiter().GetResult();
        if (err != AifError.OK || outcome?.Completed is null || outcome.Completed.IsError)
            return new SpeakingAvatar(Array.Empty<byte>(), null);

        var outs = outcome.Completed.Ports;
        byte[] wav = Array.Empty<byte>(); FaceDescriptorsObject? fdo = null;
        if (outs.TryGetValue("MachineSpeech", out var sj) && !string.IsNullOrWhiteSpace(sj))
            wav = MpaiJson.FromJson<BasicSpeechObject>(sj)?.Data ?? Array.Empty<byte>();
        if (outs.TryGetValue("MachineFaceDescriptors", out var fj) && !string.IsNullOrWhiteSpace(fj))
            fdo = MpaiJson.FromJson<FaceDescriptorsObject>(fj);
        // Thread the running Summary into the next turn.
        if (outs.TryGetValue("EditedSummary", out var es) && !string.IsNullOrWhiteSpace(es))
            _lastSummary = es;

        return new SpeakingAvatar(wav, fdo);
    }

    // Begin a fresh conversation: forget the running Summary. The Module stays
    // alive; only the dialogue context is cleared.
    public void ResetConversation() => _lastSummary = null;

    public void Dispose()
    {
        if (_aiwId is not null) { _ua.MPAI_AIFU_AIW_Stop(_aiwId.Value); _aiwId = null; }
        _provider.Dispose();
    }
}

// The Speaking Avatar product: Machine Speech (WAV) + the Machine Face Descriptors
// (the facial-animation timeline). The UA presents it on its devices (loudspeaker,
// screen) - the real-world delivery edge.
public sealed record SpeakingAvatar(byte[] MachineSpeechWav, FaceDescriptorsObject? FaceDescriptors);
