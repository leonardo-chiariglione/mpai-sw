using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;

using Mpai.Aims.Asr;
using Mpai.Aims.Tiq;
using Mpai.Aims.Tts;

namespace UaUi;

// Composition root for the UA UI.
//
// The heavy in-process model (BLIP, behind ITiqAim) is created ONCE and cached,
// so a fresh AIW per question does not reload it. ASR and TTS are process-based
// (whisper.exe / piper.exe) and cheap to construct, but we cache them too for
// consistency. The remaining AIMs are lightweight and created per run.
//
// Using a fresh AIW per question keeps each run's state clean (correct answers,
// no accumulation) while the cached models keep it fast.
public sealed class UaProvider : IAimProvider
{
    private readonly AmdStore _store;

    // The output folder is kept only so the constructor signature is unchanged
    // for callers; nothing uses it now. It existed for the file-based delivery
    // AIMs, which have gone.
    private readonly string   _outputFolder;

    // Cached heavy/model-backed AIM cores (created once, reused across runs).
    private ITiqAim?         _tiq;
    private WhisperAsrAim?   _asr;
    private PiperTtsAim?     _tts;

    public UaProvider(AmdStore store, string outputFolder)
    {
        _store        = store;
        _outputFolder = outputFolder;
    }

    // Only the three AIMs MMC-AMQ-V2.5 actually contains.
    //
    // Acquisition, presentation and delivery were here too - VOA, AOA, SOA, SOD,
    // AOD, VOD - each built with a FILE device, which was the evidence that they
    // had nothing to do: the window captured with its own recorder and played
    // with its own player, while the AIMs whose purpose is owning those devices
    // were handed files. They interact with the user directly, so they belong to
    // the User Agent, and the Controller no longer asks for them.
    public IAimProcessor Create(
        string aimName,
        IReadOnlyDictionary<string, string> settings)
    {
        return aimName switch
        {
            // Cache the process-backed ASR core.
            "MMC-ASR-V2.5" =>
                new AsrAimProcessor(
                    aimName,
                    _asr ??= AsrFactory.Create(settings),
                    AimPortReader.Load(_store, aimName)),

            // Cache the heavy in-process BLIP model.
            "MMC-TIQ-V2.5" =>
                new TiqAimProcessor(
                    aimName,
                    _tiq ??= TiqFactory.Create(settings),
                    AimPortReader.Load(_store, aimName)),

            // Cache the process-backed TTS core.
            "MMC-TTS-V2.5" =>
                new TtsAimProcessor(
                    aimName,
                    _tts ??= TtsFactory.Create(settings),
                    AimPortReader.Load(_store, aimName)),

            _ => throw new NotSupportedException($"No implementation for {aimName}.")
        };
    }
}