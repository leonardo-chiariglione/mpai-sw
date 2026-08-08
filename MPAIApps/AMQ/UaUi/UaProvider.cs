using System;
using System.Collections.Generic;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;

using Mpai.Aims.Asr;
using Mpai.Aims.Audio;
using Mpai.Aims.Tiq;
using Mpai.Aims.Tts;
using Mpai.Aims.Visual;

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

    public IAimProcessor Create(
        string aimName,
        IReadOnlyDictionary<string, string> settings)
    {
        return aimName switch
        {
            "CVE-VOA-V1.0" =>
                new VoaAimProcessor(
                    aimName,
                    new FileVisualAcquisition(string.Empty),
                    _store,
                    string.Empty),

            "CAE-AOA-V1.0" =>
                new AoaAimProcessor(
                    aimName,
                    new FileAudioAcquisition(string.Empty),
                    _store,
                    TimeSpan.FromSeconds(5)),

            // Cache the process-backed ASR core.
            "MMC-ASR-V2.5" =>
                new AsrAimProcessor(
                    aimName,
                    _asr ??= AsrFactory.Create(settings),
                    _store),

            // Cache the heavy in-process BLIP model.
            "MMC-TIQ-V2.5" =>
                new TiqAimProcessor(
                    aimName,
                    _tiq ??= TiqFactory.Create(settings),
                    _store),

            // Cache the process-backed TTS core.
            "MMC-TTS-V2.5" =>
                new TtsAimProcessor(
                    aimName,
                    _tts ??= TtsFactory.Create(settings),
                    _store),

            "CAE-AOD-V1.0" =>
                new AodAimProcessor(
                    aimName,
                    new FileAudioDelivery(_outputFolder),
                    _store),

            "CVE-VOD-V1.0" =>
                new VodAimProcessor(
                    aimName,
                    new FileVisualDelivery(_outputFolder),
                    _store),

            _ => throw new NotSupportedException($"No implementation for {aimName}.")
        };
    }
}
