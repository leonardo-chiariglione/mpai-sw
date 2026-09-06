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

namespace HciApp;

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
        if (_running) { _running = false; _avatar.StopLoop(); StartButton.Content = "Start"; return; }
        _running = true;
        StartButton.Content = "Stop";
        try { await RunFlowAsync(); }
        catch (Exception ex) { SetStatus("error: " + ex.Message); Flog("FLOW EXCEPTION: " + ex); }
        finally { _running = false; StartButton.Content = "Start"; InstructionText.Text = "Press Start to meet the CAV."; }
    }

    // The entire authentication, guided by the lady. Hands-free after Start.
    // ---- the spoken journey: greeting -> auth -> converse <-> translate ----

    private enum Mode { Converse, Translate }
    private Mode _mode = Mode.Converse;
    private string _fromLang = "en", _toLang = "it";
    private volatile bool _running;

    private async Task RunFlowAsync()
    {
        // GREETING - the lady opens the interaction.
        T("STEP welcome: speaking prompt"); await SpeakAsync("Hello. Do you want to have a ride?"); T("STEP welcome: prompt done, capturing yes");
        var yes = await Task.Run(() => _avatar!.CaptureSpeech()); T("STEP welcome: captured yes bytes=" + (yes?.Data?.Length ?? -1));
        var yesText = yes is null ? "" : await Task.Run(() => _hci!.Recognise(yes) ?? ""); T("STEP welcome: yes recognised=<" + yesText + ">");
        Flog("greeting heard: '" + yesText + "'");
        if (LooksLikeNo(yesText))
        {
            await SpeakAsync("All right. Press Start whenever you are ready.");
            return;
        }
        // Anything that isn't a clear "no" (a yes, or an unclear/short reply) proceeds -
        // the person pressed Start and is here, so we go ahead.

        // AUTH - the MAC pipeline (face + speaker -> IDR).
        T("STEP auth: begin"); await SpeakAsync("Let me check who you are.");
        var (granted, name) = await RunAuthAsync(); T("STEP auth: result granted=" + granted + " name=<" + (name ?? "") + ">");
        if (!granted)
        {
            await SpeakAsync("I'm sorry, I could not recognise you.", "ANGER", "disapproving");
            return;
        }

        // CONVERSE / TRANSLATE - the spoken conversation, mode-switched by voice.
        _mode = Mode.Converse;
        await SpeakAsync($"Welcome, {name}. Please get on board. Do you want to have a conversation?", "HAPPINESS", "welcoming");

        while (_running)
        {
            var speech = await Task.Run(() => _avatar!.CaptureSpeech());
            if (!_running) break;
            if (speech is null || speech.Data.Length == 0) continue;

            var text = await Task.Run(() => _hci!.Recognise(speech) ?? "");
            Flog("converse[" + _mode + "] heard: '" + text + "'");

            // Leave the conversation entirely.
            if (LooksLikeGoodbye(text)) { await SpeakAsync("Goodbye. Enjoy your ride.", "HAPPINESS", "welcoming"); break; }

            if (_mode == Mode.Converse)
            {
                if (TryParseTranslate(text, out var from, out var to))
                {
                    _fromLang = from; _toLang = to; _mode = Mode.Translate;
                    await SpeakAsync($"Certainly. I will translate from {LangName(from)} to {LangName(to)}. Speak, and I will translate.", "HAPPINESS", "welcoming");
                    continue;
                }
                // Ordinary affective dialogue.
                var avatar = await Task.Run(() => _hci!.ConverseMpd(speech));
                await _avatar!.PresentAsync(avatar);
                await Task.Delay(TimeSpan.FromSeconds(AvatarUaHost.WavDurationSeconds(avatar.MachineSpeechWav) + 0.4));
            }
            else // Translate
            {
                if (LooksLikeStopTranslating(text))
                {
                    _mode = Mode.Converse;
                    await SpeakAsync("Back to our conversation.", "HAPPINESS", "friendly");
                    continue;
                }
                var avatar = await Task.Run(() => _hci!.Translate(speech, _fromLang, _toLang));
                await _avatar!.PresentAsync(avatar);
                await Task.Delay(TimeSpan.FromSeconds(AvatarUaHost.WavDurationSeconds(avatar.MachineSpeechWav) + 0.4));
            }
        }
    }

    // ---- spoken-intent parsing (deterministic; control flow never depends on the LLM) ----

    private static readonly (string Name, string Code)[] Langs =
    {
        ("english","en"), ("italian","it"), ("spanish","es"), ("portuguese","pt"),
        ("french","fr"), ("german","de"), ("chinese","zh"), ("japanese","ja")
    };
    private static string LangName(string code) =>
        System.Array.Find(Langs, l => l.Code == code).Name is { } n && n.Length > 0
            ? char.ToUpper(n[0]) + n.Substring(1) : code;
    private static string? LangCode(string word)
    {
        foreach (var (name, code) in Langs) if (word.Contains(name)) return code;
        return null;
    }

    private static bool LooksLikeNo(string t)
    { t = t.ToLowerInvariant(); return t.Contains("no thank") || t == "no" || t.StartsWith("no ") || t.Contains("not now") || t.Contains("no, ") || t.Contains("cancel"); }

    private static bool LooksLikeYes(string t)
    { t = t.ToLowerInvariant(); return t.Contains("yes") || t.Contains("yeah") || t.Contains("sure") || t.Contains("ok") || t.Contains("please"); }

    private static bool LooksLikeGoodbye(string t)
    { t = t.ToLowerInvariant(); return t.Contains("goodbye") || t.Contains("bye") || t.Contains("that's all") || t.Contains("stop the conversation") || t.Contains("we are done"); }

    private static bool LooksLikeStopTranslating(string t)
    { t = t.ToLowerInvariant(); return t.Contains("stop translat") || t.Contains("back to") || t.Contains("let's talk") || t.Contains("no more translat"); }

    // Recognise a translation command. Liberal: any mention of "translat" (translate/
    // translation/translating) together with a named language switches to translation.
    // Target = the language after to/into/in, else the last language named; source =
    // after "from", else English.
    private static bool TryParseTranslate(string text, out string from, out string to)
    {
        from = "en"; to = "";
        var t = " " + text.ToLowerInvariant() + " ";
        bool wants = t.Contains("translat")
                     || t.Contains(" into ")
                     || (t.Contains(" from ") && t.Contains(" to "));
        if (!wants) return false;

        // target after to/into/in
        foreach (var kw in new[] { " into ", " to ", " in " })
        {
            var i = t.IndexOf(kw);
            if (i >= 0) { var c = LangCode(t.Substring(i + kw.Length)); if (c != null) { to = c; break; } }
        }
        // source after from
        var f = t.IndexOf(" from ");
        if (f >= 0) { var c = LangCode(t.Substring(f + 6)); if (c != null) from = c; }
        // fallback: if wants translate but no explicit target, use the last language named
        if (to.Length == 0)
        {
            string? last = null;
            foreach (var (name, code) in Langs) if (t.Contains(name)) last = code;
            if (last != null && last != from) to = last;
        }
        // if only a target found and it equals from, still ok (from stays en)
        return to.Length > 0;
    }

    // Access control via the MAC pipeline: the UA acquires the Face Object (webcam)
    // and the Speech Object (mic); HciApi.RunAccessControl runs the CAV-MAC Module
    // (Face + Speaker Recognition -> Identity Reconciliation -> verdict), and the
    // lady speaks the verdict. A non-empty User ID means access is granted. The
    // recognition and the decision are the Module's - the UA only captures + presents.
    private async Task<(bool granted, string? name)> RunAuthAsync()
    {
        ResultBorder.Visibility = Visibility.Collapsed;

        // 1) Face - ask, then grab the webcam frame.
        InstructionText.Text = "Look at the camera.";
        T("AUTH: speaking look-at-camera"); await SpeakAsync("Please look at the camera."); T("AUTH: look prompt done");
        await Task.Delay(400);
        byte[]? frame = null;
        try { frame = await Task.Run(() =>
            new WebcamVisualAcquisition().AcquireAsync(new VisualAcquisitionRequest())
                .GetAwaiter().GetResult().Data); }
        catch { /* the Module reports no-face if absent */ }
        T("AUTH: webcam frame bytes=" + (frame?.Length ?? -1));
        BasicVisualObject? face = frame is { Length: > 0 } ? BasicVisualObject.FromFile("probe.jpg", frame) : null;
        FaceStatus.Text = "face: " + (face is null ? "not captured" : "captured");

        // 2) Voice - ask, then capture the passphrase (mic, VAD auto-stop).
        InstructionText.Text = "Please speak your passphrase.";
        T("AUTH: speaking passphrase-prompt"); await SpeakAsync("Please speak your passphrase."); T("AUTH: passphrase prompt done, capturing NOW");
        BasicSpeechObject? speech = null;
        try { var wav = await Task.Run(() => _avatar!.CaptureSpeech()?.Data);
              if (wav is { Length: > 0 }) speech = BasicSpeechObject.FromData(wav, null); }
        catch { /* the Module reports no-speaker if absent */ }
        T("AUTH: passphrase captured bytes=" + (speech?.Data?.Length ?? -1));
        VoiceStatus.Text = "voice: " + (speech is null ? "not captured" : "captured");

        // 3) Run the CAV-MAC Module once (recognise -> reconcile -> verdict).
        InstructionText.Text = "Checking...";
        T("AUTH: running MAC pipeline (RunAccessControl)"); var result = await Task.Run(() => _hci!.RunAccessControl(face, speech)); T("AUTH: MAC result granted=" + result.Granted + " userId=<" + (result.UserId ?? "") + "> verdictBytes=" + result.Verdict.MachineSpeechWav.Length);

        // 4) The lady speaks the verdict the Module produced (welcoming / reproaching).
        if (result.Verdict.MachineSpeechWav.Length > 0)
        {
            await _avatar!.PresentAsync(result.Verdict);
            await Task.Delay(TimeSpan.FromSeconds(AvatarUaHost.WavDurationSeconds(result.Verdict.MachineSpeechWav) + 0.4));
        }

        // 5) Banner.
        ResultBorder.Visibility = Visibility.Visible;
        if (result.Granted)
        {
            ResultBorder.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E8E3E"));
            ResultText.Foreground = System.Windows.Media.Brushes.White;
            ResultText.Text = $"ACCESS GRANTED  -  {result.UserId}";
            SetStatus($"granted: {result.UserId}");
        }
        else
        {
            ResultBorder.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D93025"));
            ResultText.Foreground = System.Windows.Media.Brushes.White;
            ResultText.Text = "ACCESS DENIED";
            SetStatus("denied: not recognised");
        }
        return (result.Granted, result.UserId);
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

    private static void T(string msg)
    {
        try { System.IO.File.AppendAllText(@"D:\AI\hci-trace.log",
            $"{System.DateTime.Now:HH:mm:ss.fff}  {msg}{System.Environment.NewLine}"); } catch {}
    }

    private static void Flog(string msg)
    {
        try { System.IO.File.AppendAllText(@"D:\AI\hciapp-flow.log",
            $"{System.DateTime.Now:HH:mm:ss.fff}  {msg}{System.Environment.NewLine}"); } catch {}
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
