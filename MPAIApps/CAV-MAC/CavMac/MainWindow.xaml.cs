using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

using AIF.Controller;
using AIF.Store;
using AIF.GlobalStorage;

using Mpai.Core;
using Mpai.Core.OSD;
using Mpai.Gallery;
using Mpai.Aims.Visual;   // WebcamVisualAcquisition
using Mpai.UaKit;         // AvatarUaHost (avatar render + VAD mic capture)
using Mpai.Hci.Api;       // HciApi.Announce (the lady speaks)

namespace CavMac;

// CAV-MAC - Multimodal Access Control, guided entirely by the avatar. NO BUTTONS.
//
// The lady runs the whole flow herself: she asks the person to look at the camera
// (then the image is captured automatically), recognises the face, asks for the
// passphrase (then the microphone listens automatically, stopping on silence),
// recognises the speaker, reconciles the two identities, and speaks the verdict -
// welcoming on success, concerned on failure. The person only looks and speaks.
public partial class MainWindow : Window
{
    private const string EfdAiw = "UAG-EFD-V1.0";
    private const string EsdAiw = "UAG-ESD-V1.0";
    private const string IdrAiw = "UAG-IDR-V1.0";

    private const float FaceThreshold    = 0.35f;
    private const float SpeakerThreshold = 0.45f;

    private const string AmdDir       = @"D:\AI\AIMs\AMDs";
    private const string SettingsPath = @"D:\AI\AIMs\aim-settings.json";
    private static readonly string AssetsDir = @"D:\AI\Lib\Assets";

    private UserAgent?         _ua;
    private CavMacProvider?    _provider;
    private AimSettings?       _settings;
    private SubjectRepository? _gallery;
    private AvatarUaHost?      _avatar;
    private HciApi?            _hci;
    private readonly object _uaLock = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }


    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SetStatus("loading...");
            var repoRoot = FindRepoRoot() ?? @"D:\AI";

            _avatar = new AvatarUaHost(Web, Dispatcher, AmdDir, AssetsDir);
            await _avatar.InitAsync();

            await Task.Run(() =>
            {
                var store = new AmdStore(AmdDir); store.Scan();
                _settings = AimSettings.Load(SettingsPath);
                _provider = new CavMacProvider(store);
                _ua       = new UserAgent(store);
                var storage = new FileGlobalStorage(
                    Path.Combine(repoRoot, "TestData", "gallery-store"), topAim: "CAV-MAC");
                _gallery = new SubjectRepository(storage);
                _hci = new HciApi(AmdDir, SettingsPath);
            });

            // Ready - wait for the activation request (Start), like the dialogue and
            // translation apps. The app is a running service; Start begins one guided
            // authentication. Everything after Start is hands-free.
            SetStatus("Ready. Press Start to begin.");
            InstructionText.Text = "Press Start to begin.";
            StartButton.IsEnabled = true;
        }
        catch (Exception fatal)
        {
            Program.Record("startup", fatal);
            SetStatus($"startup failed: {fatal.Message}");
        }
    }

    // The activation request. Press Start, and the lady runs one guided authentication
    // hands-free (look -> capture -> passphrase -> listen -> reconcile -> verdict).
    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_hci is null || _avatar is null) return;
        StartButton.IsEnabled = false;
        try { await RunFlowAsync(); }
        catch (Exception ex) { SetStatus("error: " + ex.Message); }
        finally
        {
            InstructionText.Text = "Press Start to authenticate again.";
            StartButton.IsEnabled = true;
        }
    }

    // The entire authentication, guided by the lady. Hands-free after Start.
    private async Task RunFlowAsync()
    {
        ResultBorder.Visibility = Visibility.Collapsed;
        (string SubjectId, float Similarity)? faceMatch = null, speakerMatch = null;

        // 1) Face -------------------------------------------------------------
        InstructionText.Text = "Look at the camera.";
        await SpeakAsync("Please look at the camera.");

        await Task.Delay(400);
        byte[]? frame = null;
        try { frame = await Task.Run(() =>
            new WebcamVisualAcquisition().AcquireAsync(new VisualAcquisitionRequest())
                .GetAwaiter().GetResult().Data); }
        catch { /* step continues */ }

        try { faceMatch = await Task.Run(() =>
        {
            if (frame is null) return ((string, float)?)null;
            var fdo = DescribeFace(frame);
            return fdo?.Embedding() is { } emb ? _gallery!.MatchFace(emb, FaceThreshold) : null;
        }); }
        catch { /* step continues */ }
        FaceStatus.Text = "face: " + (faceMatch is { } fm ? $"{fm.SubjectId} ({fm.Similarity:F2})" : "no match");

        // 2) Voice ------------------------------------------------------------
        InstructionText.Text = "Please speak your passphrase.";
        await SpeakAsync("Please speak your passphrase.");

        byte[]? wav = null;
        try { wav = await Task.Run(() => _avatar!.CaptureSpeech()?.Data); }
        catch { /* step continues */ }

        try { speakerMatch = await Task.Run(() =>
        {
            if (wav is null) return ((string, float)?)null;
            var sdo = DescribeSpeech(wav);
            return sdo?.Embedding() is { } emb ? _gallery!.MatchSpeech(emb, SpeakerThreshold) : null;
        }); }
        catch { /* step continues */ }
        VoiceStatus.Text = "voice: " + (speakerMatch is { } sm ? $"{sm.SubjectId} ({sm.Similarity:F2})" : "no match");

        // 3) Reconcile + verdict ---------------------------------------------
        var decision = await Task.Run(() =>
        {
            var faceId = faceMatch is { } f ? FaceIdentity(f.SubjectId, f.Similarity) : null;
            var spkId  = speakerMatch is { } s ? SpeakerIdentity(s.SubjectId, s.Similarity) : null;
            var reconciled = Reconcile(faceId, spkId);
            return Decide(reconciled);
        });
        await ShowDecisionAsync(decision);
    }

    private async Task ShowDecisionAsync((bool Granted, string? Subject, string Reason) decision)
    {
        ResultBorder.Visibility = Visibility.Visible;
        string spoken; string emotion; string attitude;
        if (decision.Granted)
        {
            ResultBorder.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E8E3E"));
            ResultText.Foreground = System.Windows.Media.Brushes.White;
            ResultText.Text = $"ACCESS GRANTED  -  {decision.Subject}";
            spoken = $"Access granted. Welcome, {decision.Subject}.";
            emotion = "HAPPINESS"; attitude = "welcoming";
        }
        else
        {
            ResultBorder.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D93025"));
            ResultText.Foreground = System.Windows.Media.Brushes.White;
            ResultText.Text = "ACCESS DENIED";
            spoken = "I'm sorry, I could not recognise you. Access denied.";
            emotion = "ANGER"; attitude = "disapproving";
        }
        InstructionText.Text = decision.Reason;
        SetStatus(decision.Reason);
        await SpeakAsync(spoken, emotion, attitude);
    }

    // The lady speaks (Announce -> Response and Scene Rendering -> speaking avatar).
    private async Task SpeakAsync(string words, string emotion = "CALMNESS", string? attitude = null)
    {
        if (_hci is null || _avatar is null) return;
        try
        {
            var sa = await Task.Run(() => _hci!.Announce(words, emotion, attitude));
            await _avatar.PresentAsync(sa);
            var seconds = AvatarUaHost.WavDurationSeconds(sa.MachineSpeechWav);
            await Task.Delay(TimeSpan.FromSeconds(seconds + 0.4));
        }
        catch { /* step continues */ }
    }

    // ---- description + reconciliation (start-run-stop per op) --------------

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

    private AIF.Controller.Message? RunAim(string aiwName, Dictionary<string, string> boundary)
    {
        if (_ua is null) return null;
        lock (_uaLock)
        {
            _ua.MPAI_AIFU_Controller_Initialize();
            if (_ua.MPAI_AIFU_AIW_Start(aiwName, _provider!, _settings!, out var aiwId) != AifError.OK)
            { return null; }
            try
            {
                var (error, outcome) = _ua.RunAsync(aiwId, boundary).GetAwaiter().GetResult();
                if (error != AifError.OK || outcome?.Completed is null) { return null; }
                if (outcome.Completed.IsError) { return null; }
                return outcome.Completed;
            }
            finally { _ua.MPAI_AIFU_AIW_Stop(aiwId); }
        }
    }

    private void SetStatus(string s) => Dispatcher.Invoke(() => StatusText.Text = s);

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
}
