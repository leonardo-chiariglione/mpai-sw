using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

using AIF.Controller;
using AIF.Store;
using AIF.GlobalStorage;

using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Gallery;
using Mpai.Aims.Visual;   // WebcamVisualAcquisition

namespace CavMac;

// CAV-MAC V2.0 - Multimodal Access Control, through a window.
//
// The app TELLS the user when to look and when to speak (shown and spoken), and
// captures each modality LIVE at a moment the user controls - the webcam frame
// when they press Capture, the mic between Speak and Stop. Each capture is
// described through the Controller (PAF-EFD / MMC-ESD), matched against the
// gallery enrolment populated, and the two identities reconciled through HCI-IDR
// into a GRANT or DENY.
//
// Each AIW is started, run and stopped PER OPERATION - the same pattern the proven
// access-control host uses (Initialize + AIW_Start + Run + AIW_Stop). Prompts are
// spoken through the UAG-SPK prompt AIW the same way.
public partial class MainWindow : Window
{
    private const string EfdAiw    = "UAG-EFD-V1.0";
    private const string EsdAiw    = "UAG-ESD-V1.0";
    private const string IdrAiw    = "UAG-IDR-V1.0";
    private const string PromptAiw = "UAG-SPK-V1.0";

    private const float FaceThreshold    = 0.35f;
    private const float SpeakerThreshold = 0.45f;

    private UserAgent?         _ua;
    private CavMacProvider?    _provider;
    private AimSettings?       _settings;
    private SubjectRepository? _gallery;
    private LocalAudio?        _audio;

    private (string SubjectId, float Similarity)? _faceMatch;
    private (string SubjectId, float Similarity)? _speakerMatch;
    private bool _recording;
    private readonly object _uaLock = new();   // serialise all UserAgent access

    public MainWindow()
    {
        InitializeComponent();

        AuthenticateButton.Click += (_, _) => StartAuthentication();
        CaptureFaceButton.Click  += async (_, _) => await CaptureFaceAsync();
        RecordButton.Click       += async (_, _) => await RecordVoiceAsync();

        Opened += async (_, _) =>
        {
            try   { await StartAsync(); }
            catch (Exception failure)
            {
                Program.Record("window startup", failure);
                SetStatus($"startup failed: {failure.Message}  (see {Program.CrashLog})");
            }
        };
    }

    // ---- lifecycle --------------------------------------------------------

    private async Task StartAsync()
    {
        Console.SetOut(new PaneWriter(AppendLog));
        SetStatus("loading models...");

        var repoRoot = FindRepoRoot();
        if (repoRoot is null) { SetStatus("could not find AIMs/AMDs above the executable"); return; }

        _audio = new LocalAudio();

        var ok = await Task.Run(() =>
        {
            var store = new AmdStore(Path.Combine(repoRoot, "AIMs", "AMDs"));
            store.Scan();

            _settings = AimSettings.Load(Path.Combine(repoRoot, "AIMs", "aim-settings.json"));
            _provider = new CavMacProvider(store);
            _ua       = new UserAgent(store);

            var storage = new FileGlobalStorage(
                Path.Combine(repoRoot, "TestData", "gallery-store"), topAim: "CAV-MAC");
            _gallery = new SubjectRepository(storage);
            return true;
        });

        SetStatus(ok ? "Ready. Press Authenticate to begin." : "startup failed - see the log");
        AuthenticateButton.IsEnabled = ok;
    }

    // ---- the choreography -------------------------------------------------

    private async void StartAuthentication()
    {
        _faceMatch = null;
        _speakerMatch = null;
        ResultBorder.IsVisible = false;

        FaceStatus.Text  = "waiting";
        VoiceStatus.Text = "waiting";
        CaptureFaceButton.IsEnabled = true;
        RecordButton.IsEnabled = true;

        InstructionText.Text = "Step 1: look at the camera and press Capture.";
        SetStatus("awaiting face capture");
        await SpeakAsync("Look at the camera.");
    }

    private async Task CaptureFaceAsync()
    {
        CaptureFaceButton.IsEnabled = false;
        FaceStatus.Text = "capturing...";
        SetStatus("capturing face");

        var match = await Task.Run(() =>
        {
            var frame = new WebcamVisualAcquisition()
                .AcquireAsync(new VisualAcquisitionRequest()).GetAwaiter().GetResult();
            var fdo = DescribeFace(frame.Data);
            return fdo?.Embedding() is { } e ? _gallery!.MatchFace(e, FaceThreshold) : null;
        });

        _faceMatch = match;
        FaceStatus.Text = match is { } m ? $"matched {m.SubjectId} ({m.Similarity:F2})" : "no match";
        InstructionText.Text = "Step 2: press Speak, say your passphrase, then press Stop.";
        SetStatus("awaiting voice");

        // EFD has completed and returned; the UA now activates speech, in sequence.
        await SpeakAsync("Please speak your passphrase.");
        await MaybeReconcileAsync();
    }

    private async Task RecordVoiceAsync()
    {
        if (_audio is null) return;

        if (!_recording)
        {
            _recording = true;
            RecordButton.Content = "Stop";
            VoiceStatus.Text = "recording... press Stop when finished";
            _audio.StartRecording();
            SetStatus("recording");
            return;
        }

        _recording = false;
        RecordButton.Content = "Speak your passphrase";
        RecordButton.IsEnabled = false;
        VoiceStatus.Text = "processing...";
        SetStatus("describing voice");

        var wav = await _audio.StopRecordingAsync();
        Console.WriteLine($"[UA] captured {wav.Length:N0} bytes of speech");

        var match = await Task.Run(() =>
        {
            var sdo = DescribeSpeech(wav);
            return sdo?.Embedding() is { } e ? _gallery!.MatchSpeech(e, SpeakerThreshold) : null;
        });

        _speakerMatch = match;
        VoiceStatus.Text = match is { } m ? $"matched {m.SubjectId} ({m.Similarity:F2})" : "no match";
        await MaybeReconcileAsync();
    }

    private async Task MaybeReconcileAsync()
    {
        if (_faceMatch is null && _speakerMatch is null) return;
        if (CaptureFaceButton.IsEnabled || RecordButton.IsEnabled) return;

        SetStatus("reconciling identities");

        var decision = await Task.Run(() =>
        {
            var faceId = _faceMatch is { } f ? FaceIdentity(f.SubjectId, f.Similarity) : null;
            var spkId  = _speakerMatch is { } s ? SpeakerIdentity(s.SubjectId, s.Similarity) : null;
            var reconciled = Reconcile(faceId, spkId);
            return Decide(reconciled);
        });

        await ShowDecisionAsync(decision);
    }

    private async Task ShowDecisionAsync((bool Granted, string? Subject, string Reason) decision)
    {
        ResultBorder.IsVisible = true;
        string spoken;
        if (decision.Granted)
        {
            ResultBorder.Background = new SolidColorBrush(Color.Parse("#1E8E3E"));
            ResultText.Foreground   = Brushes.White;
            ResultText.Text = $"ACCESS GRANTED  -  {decision.Subject}";
            spoken = $"Access granted. Welcome, {decision.Subject}.";
        }
        else
        {
            ResultBorder.Background = new SolidColorBrush(Color.Parse("#D93025"));
            ResultText.Foreground   = Brushes.White;
            ResultText.Text = "ACCESS DENIED";
            spoken = "Access denied.";
        }
        InstructionText.Text = "Press Authenticate to try again.";
        SetStatus(decision.Reason);
        await SpeakAsync(spoken);
    }

    // ---- description + reconciliation: START-RUN-STOP per operation --------

    private FaceDescriptorsObject? DescribeFace(byte[] imageData)
    {
        var bvo = BasicVisualObject.FromFile("probe.jpg", imageData);
        var done = RunAim(EfdAiw, new() { ["InputVisual"] = MpaiJson.ToJson(bvo) });
        var json = done?.Ports.TryGetValue("FaceDescriptors", out var j) == true ? j : done?.Ports.Values.FirstOrDefault();
        return string.IsNullOrWhiteSpace(json) ? null : MpaiJson.FromJson<FaceDescriptorsObject>(json);
    }

    private SpeechDescriptorsObject? DescribeSpeech(byte[] wav)
    {
        var bso = BasicSpeechObject.FromData(wav, null);
        var done = RunAim(EsdAiw, new() { ["InputSpeech"] = MpaiJson.ToJson(bso) });
        var json = done?.Ports.TryGetValue("SpeechDescriptors", out var j) == true ? j : done?.Ports.Values.FirstOrDefault();
        return string.IsNullOrWhiteSpace(json) ? null : MpaiJson.FromJson<SpeechDescriptorsObject>(json);
    }

    private InstanceIdentifier? Reconcile(InstanceIdentifier? faceId, InstanceIdentifier? speakerId)
    {
        var boundary = new Dictionary<string, string>();
        if (faceId is not null)    boundary["InputFaceID"]    = MpaiJson.ToJson(faceId);
        if (speakerId is not null) boundary["InputSpeakerID"] = MpaiJson.ToJson(speakerId);
        if (boundary.Count == 0) return null;

        var done = RunAim(IdrAiw, boundary);
        var json = done?.Ports.TryGetValue("ReconciledID", out var j) == true ? j : done?.Ports.Values.FirstOrDefault();
        return string.IsNullOrWhiteSpace(json) ? null : MpaiJson.FromJson<InstanceIdentifier>(json);
    }

    private (bool, string?, string) Decide(InstanceIdentifier? reconciled)
    {
        if (reconciled is null || reconciled.InstanceIdentifierData.Count == 0)
            return (false, null, "no identity after reconciliation");

        var label = reconciled.InstanceIdentifierData[0].InstanceLabel;
        var enrolled = new HashSet<string>(_gallery!.FaceSubjectIds());
        enrolled.UnionWith(_gallery.SpeechSubjectIds());

        bool granted = !string.IsNullOrWhiteSpace(label) && enrolled.Contains(label);
        return (granted, granted ? label : null,
            granted ? $"granted: {label} (enrolled)" : $"denied: '{label}' is not enrolled");
    }

    private static InstanceIdentifier FaceIdentity(string id, float sim) => new()
    {
        InstanceIdentifier_ = id,
        InstanceIdentifierData = { new InstanceCandidate {
            InstanceLabel = id, LabelConfidenceLevel = sim,
            Taxonomy = new InstanceTaxonomy { TaxonomyLevelIDs = { "visual", "face", "person" } } } }
    };

    private static InstanceIdentifier SpeakerIdentity(string id, float sim) => new()
    {
        InstanceIdentifier_ = id,
        InstanceIdentifierData = { new InstanceCandidate {
            InstanceLabel = id, LabelConfidenceLevel = sim,
            Taxonomy = new InstanceTaxonomy { TaxonomyLevelIDs = { "sound", "speech", "speaker" } } } }
    };

    // ---- speaking prompts: START-RUN-STOP the UAG-SPK AIW ------------------

    // Speaking is a SEQUENCED step, not a fire-and-forget task: the UA activates
    // speech only after the preceding AIM has completed and returned. Awaiting this
    // keeps the pipeline ordered - describe completes, THEN the UA speaks, THEN the
    // next user action - so nothing races on the shared UserAgent.
    private Task SpeakAsync(string words) => Task.Run(() =>
    {
        try { RunAim(PromptAiw, new() { ["InputText"] = MpaiJson.ToJson(BasicTextObject.FromText(words)) }); }
        catch (Exception ex) { Console.WriteLine($"[UA] speak failed: {ex.Message}"); }
    });

    // Every UserAgent operation goes through here, under _uaLock, so no two AIW runs
    // (describe, reconcile, speak) ever overlap or re-initialize the Controller
    // concurrently - the shared UserAgent is not safe for concurrent use, and a
    // fire-and-forget Speak must not collide with an in-flight describe.
    private AIF.Controller.Message? RunAim(string aiwName, Dictionary<string, string> boundary)
    {
        if (_ua is null) return null;
        lock (_uaLock)
        {
            _ua.MPAI_AIFU_Controller_Initialize();
            if (_ua.MPAI_AIFU_AIW_Start(aiwName, _provider!, _settings!, out var aiwId) != AifError.OK)
            { Console.WriteLine($"[UA] could not start {aiwName}"); return null; }
            try
            {
                var (error, outcome) = _ua.RunAsync(aiwId, boundary).GetAwaiter().GetResult();
                if (error != AifError.OK || outcome?.Completed is null) { Console.WriteLine($"[UA] run failed: {error}"); return null; }
                if (outcome.Completed.IsError) { Console.WriteLine($"[UA] {outcome.Completed.FailedAim}: {outcome.Completed.Payload}"); return null; }
                return outcome.Completed;
            }
            finally { _ua.MPAI_AIFU_AIW_Stop(aiwId); }
        }
    }

    // ---- plumbing ---------------------------------------------------------

    private void SetStatus(string s) => Dispatcher.UIThread.Post(() => StatusText.Text = s);

    private void AppendLog(string line) => Dispatcher.UIThread.Post(() =>
    {
        LogBox.Text += line + Environment.NewLine;
        LogScroller.ScrollToEnd();
    });

    private static string? FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "AIMs", "AMDs"))) return d.FullName;
            d = d.Parent;
        }
        return null;
    }

    private sealed class PaneWriter : TextWriter
    {
        private readonly Action<string> _line;
        private readonly StringBuilder _pending = new();
        public PaneWriter(Action<string> line) => _line = line;
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value)
        {
            if (value == '\n') { Flush(); return; }
            if (value != '\r') _pending.Append(value);
        }
        public override void Write(string? value) { if (value is not null) foreach (var c in value) Write(c); }
        public override void Flush()
        {
            if (_pending.Length == 0) return;
            var line = _pending.ToString(); _pending.Clear(); _line(line);
        }
    }
}
