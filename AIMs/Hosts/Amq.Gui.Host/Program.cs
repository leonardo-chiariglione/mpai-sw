using System;
using System.Windows.Forms;

using AIF.Store;

using Mpai.Core;
using Mpai.Aims.Visual;
using Mpai.Aims.Audio;
using Mpai.Aims.Asr;
using Mpai.Aims.Tiq;
using Mpai.Aims.Tts;
using Mpai.Amq;

// ============================================================================
//  AMQApp - Answer to Multimodal Question (MMC-AMQ-V2.5)
//
//  Select an image, speak a question, hear the answer.
//
//  This application is a thin host. It builds the six SubAIMs, wires the two
//  visual and audio edges to the window, and runs. Everything it needs - the
//  models, the tools, and the folder images are chosen from - comes from
//  D:\AI\AIMs\aim-settings.json, so none of it is compiled in.
// ============================================================================
internal static class Program
{
    private const string SettingsFile = @"D:\AI\AIMs\aim-settings.json";

    private const string DefaultImagesFolder = @"D:\AI\MPAIApps\VisualIn";

    [STAThread]
    private static void Main()
    {
        var settings =
            AimSettings.Load(SettingsFile);

        // The AIMs report through Mpai.Core.AimLog; a windowed application has
        // no console, so the window shows what matters and the rest is dropped.
        AimLog.Sink = null;

        // Where the image picker starts. Change it in aim-settings.json, under
        // CVE-VOA-V1.0 / SourceHint; the picker can also change it at run time.
        var visualSettings =
            settings.For("CVE-VOA-V1.0");

        var imagesFolder =
            visualSettings.TryGetValue("SourceHint", out var configured) &&
            !string.IsNullOrWhiteSpace(configured)
                ? configured
                : DefaultImagesFolder;

        // ---- acquisition and delivery edges (environment-dependent) ----
        IVisualAcquisitionAim voa = new WinFormsVisualAcquisition(imagesFolder);
        IAudioAcquisitionAim  aoa = new WasapiAudioAcquisition();
        IAudioDeliveryAim     aod = new WinmmAudioDelivery();

        // ---- central SubAIMs (environment-independent) ----
        var asr = AsrFactory.Create(settings.For("MMC-ASR-V2.5"));
        var tiq = TiqFactory.Create(settings.For("MMC-TIQ-V2.5"));
        var tts = TtsFactory.Create(settings.For("MMC-TTS-V2.5"));

        // ---- the composite AIM (6 SubAIMs) ----
        var amq = new AmqCompositeAim(voa, aoa, asr, tiq, tts, aod);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var window =
            new ObserverWindow(amq, imagesFolder);

        // Visual Object Delivery renders onto the surface the window owns,
        // just as Audio Object Delivery renders to the sound device.
        window.VisualDelivery =
            new WinFormsVisualDelivery(window.ImageSurface);

        Application.Run(window);
    }
}

