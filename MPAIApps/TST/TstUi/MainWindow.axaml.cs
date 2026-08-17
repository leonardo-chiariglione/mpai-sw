using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;

namespace TstUi;

// MMC-TST-V2.5 through a portable window.
//
// The User Agent starts both AIWs once and thereafter only writes boundary ports
// and reads what comes back. Acquisition and delivery are SubAIMs, so this window
// touches no audio device - which is also what makes it portable: the only
// platform-specific code in the whole app is the two device choices in
// TstProvider.
//
// The AIMs' console output is redirected into the log pane, so what MMC-ASR
// heard and why a voice failed appear here without any AIM knowing a UI exists.
public partial class MainWindow : Window
{
    private const string PromptAiw = "UAG-SPK-V1.0";
    private const string TstAiw    = "MMC-TST-V2.5";

    private UserAgent? _ua;
    private int        _promptAiwId;
    private int        _tstAiwId;
    private bool       _recording;
    private string     _lastHeard = string.Empty;

    public MainWindow()
    {
        // InitializeComponent() is GENERATED from MainWindow.axaml, and it does
        // two things: it loads the XAML and it assigns the x:Name fields -
        // TranslateButton, OutputBox and the rest. Declaring a hand-written one
        // that only called AvaloniaXamlLoader.Load displaced the generated
        // version, so the tree was built but every field stayed null and the
        // first line of the constructor to touch one threw. UaUi does not declare
        // it either; that is the convention here.
        InitializeComponent();

        TranslateButton.Click += async (_, _) => await TranslateTypedAsync();
        SpeakButton.Click     += async (_, _) => await SpeakAsync();

        // async void: an exception here would otherwise vanish and the window
        // would close without a word.
        Opened += async (_, _) =>
        {
            try   { await StartAsync(); }
            catch (Exception failure)
            {
                Program.Record("window startup", failure);
                SetBusy(false, $"startup failed: {failure.Message}  (see {Program.CrashLog})");
            }
        };
        Closed += (_, _) => Shutdown();
    }

    // ---- lifecycle -------------------------------------------------------

    private async Task StartAsync()
    {
        SetBusy(true, "loading models...");

        Console.SetOut(new PaneWriter(AppendLog));

        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            SetBusy(false, "could not find AIMs/AMDs above the executable");
            return;
        }

        var outcome = await Task.Run(() =>
        {
            var store = new AmdStore(Path.Combine(repoRoot, "AIMs", "AMDs"));
            store.Scan();

            var settings = AimSettings.Load(Path.Combine(repoRoot, "AIMs", "aim-settings.json"));
            var provider = new TstProvider(store);
            var ua       = new UserAgent(store);

            ua.MPAI_AIFU_Controller_Initialize();

            if (ua.MPAI_AIFU_AIW_Start(PromptAiw, provider, settings, out var promptId) != AifError.OK)
                return (Ua: (UserAgent?)null, PromptId: 0, TstId: 0, Voices: Voices(settings));

            if (ua.MPAI_AIFU_AIW_Start(TstAiw, provider, settings, out var tstId) != AifError.OK)
            {
                ua.MPAI_AIFU_AIW_Stop(promptId);
                return (Ua: (UserAgent?)null, PromptId: 0, TstId: 0, Voices: Voices(settings));
            }

            return (Ua: (UserAgent?)ua, PromptId: promptId, TstId: tstId, Voices: Voices(settings));
        });

        _ua          = outcome.Ua;
        _promptAiwId = outcome.PromptId;
        _tstAiwId    = outcome.TstId;

        // Translation covers 100 languages; only SPEAKING needs a voice. The
        // lists therefore hold every language, and the ones with a voice are
        // marked, rather than the others being hidden.
        var spoken = outcome.Voices;

        SourceLanguage.ItemsSource = Common(spoken);
        TargetLanguage.ItemsSource = Common(spoken);
        SourceLanguage.SelectedItem = "en";
        TargetLanguage.SelectedItem = spoken.Contains("it") ? "it" : "en";

        VoiceNote.Text = spoken.Count > 0
            ? "voices: " + string.Join(", ", spoken)
            : "no voices configured - text only";

        if (_ua is null)
        {
            SetBusy(false, "the AIWs would not start - see the log");
            return;
        }

        SetBusy(false, "ready");
    }

    private static List<string> Voices(AimSettings settings)
    {
        var codes = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var key in settings.For("MMC-TTS-V2.5").Keys)
        {
            if (key.StartsWith("Voice:", StringComparison.OrdinalIgnoreCase))
                codes.Add(key.Substring("Voice:".Length).ToLowerInvariant());
        }

        return codes.ToList();
    }

    // The languages worth offering first: the ones that can be spoken here, then
    // a few common others that will translate but stay silent.
    private static List<string> Common(IEnumerable<string> spoken)
    {
        var codes = new List<string>(spoken);

        foreach (var code in new[] { "en", "es", "pt", "nl", "pl", "ru", "ar", "ko" })
        {
            if (!codes.Contains(code)) codes.Add(code);
        }

        return codes;
    }

    private void Shutdown()
    {
        if (_ua is null) return;

        try
        {
            _ua.MPAI_AIFU_AIW_Stop(_tstAiwId);
            _ua.MPAI_AIFU_AIW_Stop(_promptAiwId);
        }
        catch { /* closing anyway */ }
    }

    // ---- the two actions -------------------------------------------------

    private async Task TranslateTypedAsync()
    {
        var typed = (InputBox.Text ?? string.Empty).Trim();
        if (typed.Length == 0) { SetStatus("type something first"); return; }

        var boundary = Boundary();
        boundary["InputText"] = MpaiJson.ToJson(BasicTextObject.FromText(typed));

        HeardBox.Text = string.Empty;
        SetBusy(true, "translating...");

        var completed = await Task.Run(() => Run(boundary));
        Show(completed, spoken: false);
    }

    private async Task SpeakAsync()
    {
        if (_ua is null) return;

        // The second press is "that is enough". Pause reaches MMC-SOA, which
        // closes the microphone; the Resume immediately after lets the rest of
        // the pipeline run. Stop would end the AIW and discard the recording.
        if (_recording)
        {
            _ua.MPAI_AIFU_AIW_Pause(_tstAiwId);
            _ua.MPAI_AIFU_AIW_Resume(_tstAiwId);
            SpeakButton.Content = "Speak";
            SetStatus("finishing...");
            return;
        }

        var boundary = Boundary();

        // An EMPTY Speech Object asks MMC-SOA to acquire; its Qualifier carries
        // the source language, which is how MMC-ASR learns what to expect.
        boundary["InputSpeech"] = MpaiJson.ToJson(
            BasicSpeechObject.FromData(
                Array.Empty<byte>(),
                new SpeechQualifier
                {
                    SpeechQualifierID = Guid.NewGuid().ToString(),
                    Attributes = new SpeechAttributes
                    {
                        Metadata = new SpeechMetadata
                        {
                            Language = new Language
                            {
                                LanguageCode   = Code(SourceLanguage),
                                LanguageFormat = LanguageFormat.Iso639_1
                            }
                        }
                    }
                }));

        _recording          = true;
        _lastHeard          = string.Empty;
        HeardBox.Text       = string.Empty;
        OutputBox.Text      = string.Empty;
        SpeakButton.Content = "Stop";
        TranslateButton.IsEnabled = false;
        SetStatus("recording - press Stop when you have finished");

        var completed = await Task.Run(() => Run(boundary));

        _recording          = false;
        SpeakButton.Content = "Speak";
        TranslateButton.IsEnabled = true;

        Show(completed, spoken: true);
    }

    private Dictionary<string, string> Boundary() => new()
    {
        ["LanguageSelector"] = MpaiJson.ToJson(
            BasicSelectorObject.Languages(Code(SourceLanguage), Code(TargetLanguage)))
    };

    private static string Code(ComboBox box)
    {
        var text = (box.SelectedItem as string ?? "en").Trim().ToLowerInvariant();
        return text.Length >= 2 ? text.Substring(0, 2) : "en";
    }

    private AIF.Controller.Message? Run(Dictionary<string, string> boundary)
    {
        if (_ua is null) return null;

        var (error, outcome) = _ua.RunAsync(_tstAiwId, boundary).GetAwaiter().GetResult();

        if (error != AifError.OK || outcome?.Completed is null)
        {
            Console.WriteLine($"[UA] run failed: {error}");
            return null;
        }

        if (outcome.Completed.IsError)
        {
            Console.WriteLine($"[UA] {outcome.Completed.FailedAim}: {outcome.Completed.Payload}");
            return null;
        }

        return outcome.Completed;
    }

    private void Show(AIF.Controller.Message? completed, bool spoken)
    {
        SetBusy(false, "ready");

        if (spoken) HeardBox.Text = _lastHeard;

        if (completed is null)
        {
            SetStatus("nothing came back - see the log");
            return;
        }

        if (completed.Ports.TryGetValue("OutputText", out var textJson))
        {
            OutputBox.Text = MpaiJson.FromJson<BasicTextObject>(textJson).GetText();
        }
        else
        {
            OutputBox.Text = string.Empty;
            SetStatus("no translation was produced");
        }
    }

    // ---- plumbing --------------------------------------------------------

    private void SetBusy(bool busy, string status)
    {
        TranslateButton.IsEnabled = !busy && _ua is not null;
        SpeakButton.IsEnabled     = (!busy || _recording) && _ua is not null;
        SetStatus(status);
    }

    private void SetStatus(string status) => StatusText.Text = status;

    // Every line the AIMs print. MMC-ASR's is lifted out as it passes, so the
    // recognised text can sit beside the translation instead of being hunted
    // for in a log.
    private void AppendLog(string line)
    {
        const string marker = "[MMC-ASR-V2.5] heard:";

        if (line.Contains(marker, StringComparison.Ordinal))
        {
            _lastHeard = line.Substring(line.IndexOf(marker, StringComparison.Ordinal) + marker.Length).Trim();
        }

        Dispatcher.UIThread.Post(() =>
        {
            LogBox.Text += line + Environment.NewLine;
            LogScroller.ScrollToEnd();
        });
    }

    private static string? FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "AIMs", "AMDs")))
                return directory.FullName;
            directory = directory.Parent;
        }
        return null;
    }

    // Console.Out, line by line, into a callback. The AIMs write to the console
    // exactly as they always have; the User Agent decides where that lands.
    private sealed class PaneWriter : TextWriter
    {
        private readonly Action<string> _line;
        private readonly StringBuilder  _pending = new();

        public PaneWriter(Action<string> line) => _line = line;

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n') { Flush(); return; }
            if (value != '\r') _pending.Append(value);
        }

        public override void Write(string? value)
        {
            if (value is null) return;
            foreach (var character in value) Write(character);
        }

        public override void Flush()
        {
            if (_pending.Length == 0) return;

            var line = _pending.ToString();
            _pending.Clear();
            _line(line);
        }
    }
}