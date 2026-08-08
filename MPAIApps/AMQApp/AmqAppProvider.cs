using System;
using System.Collections.Generic;
using System.Windows.Forms;

using AIF.Controller;
using AIF.Store;

using Mpai.Aims.Asr;
using Mpai.Aims.Audio;
using Mpai.Aims.Tiq;
using Mpai.Aims.Tts;
using Mpai.Aims.Visual;

namespace MPAIApps.AMQApp;

public sealed class AmqAppProvider : IAimProvider
{
    private readonly AmdStore   _store;
    private readonly PictureBox _surface;

    public AmqAppProvider(AmdStore store, PictureBox surface)
    {
        _store   = store;
        _surface = surface;
    }

    public IAimProcessor Create(
        string aimName,
        IReadOnlyDictionary<string, string> settings)
    {
        return aimName switch
        {
            "CVE-VOA-V1.0" =>
                new VoaAimProcessor(aimName, new WinFormsVisualAcquisition(), _store),

            "CAE-AOA-V1.0" =>
                new AoaAimProcessor(
                    aimName, new WasapiAudioAcquisition(), _store,
                    TimeSpan.FromSeconds(Num(settings, "DurationSeconds", 5))),

            "MMC-ASR-V2.5" =>
                new AsrAimProcessor(aimName, AsrFactory.Create(settings), _store),

            "MMC-TIQ-V2.5" =>
                new TiqAimProcessor(aimName, TiqFactory.Create(settings), _store),

            "MMC-TTS-V2.5" =>
                new TtsAimProcessor(aimName, TtsFactory.Create(settings), _store),

            "CAE-AOD-V1.0" =>
                new AodAimProcessor(aimName, new WinmmAudioDelivery(), _store),

            "CVE-VOD-V1.0" =>
                new VodAimProcessor(
                    aimName, new WinFormsVisualDelivery(_surface), _store),

            _ => throw new NotSupportedException($"No implementation for {aimName}.")
        };
    }

    private static double Num(IReadOnlyDictionary<string, string> s, string k, double d) =>
        s.TryGetValue(k, out var v) &&
        double.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : d;
}
