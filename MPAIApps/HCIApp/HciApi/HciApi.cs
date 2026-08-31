using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.Store;
using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Hci.Api;

// The HCI API (MPAI-HCI middleware API, per M3152 Â§5). A thin faÃ§ade over the HCI
// Modules: it holds the AIF UserAgent/Controller and the Module providers, and
// exposes the specified operations so HCI applications are thin clients that supply
// intent and consume products - they do not wire the AIF themselves.
//
// This first cut implements the dialogue slice: SubmitDialogueIntent (Entity
// Dialogue Processing) and ReceiveSpeakingAvatar (Response and Scene Rendering).
// Presentation of the Speaking Avatar is the app's SAR seam (a device write, below
// the API). The whole HCI MW is one AIW/Module under one Controller (one PTF trust
// domain); this faÃ§ade is the app-facing surface above the API boundary.
public sealed class HciApi : IDisposable
{
    private const string EdpModule = "UAG-EDP-V1.0";   // Entity Dialogue Processing (verify name)
    private const string RsrModule = "UAG-RSR-V1.0";   // Response and Scene Rendering

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

    // ---- M3152 Â§5.1 Supply intent: SubmitDialogueIntent ----
    // Give the dialogue processor the human's turn; receive the generated reply
    // (Machine Text + the machine's Personal Status). Runs Entity Dialogue Processing.
    // NOTE: EDP's exact boundary ports are filled once its L3 is confirmed.
    public DialogueOutput SubmitDialogueIntent(string humanText, EntityPersonalStatus? humanStatus = null)
    {
        var boundary = new Dictionary<string, string>
        {
            ["TextObject"]     = MpaiJson.ToJson(BasicTextObject.FromText(humanText)),
            ["PersonalStatus"] = MpaiJson.ToJson(humanStatus ?? new EntityPersonalStatus())
        };
        var outs = RunModule(EdpModule, boundary);
        var machineText = ""; EntityPersonalStatus? machinePs = null;
        if (outs.TryGetValue("MachineTextObject", out var mt) && !string.IsNullOrWhiteSpace(mt))
            machineText = MpaiJson.FromJson<BasicTextObject>(mt)?.GetText() ?? "";
        if (outs.TryGetValue("MachinePersonalStatus", out var mp) && !string.IsNullOrWhiteSpace(mp))
            machinePs = MpaiJson.FromJson<EntityPersonalStatus>(mp);
        return new DialogueOutput(machineText, machinePs ?? new EntityPersonalStatus());
    }

    // ---- M3152 Â§5.2 Consume product: ReceiveSpeakingAvatar ----
    // Render the machine's reply as the Speaking Avatar: Machine Speech + the Machine
    // Face Descriptors (the facial-animation timeline). Runs Response and Scene Rendering.
    public SpeakingAvatar ReceiveSpeakingAvatar(string machineText, EntityPersonalStatus machineStatus)
    {
        var boundary = new Dictionary<string, string>
        {
            ["TextObject"]     = MpaiJson.ToJson(BasicTextObject.FromText(machineText)),
            ["PersonalStatus"] = MpaiJson.ToJson(machineStatus)
        };
        var outs = RunModule(RsrModule, boundary);
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

// The reply the dialogue processor generated: the machine's text + its Personal Status.
public sealed record DialogueOutput(string MachineText, EntityPersonalStatus MachinePersonalStatus);

// The Speaking Avatar product: Machine Speech (WAV) + the Machine Face Descriptors
// (the facial-animation timeline). The app presents it on the device (SAR seam).
public sealed record SpeakingAvatar(byte[] MachineSpeechWav, FaceDescriptorsObject? FaceDescriptors);
