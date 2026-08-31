using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;   // ComboBoxItem

using Microsoft.Web.WebView2.Core;

using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Hci.Api;    // HciApi - the HCI middleware API faÃ§ade
using Mpai.Osd.Tod;    // WebView3DModelDelivery
using Mpai.Aims.Audio; // WasapiAudioAcquisition (mic - UA real-world edge)
using Mpai.Aims.Speech;// SoaAimProcessor (Speech Object Acquisition - UA edge)

namespace MatApp;

// UAD-MAT - the User Agent for Multimodal Anonymous Translation: the process that
// controls the MMC-MAT Middleware Module across the north HCI API. It owns the
// real-world edges (mic capture via Speech Object Acquisition; the loudspeaker and
// screen via the WebView renderer), and drives one MMC-MAT run per turn: the human
// speaks in one language; the avatar speaks the translation in another, lip-synced.
// The language pair (from / to) is chosen by the person and remembered, so after the
// first choice they just speak.
public partial class MainWindow : Window
{
    private const string AmdDir = @"D:\AI\AIMs\AMDs";

    // The languages the Text-To-Speech has voices for.
    private static readonly (string Code, string Name)[] Languages =
    {
        ("en", "English"), ("it", "Italiano"), ("es", "Espanol"), ("pt", "Portugues"),
        ("fr", "Francais"), ("de", "Deutsch"), ("ja", "Nihongo"), ("zh", "Zhongwen")
    };

    private HciApi? _hci;
    private WebView3DModelDelivery? _renderer;
    private bool _ready;
    private volatile bool _listening;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Populate the language dropdowns (remembered pair defaults to English -> Italian).
        foreach (var (code, name) in Languages)
        {
            FromLang.Items.Add(new ComboBoxItem { Content = name, Tag = code });
            ToLang.Items.Add(new ComboBoxItem { Content = name, Tag = code });
        }
        FromLang.SelectedIndex = 0;   // English
        ToLang.SelectedIndex = 1;     // Italiano

        // WebView2 with audio autoplay allowed (the speech plays from a message handler).
        var env = await CoreWebView2Environment.CreateAsync(null, null,
            new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
        await Web.EnsureCoreWebView2Async(env);
        var webDir = Path.Combine(AppContext.BaseDirectory, "web");
        Web.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "cavapp.local", webDir, CoreWebView2HostResourceAccessKind.Allow);
        Web.CoreWebView2.Navigate("https://cavapp.local/cav-webview.html");

        _renderer = new WebView3DModelDelivery(json =>
            Dispatcher.InvokeAsync(() => Web.CoreWebView2.PostWebMessageAsJson(json)).Task);

        _hci = new HciApi(AmdDir, @"D:\AI\AIMs\aim-settings.json");
        _ready = true;
    }

    private string FromCode() => (FromLang.SelectedItem as ComboBoxItem)?.Tag as string ?? "en";
    private string ToCode()   => (ToLang.SelectedItem   as ComboBoxItem)?.Tag as string ?? "it";

    // Listen toggles a continuous translation session: press to start, and after each
    // spoken utterance the avatar speaks the translation and then listens again, until
    // pressed to stop.
    private void ListenButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _hci is null || _renderer is null) return;
        if (_listening) { StopListening(); return; }
        _listening = true;
        ListenButton.Content = "Stop";
        _ = Task.Run(TranslateLoopAsync);
    }

    private void StopListening()
    {
        _listening = false;
        Dispatcher.Invoke(() => { ListenButton.Content = "Listen"; StatusText.Text = "Stopped."; });
    }

    private async Task TranslateLoopAsync()
    {
        string from = "", to = "";
        await Dispatcher.InvokeAsync(() => { from = FromCode(); to = ToCode(); });
        try
        {
            while (_listening)
            {
                var speech = CaptureSpeech();
                if (!_listening) break;
                if (speech is null || speech.Data.Length == 0) continue;

                var avatar = await Task.Run(() => _hci!.Translate(speech, from, to));
                await Dispatcher.InvokeAsync(async () =>
                {
                    var model = Basic3DModelObject.FromData(Array.Empty<byte>());
                    await _renderer!.DeliverWithSpeechAsync(model, avatar.FaceDescriptors, avatar.MachineSpeechWav);
                });

                var speakSeconds = WavDurationSeconds(avatar.MachineSpeechWav);
                await Task.Delay(TimeSpan.FromSeconds(speakSeconds + 0.6));
            }
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() => System.Windows.MessageBox.Show(ex.ToString(), "MAT error"));
        }
        finally { StopListening(); }
    }

    // Capture the human's spoken turn (UA real-world edge: mic + Speech Object
    // Acquisition with voice-activity auto-stop). Returns the Speech Object; the
    // recognition + translation happen inside MMC-MAT.
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
}
