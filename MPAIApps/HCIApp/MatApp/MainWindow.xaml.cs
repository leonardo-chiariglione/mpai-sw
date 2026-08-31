using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;   // ComboBoxItem

using Mpai.Core;      // BasicSpeechObject
using Mpai.Hci.Api;   // HciApi
using Mpai.UaKit;     // AvatarUaHost - the shared UA plumbing

namespace MatApp;

// UAD-MAT - the User Agent for Multimodal Anonymous Translation. A thin client: it
// owns its UI (From/To language pickers + Listen) and its one Module call
// (HciApi.Translate); the real-world edges (WebView avatar renderer, mic capture, the
// continuous listen loop, present) are provided by Mpai.UaKit's AvatarUaHost, shared
// with every avatar UA. The human speaks in one language; the avatar speaks the
// translation in another, lip-synced. The language pair is chosen and remembered.
public partial class MainWindow : Window
{
    private const string AmdDir       = @"D:\AI\AIMs\AMDs";
    private const string SettingsPath = @"D:\AI\AIMs\aim-settings.json";
    private static readonly string AssetsDir = @"D:\AI\Lib\Assets";

    // The languages the Text-To-Speech has voices for.
    private static readonly (string Code, string Name)[] Languages =
    {
        ("en", "English"), ("it", "Italiano"), ("es", "Espanol"), ("pt", "Portugues"),
        ("fr", "Francais"), ("de", "Deutsch"), ("ja", "Nihongo"), ("zh", "Zhongwen")
        // Every language is offered: the written translation is always shown; it is
        // also spoken where a voice can synthesise it. Chinese and Japanese show the
        // translated text but are not yet spoken (their voices need dedicated CJK
        // phonemisers - pinyin / OpenJTalk - not in this espeak-only build).
    };

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
        foreach (var (code, name) in Languages)
        {
            FromLang.Items.Add(new ComboBoxItem { Content = name, Tag = code });
            ToLang.Items.Add(new ComboBoxItem { Content = name, Tag = code });
        }
        FromLang.SelectedIndex = 0;   // English
        ToLang.SelectedIndex = 1;     // Italiano

        _ua = new AvatarUaHost(Web, Dispatcher, AmdDir, AssetsDir);
        await _ua.InitAsync();
        _ua.RunningChanged += running => Dispatcher.Invoke(() =>
        {
            ListenButton.Content = running ? "Stop" : "Listen";
            StatusText.Text = running ? "Listening - speak." : "Choose languages, press Listen, and speak.";
        });

        _hci = new HciApi(AmdDir, SettingsPath);
        _ready = true;

        // Warm the translation model in the background so the first real turn is fast.
        _ = Task.Run(() =>
        {
            try { _hci!.Translate(BasicSpeechObject.FromData(new byte[64], null), "en", "it"); }
            catch { }
        });
    }

    private string FromCode() => (FromLang.SelectedItem as ComboBoxItem)?.Tag as string ?? "en";
    private string ToCode()   => (ToLang.SelectedItem   as ComboBoxItem)?.Tag as string ?? "it";

    // Listen toggles a continuous translation session. Each spoken turn is handled by
    // Translate(from, to); the avatar speaks the translation and then listens again.
    private void ListenButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _hci is null || _ua is null) return;
        if (_ua.IsRunning) { _ua.StopLoop(); return; }
        string from = FromCode(), to = ToCode();
        _ua.StartLoop(speech =>
        {
            var avatar = _hci!.Translate(speech, from, to);
            Dispatcher.Invoke(() => TranslationText.Text = avatar.TranslatedText ?? "");
            return avatar;
        });
    }
}
