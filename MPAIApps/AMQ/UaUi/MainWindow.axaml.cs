using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using AIF.Controller;
using AIF.Store;

using Mpai.Core;

namespace UaUi;

public partial class MainWindow : Window
{
    // Paths come from ua-config.json (next to the exe); falls back to D:\AI
    // defaults if the config is absent. One edit (MpaiRoot) relocates everything.
    private static readonly UaConfig Config = UaConfig.Load();
    private static readonly string AmdRepository = Config.AmdRepository;
    private static readonly string SettingsFile  = Config.SettingsFile;
    private static readonly string OutputFolder  = Config.OutputFolder;

    private static readonly string[] ImageExtensions =
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff" };

    private UserAgent? _ua;
    private volatile bool _modelsReady;
    private UaProvider? _provider;
    private AmdStore? _store;
    private AimSettings? _settings;
    private Task? _loadTask;
    private string _folder = string.Empty;
    private string? _selectedFile;
    private readonly IAudioRecorder _recorder = new WindowsAudioRecorder();
    private readonly IAudioPlayer _player = new WindowsAudioPlayer();
    private byte[]? _recordedQuestion;
    private string? _lastAnswerWav;

    // RCA mode: the MAS backend, created lazily when MasServerUrl is configured.
    private Mpai.Mas.Rca.MasAmqBackend? _masBackend;

    public MainWindow()
    {
        InitializeComponent();

        BrowseButton.Click += async (_, _) => await BrowseFolderAsync();
        FileList.SelectionChanged += (_, _) => OnFileSelected();
        PickTyped.Click += (_, _) => OnTypedName();
        AskButton.Click += (_, _) => OnAskQuestion();
        AnswerButton.Click += async (_, _) => await GetAnswerAsync();
        QuestionBox.KeyDown += async (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter && AnswerButton.IsEnabled)
                await GetAnswerAsync();
        };

        // Model loading starts as soon as the app launches, in the background.
        // Until models are ready, disable folder/image controls so the user
        // can't act on a not-yet-ready system (which caused a double-entry).
        BrowseButton.IsEnabled = false;
        FolderBox.IsEnabled    = false;
        PickTyped.IsEnabled    = false;
        _loadTask = Task.Run(LoadModels);
    }

    private void Status(string message) =>
        Dispatcher.UIThread.Post(() => StatusText.Text = message);

    private static readonly string LogFile = Config.LogFile;
    private static void Log(string message)
    {
        try { File.AppendAllText(LogFile, DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine); }
        catch { }
    }

    // ── One-time model loading at launch ─────────────────────────────────────
    private void LoadModels()
    {
        try
        {
            Status("Loading models…");
            Log("[UA-UI] Loading models at launch…");

            var store = new AmdStore(AmdRepository);
            store.Scan();
            var settings = AimSettings.Load(SettingsFile);
            Directory.CreateDirectory(OutputFolder);

            _store    = store;
            _settings = settings;
            _ua = new UserAgent(store);
            _ua.MPAI_AIFU_Controller_Initialize();

            // One persistent provider caches the heavy BLIP model (and the ASR/
            // TTS cores). A fresh AIW per question reuses these cached models, so
            // each run is fast AND has clean state.
            _provider = new UaProvider(store, OutputFolder);

            // Warm the cache now: instantiate once so BLIP loads at launch.
            var err = _ua.MPAI_AIFU_AIW_Start("MMC-AMQ-V2.5", _provider, settings, out _);
            if (err != AifError.OK)
            {
                Status($"Model loading failed: {err}");
                return;
            }

            _modelsReady = true;
            Log("[UA-UI] Models loaded.");
            Dispatcher.UIThread.Post(() =>
            {
                StatusText.Text = "Models loaded. Choose a folder and select an image.";
                // System is now in a stable state: enable folder/image controls.
                BrowseButton.IsEnabled = true;
                FolderBox.IsEnabled    = true;
                PickTyped.IsEnabled    = true;
                RefreshAskButton();
            });
        }
        catch (Exception ex)
        {
            Log("[UA-UI] MODEL LOAD EXCEPTION: " + ex);
            Status("Model loading failed: " + ex.Message);
        }
    }

    // Ask question is enabled only when models are ready AND an image is selected.
    private void RefreshAskButton()
    {
        AskButton.IsEnabled = _modelsReady && _selectedFile is not null;
    }

    // Get answer is enabled once a question is active (and an image is selected).
    private void RefreshAnswerButton()
    {
        AnswerButton.IsEnabled = _modelsReady && _questionActive && _selectedFile is not null;
    }

    private bool _questionActive;

    // ── Folder selection + enumeration ───────────────────────────────────────
    private async Task BrowseFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the image folder",
            AllowMultiple = false
        });

        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (string.IsNullOrEmpty(path)) return;

        FolderBox.Text = path;
        LoadFolder(path);
    }

    private void LoadFolder(string path)
    {
        _folder = path;
        try
        {
            var files = Directory.EnumerateFiles(path)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Select(Path.GetFileName)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            FileList.ItemsSource = files;
            Status(_modelsReady ? $"{files.Count} images found. Select one."
                                : $"{files.Count} images found. (Models still loading…)");
        }
        catch (Exception ex)
        {
            Status($"Could not read folder: {ex.Message}");
        }
    }

    // ── Selection: display the image ─────────────────────────────────────────
    private void OnFileSelected()
    {
        if (FileList.SelectedItem is string name) SelectImage(name);
    }

    private void OnTypedName()
    {
        var name = TypedName.Text?.Trim();
        if (!string.IsNullOrEmpty(name)) SelectImage(name);
    }

    private void SelectImage(string fileName)
    {
        var full = Path.Combine(_folder, fileName);
        if (!File.Exists(full)) { Status($"Not found: {fileName}"); return; }

        try
        {
            using var stream = File.OpenRead(full);
            FrameImage.Source = new Bitmap(stream);
            FrameCaption.Text = fileName;
            _selectedFile = full;
        }
        catch (Exception ex)
        {
            Status($"Could not display {fileName}: {ex.Message}");
            return;
        }

        RefreshAskButton();
        Status(_modelsReady ? $"Selected {fileName}. Press Ask question."
                            : $"Selected {fileName}. (Ask question will enable once models finish loading.)");
    }

    // ── Ask question: start recording; text box is the typed alternative ─────
    private void OnAskQuestion()
    {
        if (_selectedFile is null) { Status("Select an image first."); return; }
        _questionActive = true;
        _recordedQuestion = null;
        QuestionBox.IsEnabled = true;
        QuestionBox.Text = string.Empty;
        QuestionBox.Focus();
        RefreshAnswerButton();

        try
        {
            _recorder.Start();
            Status("Recording… speak your question, then press Get answer. (Or type it instead.)");
        }
        catch (Exception ex)
        {
            Log("[UA-UI] RECORD START FAILED: " + ex);
            Status("Could not start recording — you can type the question. " + ex.Message);
        }
    }

    // ── Get answer: run the pipeline with the loaded models ──────────────────
    private async Task GetAnswerAsync()
    {
        if (!_modelsReady || _ua is null) { Status("Models not loaded yet."); return; }
        if (_selectedFile is null) { Status("Select an image first."); return; }

        // Stop recording if it is running; keep the captured audio.
        if (_recorder.IsRecording)
        {
            try { _recordedQuestion = _recorder.Stop(); }
            catch (Exception ex) { Log("[UA-UI] RECORD STOP FAILED: " + ex); }
        }

        var typed = QuestionBox.Text?.Trim() ?? string.Empty;
        bool useText = typed.Length > 0;
        if (!useText && (_recordedQuestion is null || _recordedQuestion.Length == 0))
        {
            Status("No question — type one, or press Ask question and speak.");
            return;
        }

        Status(useText ? "Answering your typed question…" : "Answering your spoken question…");
        AnswerButton.IsEnabled = false;
        var selected = _selectedFile;
        var ua = _ua;
        var question = typed;
        var audioQuestion = _recordedQuestion;

        // ── MAS mode: when a SCI URL is configured, act as a Remote Client
        // Application and answer via the remote SCI over the MPAI-MAS Remote API.
        // Otherwise fall through to the in-process path below (unchanged).
        if (!string.IsNullOrWhiteSpace(Config.MasServerUrl))
        {
            await AnswerViaMasAsync(selected!, useText, question, audioQuestion);
            return;
        }

        var (ok, msg, answer, frame) = await Task.Run(() =>
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                // Reuse the persistent store/settings/provider created ONCE at
                // launch. The provider caches the heavy BLIP model (and ASR/TTS
                // cores); creating a new provider here would discard that cache
                // and reload BLIP on every question — the main latency cause.
                var provider = _provider!;
                var e0 = ua.MPAI_AIFU_AIW_Start("MMC-AMQ-V2.5", provider, _settings!, out var aiwId);
                if (e0 != AifError.OK) return (false, $"Start failed: {e0}", (string?)null, (string?)null);
                Log($"[UA-UI] AIW_Start: {sw.ElapsedMilliseconds} ms");

                Log($"[UA-UI] Image={Path.GetFileName(selected)}  Question=\"{question}\"");

                var bytes = File.ReadAllBytes(selected!);
                var image = BasicVisualObject.FromFile(selected!, bytes);
                var (e1, o1) = ua.RunAsync(aiwId, new Dictionary<string, string>
                {
                    ["InputVisual"] = MpaiJson.ToJson(image)
                }).GetAwaiter().GetResult();
                Log($"[UA-UI] RunAsync(image): {sw.ElapsedMilliseconds} ms total");
                if (e1 != AifError.OK || o1 is null || !o1.Suspended)
                    return (false, "Pipeline did not reach the question step.", null, null);

                // TIQ has one text input, fed by EITHER the boundary InputText OR
                // ASR (from the speech branch). To avoid the two colliding:
                //  * TEXT mode: supply the typed InputText AND an empty InputSpeech.
                //    The empty speech lets SOA run without suspending; ASR yields
                //    no real text; TIQ uses the typed InputText.
                //  * VOICE mode: supply ONLY InputSpeech. ASR produces the text and
                //    feeds TIQ; the boundary InputText is left unset. The executor
                //    does not suspend for it because TIQ is satisfied internally
                //    by ASR (InternallySatisfied).
                Dictionary<string, string> questionPorts;
                if (useText)
                {
                    Log($"[UA-UI] TEXT question: \"{question}\"");
                    questionPorts = new Dictionary<string, string>
                    {
                        ["InputText"]   = MpaiJson.ToJson(BasicTextObject.FromText(question)),
                        ["InputSpeech"] = MpaiJson.ToJson(BasicSpeechObject.FromData(Array.Empty<byte>()))
                    };
                }
                else
                {
                    Log($"[UA-UI] SPEECH question: {audioQuestion!.Length} bytes");
                    questionPorts = new Dictionary<string, string>
                    {
                        ["InputSpeech"] = MpaiJson.ToJson(BasicSpeechObject.FromData(audioQuestion!))
                    };
                }

                Log($"[UA-UI] Resuming with ports: {string.Join(",", questionPorts.Keys)}");
                var (e2, o2) = ua.ResumeAsync(aiwId, questionPorts).GetAwaiter().GetResult();
                Log($"[UA-UI] ResumeAsync(answer): {sw.ElapsedMilliseconds} ms total");
                if (o2 is not null && o2.Completed is not null)
                    Log($"[UA-UI] Completed ports: {string.Join(",", o2.Completed.Ports.Keys)}");
                if (e2 != AifError.OK || o2 is null) return (false, $"Answer failed: {e2}", null, null);
                if (o2.Suspended) return (false, $"Still waiting for {o2.WaitingPort}.", null, null);

                string? answerText = null;
                if (o2.Completed is not null &&
                    o2.Completed.Ports.TryGetValue("OutputText", out var ansJson))
                {
                    try { answerText = MpaiJson.FromJson<BasicTextObject>(ansJson).GetText(); }
                    catch { answerText = "(answer produced)"; }
                }
                Log($"[UA-UI] TIQ answer = \"{answerText}\"");

                string? framePath = null;
                string? answerWav = null;
                try
                {
                    framePath = Directory.EnumerateFiles(OutputFolder)
                        .Where(f => { var e = Path.GetExtension(f).ToLowerInvariant();
                                      return e is ".png" or ".jpg" or ".jpeg" or ".bmp"; })
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
                    answerWav = Directory.EnumerateFiles(OutputFolder, "*.wav")
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
                }
                catch { }

                _lastAnswerWav = answerWav;
                return (true, answerText ?? "(no text answer)", answerText, framePath);
            }
            catch (Exception ex)
            {
                Log("[UA-UI] ANSWER EXCEPTION: " + ex);
                return (false, "Answer failed: " + ex.Message, (string?)null, (string?)null);
            }
        });

        Dispatcher.UIThread.Post(() =>
        {
            StatusText.Text = ok ? $"Answer: {answer}" : msg;
            if (ok && frame is not null && File.Exists(frame))
            {
                try { using var s = File.OpenRead(frame); FrameImage.Source = new Bitmap(s); FrameCaption.Text = "Answer frame"; }
                catch { }
            }
            if (ok && _lastAnswerWav is not null && File.Exists(_lastAnswerWav))
            {
                try { _player.PlayWav(File.ReadAllBytes(_lastAnswerWav)); }
                catch (Exception ex) { Log("[UA-UI] PLAY FAILED: " + ex); }
            }
            RefreshAnswerButton();
        });
    }

    // ── RCA mode: answer via the remote SCI over the MPAI-MAS Remote API ──────
    private async Task AnswerViaMasAsync(
        string imagePath, bool useText, string question, byte[]? audioQuestion)
    {
        try
        {
            // Lazily create + prepare the MAS backend (create SCI, start AIW).
            if (_masBackend is null)
            {
                Status("Connecting to MPAI-MAS service…");
                _masBackend = new Mpai.Mas.Rca.MasAmqBackend(Config.MasServerUrl);
                await _masBackend.PrepareAsync();
            }

            var image = BasicVisualObject.FromFile(imagePath, File.ReadAllBytes(imagePath));
            BasicTextObject?  qText  = useText ? BasicTextObject.FromText(question) : null;
            BasicAudioObject? qAudio = useText ? null
                                               : BasicAudioObject.FromData(audioQuestion!);

            Log($"[UA-UI] MAS ask ({(useText ? "text" : "audio")}) via {Config.MasServerUrl}");
            var result = await _masBackend.AskAsync(image, qText, qAudio);
            Log($"[UA-UI] MAS answer = \"{result.Text}\"");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusText.Text = $"Answer: {result.Text}";
                if (result.FrameBytes is { Length: > 0 })
                {
                    try { using var s = new MemoryStream(result.FrameBytes);
                          FrameImage.Source = new Bitmap(s); FrameCaption.Text = "Answer frame"; }
                    catch { }
                }
                if (result.SpokenWav is { Length: > 0 })
                {
                    try { _player.PlayWav(result.SpokenWav); }
                    catch (Exception ex) { Log("[UA-UI] MAS PLAY FAILED: " + ex); }
                }
                RefreshAnswerButton();
            });
        }
        catch (Exception ex)
        {
            Log("[UA-UI] MAS ANSWER EXCEPTION: " + ex);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusText.Text = "MAS answer failed: " + ex.Message;
                RefreshAnswerButton();
            });
        }
    }
}
