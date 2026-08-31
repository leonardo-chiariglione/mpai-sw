using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

using Microsoft.Web.WebView2.Core;

using Mpai.Core.OSD;
using Mpai.Hci.Api;   // HciApi - the HCI middleware API faÃ§ade
using Mpai.Osd.Tod;   // WebView3DModelDelivery

namespace CavApp;

// The In-Cabin conversational CAV (an HCI application), now a THIN CLIENT of the HCI
// API. On "Say", it supplies the human's turn to the API (SubmitDialogueIntent ->
// Entity Dialogue Processing produces the Machine's reply + Personal Status), then
// asks the API to render the Speaking Avatar (ReceiveSpeakingAvatar -> Response and
// Scene Rendering produces the Machine Speech + facial-animation timeline). The app
// presents the Speaking Avatar on the device - the SAR presentation seam (a device
// write, below the API): it posts speech + animation to the embedded WebView, which
// plays and lip-syncs the avatar. The app supplies intent and consumes products; it
// does not wire the AIF.
public partial class MainWindow : Window
{
    private HciApi? _hci;                          // the HCI middleware API
    private WebView3DModelDelivery? _renderer;     // 3OD's device (the SAR presentation)
    private bool _ready;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // WebView2 with autoplay allowed (the speech is played from a message handler).
        var env = await CoreWebView2Environment.CreateAsync(null, null,
            new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
        await Web.EnsureCoreWebView2Async(env);
        var webDir = Path.Combine(AppContext.BaseDirectory, "web");
        Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "cavapp.local", webDir, CoreWebView2HostResourceAccessKind.Allow);
        Web.CoreWebView2.Navigate("https://cavapp.local/cav-webview.html");

        _renderer = new WebView3DModelDelivery(json =>
            Dispatcher.InvokeAsync(() => Web.CoreWebView2.PostWebMessageAsJson(json)).Task);

        // The HCI middleware API - the faÃ§ade over the HCI Modules (EDP, RSR).
        _hci = new HciApi(@"D:\AI\AIMs\AMDs", @"D:\AI\AIMs\aim-settings.json");

        _ready = true;

        // Warm the dialogue model in the background so the first real turn is fast
        // (Ollama loads the model on the first request; do that now, discarding the reply).
        _ = Task.Run(() => { try { _hci.SubmitDialogueIntent("hello"); } catch { } });
    }

    private async void SayButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _hci is null || _renderer is null) return;
        string humanText = InputBox.Text?.Trim() ?? "";
        if (humanText.Length == 0) return;

        SayButton.IsEnabled = false;
        InputBox.Clear();
        try
        {
            // Supply intent -> the Machine's reply + Personal Status (Entity Dialogue Processing).
            var reply = await Task.Run(() => _hci.SubmitDialogueIntent(humanText));

            // Render the Speaking Avatar from the reply (Response and Scene Rendering).
            var avatar = await Task.Run(() =>
                _hci.ReceiveSpeakingAvatar(reply.MachineText, reply.MachinePersonalStatus));

            // Present it (SAR seam - device write): post speech + animation to the WebView.
            var model = Basic3DModelObject.FromData(Array.Empty<byte>());   // avatar bundled in the renderer
            await _renderer.DeliverWithSpeechAsync(model, avatar.FaceDescriptors, avatar.MachineSpeechWav);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.ToString(), "CAV error");
        }
        finally { SayButton.IsEnabled = true; }
    }
}
