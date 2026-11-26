using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using Mpai.Core;
using Mpai.UaKit;       // AvatarUaHost, SpeechListener, TypedInput, MachineVoice, StandardDevices
using Mpai.Aif.Api;     // ControllerApi, AvatarUtterance
using Mpai.Ua;          // WorkflowInterpreter, DeviceRegistry
using Mpai.Wdl;         // WorkflowReader

using Mpai.Mas.Client;       // RemoteControllerApi: the Controller API of a Controller that is a program of its own

namespace MmcTst;

// MMC-TST User Agent - Text and Speech Translation (MMC-TST-V2.5), on its own. It does not follow its own steps: it executes the Workflow
// Description of the App (Apps/MMC/TST/MMC-TST.orch) with the interpreter of the User Agent, handing it the Controller API of a Controller
// that is a program of its own (MmcController). The workflow starts the Module, takes the Language Selector (the language the person speaks
// and the one the translation is to be in), and each turn acquires the person's speech or typed text, offers them with the Selector, shows
// the translated Text Object and plays the translated Speech Object. This window supplies only the devices that serve the Data Types (the
// language boxes, the microphone, the text box, the screen, the loudspeaker) and Stop. The instructions are said by the avatar, which also
// plays the translated speech (the window renders the Speech Object; it does no AIM's work).
public partial class MainWindow : Window
{
    private const string BSO = "OSD-BSO-V1.5";
    private const string BTO = "OSD-BTO-V1.5";
    private const string SEL = "OSD-SEL-V1.5";
    private const string MachineName = "MPAI as a Service";

    // The languages the speech engines speak and hear.
    private static readonly (string Code, string Name)[] Languages =
    {
        ("en", "English"), ("it", "Italiano"), ("es", "Espanol"), ("pt", "Portugues"), ("fr", "Francais"), ("de", "Deutsch"), ("el", "Ellinika")
    };

    private static readonly string AmdDir       = Mpai.Core.MpaiPaths.Amds;
    private static readonly string SettingsPath = Mpai.Core.MpaiPaths.Settings;
    private static readonly string AssetsDir    = Mpai.Core.MpaiPaths.Assets;

    // The workflow of the App: the one source, read where the repository keeps it.
    private static string WorkflowPath => Path.Combine(Mpai.Core.MpaiPaths.Root, "Apps", "MMC", "TST", "MMC-TST.orch");

    private IControllerApi?           _north;
    private AvatarUaHost?             _avatar;
    private SpeechListener?           _ears;
    private MachineVoice?             _voice;
    private readonly TypedInput       _typed;
    private CancellationTokenSource?  _stopping;
    private Task?                     _running;
    private bool                      _closing;
    private bool                      _told;           // the instruction of this turn has been said
    private string                    _heard = "";     // what the person said or typed this turn
    private string                    _from = "en";    // the language chosen as the person's

    public MainWindow()
    {
        InitializeComponent();
        foreach (var (code, name) in Languages)
        {
            FromBox.Items.Add(new ComboBoxItem { Content = name, Tag = code });
            ToBox.Items.Add(new ComboBoxItem { Content = name, Tag = code });
        }
        FromBox.SelectedIndex = 0; ToBox.SelectedIndex = 1;
        _typed = new TypedInput(TextBoxIn, TranslateButton);
        Loaded += OnLoaded;
    }

    private string From => (string)((ComboBoxItem)FromBox.SelectedItem).Tag;
    private string To   => (string)((ComboBoxItem)ToBox.SelectedItem).Tag;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SetStatus("Wait while the avatar loads");
            _avatar = new AvatarUaHost(Web, Dispatcher, AmdDir, AssetsDir);
            await _avatar.InitAsync();
            _ears  = new SpeechListener(_avatar);
            _voice = new MachineVoice(_avatar, MachineName, () => new RemoteControllerApi(Program.ControllerUrl), Program.Record);
            var workflow = new WorkflowReader().Read(File.ReadAllText(WorkflowPath));
            _north = new RemoteControllerApi(Program.ControllerUrl);
            await Task.Delay(TimeSpan.FromSeconds(2.0));   // let the avatar/WebView settle before speaking

            _stopping = new CancellationTokenSource();
            StopButton.IsEnabled = true;
            SetStatus("Running.");
            var interpreter = new WorkflowInterpreter(_north!.Async(), Devices(), SetStatus);
            _running = interpreter.RunAsync(workflow, _stopping.Token);
            try { await _running; }
            catch (OperationCanceledException) { }
            if (!_closing) SetStatus("Stopped.");
        }
        catch (Exception fatal) { Program.Record("workflow", fatal); SetStatus("stopped: " + fatal.Message); }
        finally { StopButton.IsEnabled = false; }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StopButton.IsEnabled = false;
        _avatar?.Silence();
        _stopping?.Cancel();
    }

    // Closing ends the workflow first (its On Stop asks the Controller to stop the Module), then the window closes.
    protected override async void OnClosing(CancelEventArgs e)
    {
        if (_closing || _running is null || _running.IsCompleted) { base.OnClosing(e); return; }
        e.Cancel = true;
        _closing = true;
        _avatar?.Silence();
        _stopping?.Cancel();
        try { await Task.WhenAny(_running, Task.Delay(TimeSpan.FromSeconds(10))); } catch { }
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        try { _voice?.Dispose(); } catch { }
        try { (_north as IDisposable)?.Dispose(); } catch { }
        base.OnClosed(e);
    }

    private void SetStatus(string s) => Dispatcher.Invoke(() => StatusText.Text = s);

    // An instruction is shown AND said; the step goes on when she has finished saying it.
    private async Task TellAsync(string words)
    {
        Dispatcher.Invoke(() => InstructionText.Text = words);
        if (_voice is not null) await _voice.SayAsync(words);
    }

    // Once in a turn: the microphone and the text box are asked for together, and a microphone that listens again does not tell again.
    private async Task TellOnceAsync(string words)
    {
        if (_told) return;
        _told = true;
        await TellAsync(words);
    }

    // ---- the devices of this User Agent ---------------------------------------------------------------------------

    private DeviceRegistry Devices()
    {
        var devices = new DeviceRegistry();
        var stop = _stopping!.Token;

        // THE LANGUAGES, CHOSEN BY THE PERSON: the one the person speaks and the one the translation is to be in, one Selector for both.
        devices.RegisterAcquire(SEL, async (_, _) =>
        {
            await TellAsync("Choose the input and output languages, then press OK.");
            var chosen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            RoutedEventHandler ok = (_, _) => chosen.TrySetResult(true);
            using var onStop = stop.Register(() => chosen.TrySetResult(false));
            await Dispatcher.InvokeAsync(() => { LanguagesButton.Click += ok; LanguagesButton.IsEnabled = true; });
            var yes = await chosen.Task;
            string? from = null, to = null;
            await Dispatcher.InvokeAsync(() => { LanguagesButton.Click -= ok; LanguagesButton.IsEnabled = false; from = From; to = To; });
            if (!yes) return null;
            _from = from!;
            return MpaiJson.ToJson(BasicSelectorObject.Languages(from!, to!));
        });

        // THE PERSON'S SPEECH, from the microphone, in the language the person said they speak.
        devices.RegisterAcquire(BSO, async (_, _, abandon) =>
        {
            await TellOnceAsync("Speak, or type and press Enter.");
            var speech = await _ears!.ListenAsync(stop, abandon);
            if (speech is null) return null;
            _heard = "(spoken)";
            return MpaiJson.ToJson(WithLanguage(speech, _from));
        });

        // THE PERSON'S TYPED TEXT: the box opens, and Enter - the key or the button - gives it.
        devices.RegisterAcquire(BTO, async (_, _, abandon) =>
        {
            var words = await _typed.AskAsync(stop, abandon);
            if (words is null) return null;
            _heard = words;
            return MpaiJson.ToJson(WordsIn(words, _from));
        });

        // THE SCREEN: the translation is shown; a prompt, if the workflow has one, is shown and said.
        devices.RegisterPresent("screen", async data =>
        {
            if (data.TryGetValue("Prompt", out var prompt)) { await TellAsync(prompt); return; }
            foreach (var kv in data)
            {
                if (!kv.Key.StartsWith("Display:OSD-BTO", StringComparison.OrdinalIgnoreCase)) continue;
                var translation = TextOf(kv.Value) ?? "";
                Dispatcher.Invoke(() =>
                {
                    Transcript.AppendText($"{_heard}{Environment.NewLine}  =>  {translation}{Environment.NewLine}{Environment.NewLine}");
                    Transcript.ScrollToEnd();
                });
            }
        });

        // THE LOUDSPEAKER: the translated Speech Object is played (the avatar's voice is the Module's speech); the step ends when it has been played.
        devices.RegisterPresent("speech", async data =>
        {
            if (!data.TryGetValue(BSO, out var spoken)) return;
            _told = false;   // the next turn tells again what to do
            BasicSpeechObject? speech = null;
            try { speech = MpaiJson.FromJson<BasicSpeechObject>(spoken); } catch { }
            if (speech?.Data is not { Length: > 0 } wav) { SetStatus("the translated Speech Object carries no sound."); return; }
            SetStatus("done.");
            await StandardDevices.PresentAsync(_avatar!, new AvatarUtterance(wav, null));
        });

        return devices;
    }

    private static string? TextOf(string? json)
    { if (string.IsNullOrWhiteSpace(json)) return null; try { return MpaiJson.FromJson<BasicTextObject>(json)?.GetText(); } catch { return null; } }

    private static BasicTextObject WordsIn(string words, string language) =>
        BasicTextObject.FromText(words, new TextQualifier
        {
            TextQualifierID = Guid.NewGuid().ToString(),
            Attributes = new TextAttributes { Language = new Language { LanguageCode = language, LanguageFormat = LanguageFormat.Iso639_1 } }
        });

    // The captured speech, with the language the person says they speak in its Qualifier.
    private static BasicSpeechObject WithLanguage(BasicSpeechObject speech, string language)
    {
        var captured = speech.SpeechQualifier;
        var qualifier = new SpeechQualifier
        {
            SpeechQualifierID = Guid.NewGuid().ToString(),
            MInstanceID       = captured?.MInstanceID,
            UEnvironmentID    = captured?.UEnvironmentID,
            SubType           = captured?.SubType,
            Format            = captured?.Format,
            Attributes = new SpeechAttributes
            {
                Source                = captured?.Attributes?.Source,
                SpeechCharacteristics = captured?.Attributes?.SpeechCharacteristics,
                Structure             = captured?.Attributes?.Structure,
                Device                = captured?.Attributes?.Device,
                Metadata = new SpeechMetadata
                {
                    SpeakerProperties = captured?.Attributes?.Metadata?.SpeakerProperties,
                    Language = new Language { LanguageCode = language, LanguageFormat = LanguageFormat.Iso639_1 }
                }
            }
        };
        return BasicSpeechObject.FromData(speech.Data, qualifier);
    }
}
