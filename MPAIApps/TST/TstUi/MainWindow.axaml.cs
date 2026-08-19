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

    // Remote mode: the AIWs run on an MPAI-MAS server and this window becomes a
    // Remote Client Application, holding the microphone, the loudspeaker and
    // nothing else. Both are null when running locally.
    private Mpai.Mas.Rca.TstMasBackend? _mas;
    private LocalAudio?                 _audio;
    private byte[]?                     _lastSpokenAnswer;

    // What the last exchange actually asked for. Shown with the result, because
    // a translation that looks wrong is often a translation of the wrong thing,
    // asked in the wrong language - and nothing in the window said which.
    private string _lastRequest = string.Empty;

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
        Console.SetOut(new PaneWriter(AppendLog));

        var config = TstConfig.Load();

        // Needed in either mode now: the User Agent holds the microphone and the
        // loudspeaker whether the AIF is in this process or on a server.
        _audio = new LocalAudio();

        if (!string.IsNullOrWhiteSpace(config.MasServerUrl))
        {
            if (await StartRemoteAsync(config)) return;

            // The server named in the configuration did not answer. Falling back
            // to local is better than leaving a window whose language lists are
            // empty and whose buttons do nothing, which is what happened before:
            // an unreachable server produced a UI that could not even be used to
            // find out why.
            Console.WriteLine("[UA] falling back to local: everything runs in this process.");
        }

        await StartLocalAsync();
    }

    private async Task StartLocalAsync()
    {
        SetBusy(true, "loading models...");

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

    // Remote: no models load here, so this is quick. What takes the time is the
    // SERVER starting the AIW, which happens on the first request rather than
    // now - so "ready" here means "the server answered", not "the server has
    // loaded Whisper".
    private async Task<bool> StartRemoteAsync(TstConfig config)
    {
        SetBusy(true, $"connecting to {config.MasServerUrl} ...");

        var backend = new Mpai.Mas.Rca.TstMasBackend(config.MasServerUrl);

        try
        {
            await backend.PrepareAsync();
            _mas = backend;
        }
        catch (Exception failure)
        {
            Console.WriteLine($"[UA] the MAS server did not answer: {failure.Message}");
            return false;
        }

        // Extended the same way the local list is, so the two modes offer the
        // same languages. A Remote Client Application cannot see the server's
        // voices - and should not, since what a server can speak is its own
        // business - so its list starts from the configuration; but taking it
        // verbatim meant es appeared standalone and not remotely, which is a
        // difference with no reason behind it.
        var offered = Common(config.Languages);

        SourceLanguage.ItemsSource  = new List<string>(offered);
        TargetLanguage.ItemsSource  = new List<string>(offered);
        SourceLanguage.SelectedItem = "en";
        TargetLanguage.SelectedItem = offered.Contains("it") ? "it" : "en";

        VoiceNote.Text = "remote: " + config.MasServerUrl;

        SetBusy(false, "ready (remote)");
        return true;
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
        if (_mas is not null)
        {
            try { _mas.StopAsync().GetAwaiter().GetResult(); } catch { }
            _mas.Dispose();
            _mas = null;
        }

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

        HeardBox.Text = string.Empty;
        _lastRequest  = $"{Code(SourceLanguage)} to {Code(TargetLanguage)}, typed";
        SetBusy(true, "translating...");

        if (_mas is not null)
        {
            await ShowRemoteAsync(() => _mas.TranslateTextAsync(typed, Code(SourceLanguage), Code(TargetLanguage)));
            return;
        }

        var boundary = Boundary();
        boundary["InputText"] = MpaiJson.ToJson(BasicTextObject.FromText(typed));

        var completed = await Task.Run(() => Run(boundary));
        Show(completed, spoken: false);
    }

    private async Task SpeakAsync()
    {
        // ONE path for both modes.
        //
        // The microphone belongs to the User Agent now, in both. MMC-SOA is no
        // longer a SubAIM of MMC-TST: acquisition interacts with the user
        // directly, so it travels with the User Agent when it becomes a Remote
        // Client Application - which is the test of whether it was ever part of
        // the composite.
        //
        // What this replaces, locally, was an EMPTY Speech Object sent as a
        // trigger meaning "MMC-SOA, acquire", with press-to-stop implemented by
        // PAUSING the running AIW. Both are gone: there is nothing inside the
        // composite to trigger or to pause. The remote path already worked this
        // way, so the two have converged rather than one being rewritten.
        if (_audio is null) return;

        if (!_recording)
        {
            _recording          = true;
            SpeakButton.Content = "Stop";
            TranslateButton.IsEnabled = false;
            _lastHeard     = string.Empty;
            HeardBox.Text  = string.Empty;
            OutputBox.Text = string.Empty;
            SetStatus("recording - press Stop when you have finished");

            _audio.StartRecording();
            return;
        }

        _recording          = false;
        SpeakButton.Content = "Speak";
        _lastRequest        = $"{Code(SourceLanguage)} to {Code(TargetLanguage)}, spoken";
        SetBusy(true, _mas is not null ? "sending to the server..." : "translating...");

        var wav = await _audio.StopRecordingAsync();
        Console.WriteLine($"[UA] captured {wav.Length:N0} bytes");

        if (_mas is not null)
        {
            await ShowRemoteAsync(() =>
                _mas.TranslateSpeechAsync(wav, Code(SourceLanguage), Code(TargetLanguage)));

            TranslateButton.IsEnabled = true;
            return;
        }

        if (_ua is null) return;

        // The Speech Qualifier carries the source language, which is how MMC-ASR
        // knows what to expect. That has not changed - only who fills it in.
        var boundary = Boundary();
        boundary["InputSpeech"] = MpaiJson.ToJson(
            BasicSpeechObject.FromData(
                wav,
                new SpeechQualifier
                {
                    SpeechQualifierID = Guid.NewGuid().ToString(),
                    Attributes = new SpeechAttributes
                    {
                        Source = SpeechSource.Real,
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

        var completed = await Task.Run(() => Run(boundary));

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

    // One place where a remote answer is displayed and played, so the typed and
    // spoken paths cannot drift apart.
    private async Task ShowRemoteAsync(Func<Task<Mpai.Mas.Rca.TstMasBackend.Translation>> exchange)
    {
        try
        {
            var translation = await exchange();

            OutputBox.Text    = translation.Text;
            _lastSpokenAnswer = translation.Speech;

            SetBusy(false, translation.Speech is null
                ? $"{_lastRequest} - no speech came back"
                : $"{_lastRequest} - remote");

            // Played HERE: the server's MMC-SOD delivered to a file on the
            // server, where nobody is listening.
            if (translation.Speech is not null && _audio is not null)
            {
                await _audio.PlayAsync(translation.Speech);
            }
        }
        catch (Exception failure)
        {
            Console.WriteLine($"[UA] the exchange failed: {failure.Message}");
            SetBusy(false, "the server did not answer - see the log");
        }
    }

    private void Show(AIF.Controller.Message? completed, bool spoken)
    {
        SetBusy(false, _lastRequest.Length > 0 ? _lastRequest : "ready");

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

        // Delivery is the User Agent's too. MMC-TTS writes to the boundary and
        // the loudspeaker is here - the same arrangement the remote path has
        // always had, where the server could not have played it anyway.
        if (completed.Ports.TryGetValue("OutputSpeech", out var speechJson) && _audio is not null)
        {
            var speech = MpaiJson.FromJson<BasicSpeechObject>(speechJson);

            if (speech.Data is { Length: > 0 })
            {
                _ = _audio.PlayAsync(speech.Data);
            }
            else
            {
                Console.WriteLine("[UA] no speech came back - the language may have no voice.");
            }
        }
    }

    // ---- plumbing --------------------------------------------------------

    private void SetBusy(bool busy, string status)
    {
        var live = _ua is not null || _mas is not null;

        TranslateButton.IsEnabled = !busy && live;
        SpeakButton.IsEnabled     = (!busy || _recording) && live;
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