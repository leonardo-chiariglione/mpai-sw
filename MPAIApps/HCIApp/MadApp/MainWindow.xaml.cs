using System;
using System.Threading.Tasks;
using System.Windows;

using Mpai.Core.OSD;
using Mpai.Hci.Api;   // HciApi
using Mpai.UaKit;     // AvatarUaHost - the shared UA plumbing

namespace MadApp;

// UAD-MAD - the User Agent for Multimodal Anonymous Dialogue. A thin client: it owns
// its UI (text box + Say + Listen) and its one Module call (HciApi.Converse); the
// real-world edges (WebView avatar renderer, mic capture, the continuous listen loop,
// present) are provided by Mpai.UaKit's AvatarUaHost, shared with every avatar UA.
// MMC-MAD is ONE Module (ASR -> EDP -> RSR); the UA does not wire the AIF.
public partial class MainWindow : Window
{
    private const string AmdDir      = @"D:\AI\AIMs\AMDs";
    private const string SettingsPath= @"D:\AI\AIMs\aim-settings.json";
    private static readonly string AssetsDir = @"D:\AI\Lib\Assets";

    private HciApi?       _hci;
    private AvatarUaHost? _ua;
    private bool _ready;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _ua = new AvatarUaHost(Web, Dispatcher, AmdDir, AssetsDir);
        await _ua.InitAsync();
        _ua.RunningChanged += running => Dispatcher.Invoke(() =>
        {
            ListenButton.Content = running ? "Stop" : "Listen";
            SayButton.IsEnabled  = !running;
        });

        _hci = new HciApi(AmdDir, SettingsPath);
        _ready = true;

        // Warm the dialogue model in the background so the first real turn is fast.
        _ = Task.Run(() => { try { _hci.Converse(text: "hello"); } catch { } });
    }

    // Listen toggles the continuous conversation loop. Each spoken turn is handled by
    // Converse; the CAV answers and then listens again, threading the running Summary.
    private void ListenButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _hci is null || _ua is null) return;
        if (_ua.IsRunning) { _ua.StopLoop(); return; }
        _ua.StartLoop(speech => _hci!.Converse(speech: speech));
    }

    private async void SayButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _hci is null || _ua is null) return;
        string humanText = InputBox.Text?.Trim() ?? "";
        if (humanText.Length == 0) return;

        SayButton.IsEnabled = false; ListenButton.IsEnabled = false;
        InputBox.Clear();
        try
        {
            var avatar = await Task.Run(() => _hci!.Converse(text: humanText));
            await _ua.PresentAsync(avatar);
        }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.ToString(), "CAV error"); }
        finally { SayButton.IsEnabled = true; ListenButton.IsEnabled = true; }
    }
}
