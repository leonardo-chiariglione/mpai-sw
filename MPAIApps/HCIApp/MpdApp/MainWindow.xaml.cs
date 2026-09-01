using System;
using System.Threading.Tasks;
using System.Windows;

using Mpai.Hci.Api;   // HciApi
using Mpai.UaKit;     // AvatarUaHost

namespace MpdApp;

// UAD-MPD - the User Agent for Multimodal Personal Status-based Dialogue. A thin
// client: its UI (Listen) + one Module call (HciApi.ConverseMpd); the avatar surface,
// the microphone capture and the continuous listen loop are Mpai.UaKit's. The CAV,
// through MMC-MPD, perceives the meaning (NLU) and the feeling (ESI + PSM) of what the
// person says and replies aware of both - spoken by the expressive avatar.
public partial class MainWindow : Window
{
    private const string AmdDir       = @"D:\AI\AIMs\AMDs";
    private const string SettingsPath = @"D:\AI\AIMs\aim-settings.json";
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
            ListenButton.Content = running ? "Stop" : "Listen");

        _hci = new HciApi(AmdDir, SettingsPath);
        _ready = true;

        // Warm the pipeline (loads the LLM + emotion models) so the first turn is fast.
        _ = Task.Run(() =>
        {
            try { _hci!.ConverseMpd(Mpai.Core.BasicSpeechObject.FromData(new byte[64], null)); }
            catch { }
        });
    }

    private void ListenButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _hci is null || _ua is null) return;
        if (_ua.IsRunning) { _ua.StopLoop(); return; }
        _ua.StartLoop(speech => _hci!.ConverseMpd(speech));
    }
}
