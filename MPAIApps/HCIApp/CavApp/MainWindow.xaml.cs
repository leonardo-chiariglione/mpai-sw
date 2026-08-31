using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

using Microsoft.Web.WebView2.Core;

using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Hci.Api;   // HciApi - the HCI middleware API faÃ§ade
using Mpai.Osd.Tod;   // WebView3DModelDelivery
using Mpai.Aims.Audio; // WasapiAudioAcquisition (the mic device - UA real-world edge)
using Mpai.Aims.Speech;// SoaAimProcessor (Speech Object Acquisition - UA edge)

namespace CavApp;

// UAD-MAD - the User Agent for Multimodal Anonymous Dialogue: the process that
// controls the MMC-MAD Middleware Module across the north HCI API. It owns the
// real-world edges (mic capture via Speech Object Acquisition; the loudspeaker and
// screen via the WebView renderer), and drives one MMC-MAD run per turn: it supplies
// the human's turn - typed text or a captured Speech Object - and presents the
// Speaking Avatar (Machine Speech + facial-animation timeline) the Module returns.
// MMC-MAD is ONE Module (ASR -> EDP -> RSR); the UA does not wire the AIF.
public partial class MainWindow : Window
{
    private const string AmdDir = @"D:\AI\AIMs\AMDs";
    private HciApi? _hci;                          // the HCI middleware API
    private WebView3DModelDelivery? _renderer;     // 3OD's device (the SAR presentation)
    private bool _ready;
    private volatile bool _conversing;   // the continuous conversation loop is running

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
        _ = Task.Run(() => { try { _hci.Converse(text: "hello"); } catch { } });
    }

    // Listen toggles a CONTINUOUS conversation: press to start, and after each spoken
    // turn the CAV answers and then listens again automatically - a flowing back-and-
    // forth - until pressed again to stop. The running Summary threads context across
    // the whole conversation.
    private void ListenButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _hci is null || _renderer is null) return;
        if (_conversing) { StopConversation(); return; }
        _conversing = true;
        ListenButton.Content = "Stop";
        SayButton.IsEnabled = false;
        _ = Task.Run(ConversationLoopAsync);
    }

    private void StopConversation()
    {
        _conversing = false;
        Dispatcher.Invoke(() => { ListenButton.Content = "Listen"; SayButton.IsEnabled = true; });
    }

    // The continuous conversation loop (background): listen -> answer -> wait for the
    // avatar to finish speaking -> listen again, until stopped. Empty captures (nobody
    // spoke) simply listen again; the Stop button ends the loop.
    private async Task ConversationLoopAsync()
    {
        try
        {
            while (_conversing)
            {
                var speech = CaptureSpeech();                 // VAD auto-stop
                if (!_conversing) break;
                if (speech is null || speech.Data.Length == 0) continue;

                var avatar = await Task.Run(() => _hci!.Converse(speech: speech));
                await Dispatcher.InvokeAsync(async () =>
                {
                    var model = Basic3DModelObject.FromData(Array.Empty<byte>());
                    await _renderer!.DeliverWithSpeechAsync(model, avatar.FaceDescriptors, avatar.MachineSpeechWav);
                });

                // Let the avatar finish speaking before listening again, so the mic
                // does not capture her own voice.
                var speakSeconds = WavDurationSeconds(avatar.MachineSpeechWav);
                await Task.Delay(TimeSpan.FromSeconds(speakSeconds + 0.6));
            }
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => System.Windows.MessageBox.Show(ex.ToString(), "CAV conversation error"));
        }
        finally { StopConversation(); }
    }

    // Duration of a 16-bit PCM WAV, in seconds, from its bytes.
    private static double WavDurationSeconds(byte[] wav)
    {
        try
        {
            if (wav.Length < 44) return 1.0;
            int channels   = BitConverter.ToInt16(wav, 22);
            int sampleRate = BitConverter.ToInt32(wav, 24);
            int bits       = BitConverter.ToInt16(wav, 34);
            if (channels <= 0 || sampleRate <= 0 || bits <= 0) return 1.0;
            int pos = 12, dataLen = wav.Length - 44;
            while (pos + 8 <= wav.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
                int len = BitConverter.ToInt32(wav, pos + 4);
                if (id == "data") { dataLen = Math.Min(len, wav.Length - (pos + 8)); break; }
                pos += 8 + len + (len & 1);
            }
            double bytesPerSec = sampleRate * channels * (bits / 8.0);
            return bytesPerSec > 0 ? dataLen / bytesPerSec : 1.0;
        }
        catch { return 1.0; }
    }

    private async void SayButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _hci is null || _renderer is null) return;
        string humanText = InputBox.Text?.Trim() ?? "";
        if (humanText.Length == 0) return;

        SayButton.IsEnabled = false; ListenButton.IsEnabled = false;
        InputBox.Clear();
        try
        {
            await ConverseAsync(text: humanText);
        }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.ToString(), "CAV error"); }
        finally { SayButton.IsEnabled = true; ListenButton.IsEnabled = true; }
    }

    // Capture the human's spoken turn (UA real-world edge: mic + Speech Object
    // Acquisition with voice-activity auto-stop). Returns the Speech Object; the
    // recognition (ASR) happens inside MMC-MAD, not here.
    private BasicSpeechObject? CaptureSpeech()
    {
        var store = new AIF.Store.AmdStore(AmdDir);
        store.Scan();
        var mic = new WasapiAudioAcquisition();
        var soa = new SoaAimProcessor("MMC-SOA-V2.5",
            mic, AIF.Controller.AimPortReader.Load(store, "MMC-SOA-V2.5"),
            vadAutoStop: true);
        var msg = new AIF.Controller.Message
        {
            MessageId = System.Guid.NewGuid().ToString(),
            Ports = new System.Collections.Generic.Dictionary<string, string>()
        };
        var outcome = soa.ProcessAsync(msg).GetAwaiter().GetResult();
        var speechJson = outcome.Ports.Values.FirstOrDefault() ?? "";
        return string.IsNullOrWhiteSpace(speechJson) ? null : MpaiJson.FromJson<BasicSpeechObject>(speechJson);
    }

    // Run one dialogue turn through MMC-MAD (one Module): the human's typed text
    // OR spoken Speech Object -> the Speaking Avatar -> present it on the device.
    private async Task ConverseAsync(string? text = null, BasicSpeechObject? speech = null)
    {
        var avatar = await Task.Run(() => _hci!.Converse(text, speech));
        var model = Basic3DModelObject.FromData(Array.Empty<byte>());   // avatar bundled in the renderer
        await _renderer!.DeliverWithSpeechAsync(model, avatar.FaceDescriptors, avatar.MachineSpeechWav);
    }
}
