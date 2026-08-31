using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

using Microsoft.Web.WebView2.Core;

using AIF.Controller;
using AIF.Store;
using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Osd.Tod;   // WebView3DModelDelivery

namespace CavApp;

// The In-Cabin conversational CAV (an HCI application). It presents the Speaking
// Avatar: on "Say", it runs the Response and Scene Rendering Module (RSR = PSD +
// TTS + GFD) via the AIF Controller to PRODUCE the Machine Speech + the Machine
// Face Descriptors (the facial-animation timeline with lip-sync), then delivers
// them to the embedded WebView renderer (3OD's device) which plays the speech and
// animates the avatar in sync. Dialogue slice: text intent -> RSR -> present.
public partial class MainWindow : Window
{
    private const string RsrModule = "UAG-RSR-V1.0";
    private UserAgent? _ua;
    private CavProvider? _provider;
    private AimSettings? _settings;
    private WebView3DModelDelivery? _renderer;   // 3OD's device (WebView-backed)
    private bool _ready;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // --- WebView2: allow audio autoplay (the speech is played from a message
        // handler, not an in-page click, which Chromium would otherwise block) ---
        var env = await CoreWebView2Environment.CreateAsync(null, null,
            new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
        await Web.EnsureCoreWebView2Async(env);
        var webDir = Path.Combine(AppContext.BaseDirectory, "web");
        Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "cavapp.local", webDir, CoreWebView2HostResourceAccessKind.Allow);
        Web.CoreWebView2.Navigate("https://cavapp.local/cav-webview.html");

        // The 3OD device: posts render messages to the WebView (on the UI thread).
        _renderer = new WebView3DModelDelivery(json =>
        {
            return Dispatcher.InvokeAsync(() => Web.CoreWebView2.PostWebMessageAsJson(json)).Task;
        });

        // --- AIF: the Controller, store, provider for the RSR Module's SubAIMs ---
        var store = new AmdStore(@"D:\AI\AIMs\AMDs");
        store.Scan();
        _settings = AimSettings.Load(@"D:\AI\AIMs\aim-settings.json");
        _ua = new UserAgent(store);
        _provider = new CavProvider(store);
        _ua.MPAI_AIFU_Controller_Initialize();

        _ready = true;
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SayButton_Click(sender, e);
    }

    private async void SayButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _ua is null || _provider is null || _settings is null || _renderer is null) return;
        string text = InputBox.Text?.Trim() ?? "";
        if (text.Length == 0) return;

        SayButton.IsEnabled = false;
        try
        {
            // The machine's Personal Status (as EDP would produce it): calm + respectful.
            var machineEps = new EntityPersonalStatus
            {
                TextPersonalStatus = new TextPersonalStatus
                {
                    TextEmotion        = Emotion.Of(FactorLabel.Of("CALMNESS", "calm", null, 0.8)),
                    TextSocialAttitude = SocialAttitude.Of(FactorLabel.Of("SOCIAL RANK", "respectful", null, 0.8))
                }
            };

            var (fdo, speechWav) = await Task.Run(() => RunRsr(text, machineEps));

            // Deliver the Speaking Avatar to the renderer (3OD's device): the FDO
            // animation timeline + the speech WAV; the WebView plays + animates in sync.
            var model = Basic3DModelObject.FromData(Array.Empty<byte>());   // avatar bundled in the renderer
            if (speechWav.Length == 0)
                System.Windows.MessageBox.Show("RSR produced no speech (0 bytes).", "CAV");
            await _renderer.DeliverWithSpeechAsync(model, fdo, speechWav);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.ToString(), "CAV error");
        }
        finally { SayButton.IsEnabled = true; }
    }

    // Run the RSR Module via the Controller; return the produced FDO + speech WAV bytes.
    private (FaceDescriptorsObject? fdo, byte[] speechWav) RunRsr(string text, EntityPersonalStatus eps)
    {
        if (_ua!.MPAI_AIFU_AIW_Start(RsrModule, _provider!, _settings!, out var id) != AifError.OK)
            return (null, Array.Empty<byte>());
        try
        {
            var boundary = new Dictionary<string, string>
            {
                ["TextObject"]     = MpaiJson.ToJson(BasicTextObject.FromText(text)),
                ["PersonalStatus"] = MpaiJson.ToJson(eps)
            };
            var (err, outcome) = _ua.RunAsync(id, boundary).GetAwaiter().GetResult();
            if (err != AifError.OK || outcome?.Completed is null || outcome.Completed.IsError)
                return (null, Array.Empty<byte>());

            var outs = outcome.Completed.Ports;
            FaceDescriptorsObject? fdo = null;
            byte[] wav = Array.Empty<byte>();
            if (outs.TryGetValue("MachineFaceDescriptors", out var fj) && !string.IsNullOrWhiteSpace(fj))
                fdo = MpaiJson.FromJson<FaceDescriptorsObject>(fj);
            if (outs.TryGetValue("MachineSpeech", out var sj) && !string.IsNullOrWhiteSpace(sj))
                wav = MpaiJson.FromJson<BasicSpeechObject>(sj)?.Data ?? Array.Empty<byte>();
            return (fdo, wav);
        }
        finally { _ua.MPAI_AIFU_AIW_Stop(id); }
    }
}