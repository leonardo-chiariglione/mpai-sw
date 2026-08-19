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

        var (ok, msg, answer, frameBytes, answerWav) = await Task.Run(() =>
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
                if (e0 != AifError.OK)
                    return (false, $"Start failed: {e0}", (string?)null, (byte[]?)null, (byte[]?)null);
                Log($"[UA-UI] AIW_Start: {sw.ElapsedMilliseconds} ms");

                Log($"[UA-UI] Image={Path.GetFileName(selected)}  Question=\"{question}\"");

                // ONE run, image and question together.
                //
                // This used to be two: RunAsync(image), which suspended, then
                // ResumeAsync(question). The suspension existed so the AIW could
                // ASK for the question - but the workflow shows the user a FRAME
                // first, built by TIQ, and an AIM appears once in a Topology.
                // Showing an image and inviting a question are user-facing acts,
                // so they belong here, before the AIW is started at all.
                //
                // That also disposes of the worst hack in this file: text mode
                // used to send an EMPTY Speech Object alongside the typed
                // question, so that MMC-SOA would run without suspending. With
                // acquisition gone from the composite, that empty object would
                // reach MMC-ASR directly and Whisper would invent a sentence
                // from silence. Text mode now simply omits InputSpeech, and the
                // executor skips MMC-ASR because the Port is optional.
                var bytes = File.ReadAllBytes(selected!);
                var image = BasicVisualObject.FromFile(selected!, bytes);

                var ports = new Dictionary<string, string>
                {
                    ["InputVisual"] = MpaiJson.ToJson(image)
                };

                if (useText)
                {
                    Log($"[UA-UI] TEXT question: \"{question}\"");
                    ports["InputText"] = MpaiJson.ToJson(BasicTextObject.FromText(question));
                }
                else
                {
                    Log($"[UA-UI] SPEECH question: {audioQuestion!.Length} bytes");
                    ports["InputSpeech"] = MpaiJson.ToJson(BasicSpeechObject.FromData(audioQuestion!));
                }

                Log($"[UA-UI] Running with ports: {string.Join(",", ports.Keys)}");
                var (e1, o1) = ua.RunAsync(aiwId, ports).GetAwaiter().GetResult();
                Log($"[UA-UI] RunAsync: {sw.ElapsedMilliseconds} ms total");

                if (e1 != AifError.OK || o1 is null)
                    return (false, $"Answer failed: {e1}", (string?)null, (byte[]?)null, (byte[]?)null);

                if (o1.Suspended)
                    return (false, $"Unexpectedly waiting for {o1.WaitingPort}.", (string?)null, (byte[]?)null, (byte[]?)null);

                if (o1.Completed is null)
                    return (false, "No result.", (string?)null, (byte[]?)null, (byte[]?)null);

                Log($"[UA-UI] Completed ports: {string.Join(",", o1.Completed.Ports.Keys)}");

                if (o1.Completed.IsError)
                    return (false, $"{o1.Completed.FailedAim}: {o1.Completed.Payload}",
                            (string?)null, (byte[]?)null, (byte[]?)null);

                string? answerText = null;
                if (o1.Completed.Ports.TryGetValue("OutputText", out var ansJson))
                {
                    try { answerText = MpaiJson.FromJson<BasicTextObject>(ansJson).GetText(); }
                    catch { answerText = "(answer produced)"; }
                }
                Log($"[UA-UI] TIQ answer = \"{answerText}\"");

                // The frame and the spoken answer come back ON THE BOUNDARY
                // PORTS. They used to be found by scanning the output folder for
                // the newest .wav and image, because CVE-VOD and MMC-SOD wrote
                // them there - which is delivery done by AIMs that had no
                // business doing it, and a race whenever two runs overlapped.
                byte[]? frame = null;
                if (o1.Completed.Ports.TryGetValue("OutputVisual", out var frameJson))
                {
                    try { frame = MpaiJson.FromJson<BasicVisualObject>(frameJson).Data; }
                    catch (Exception ex) { Log("[UA-UI] frame decode failed: " + ex.Message); }
                }

                byte[]? spoken = null;
                if (o1.Completed.Ports.TryGetValue("OutputSpeech", out var speechJson))
                {
                    try { spoken = MpaiJson.FromJson<BasicSpeechObject>(speechJson).Data; }
                    catch (Exception ex) { Log("[UA-UI] speech decode failed: " + ex.Message); }
                }

                return (true, answerText ?? "(no text answer)", answerText, frame, spoken);
            }
            catch (Exception ex)
            {
                Log("[UA-UI] ANSWER EXCEPTION: " + ex);
                return (false, "Answer failed: " + ex.Message, (string?)null, (byte[]?)null, (byte[]?)null);
            }
        });

        Dispatcher.UIThread.Post(() =>
        {
            StatusText.Text = ok ? $"Answer: {answer}" : msg;

            if (ok && frameBytes is { Length: > 0 })
            {
                try { using var s = new MemoryStream(frameBytes);
                      FrameImage.Source = new Bitmap(s); FrameCaption.Text = "Answer frame"; }
                catch (Exception ex) { Log("[UA-UI] FRAME FAILED: " + ex); }
            }

            // Playback is the User Agent's: MMC-TTS writes to the boundary and
            // the loudspeaker is here.
            if (ok && answerWav is { Length: > 0 })
            {
                try { _player.PlayWav(answerWav); }
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