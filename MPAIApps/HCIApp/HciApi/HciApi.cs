using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using AIF.Controller;
using AIF.Store;
using Mpai.Core;
using Mpai.Core.OSD;

namespace Mpai.Hci.Api;

// The HCI API (MPAI-HCI middleware API). A thin faÃ§ade the User Agent (UAD-MAD)
// uses to drive the MMC-MAD Middleware Module across the north API. MMC-MAD is ONE
// AIW (ASR -> EDP -> RSR). The Module is started ONCE and kept alive across turns:
// each turn is one RunAsync on the same instance, so the AIM tree is instantiated
// once (speed) and the running dialogue Summary threads from each turn's
// EditedSummary into the next turn's Summary (context - the dialogue remembers).
// Acquisition (mic) and delivery (loudspeaker, screen) are the UA's real-world
// edges, outside the Module.
public sealed class HciApi : IDisposable
{
    private const string MadModule = "MMC-MAD-V2.5";   // Multimodal Anonymous Dialogue
    private const string MatModule = "MMC-MAT-V2.5";   // Multimodal Anonymous Translation

    private readonly UserAgent    _ua;
    private readonly HciProvider  _provider;
    private readonly AimSettings  _settings;

    private int?    _aiwId;         // the started MMC-MAD instance, kept alive across turns
    private int?    _matAiwId;      // the started MMC-MAT instance, kept alive across turns
    private string? _lastSummary;   // the running dialogue Summary (threaded turn to turn)

    public HciApi(string amdDir, string settingsPath)
    {
        var store = new AmdStore(amdDir);
        store.Scan();
        _settings = AimSettings.Load(settingsPath);
        _provider = new HciProvider(store);
        _ua = new UserAgent(store);
        _ua.MPAI_AIFU_Controller_Initialize();
    }

    // Start MMC-MAD once and keep it alive; subsequent turns reuse the instance.
    private bool EnsureStarted()
    {
        if (_aiwId is not null) return true;
        if (_ua.MPAI_AIFU_AIW_Start(MadModule, _provider, _settings, out var id) != AifError.OK)
            return false;
        _aiwId = id;
        return true;
    }

    // Drive one dialogue turn through MMC-MAD. Supply the human's turn - typed text
    // and/or a spoken Speech Object - and receive the Speaking Avatar. The running
    // Summary is threaded automatically, so the dialogue keeps context across turns.
    public SpeakingAvatar Converse(string? text = null, BasicSpeechObject? speech = null)
    {
        if (!EnsureStarted()) return new SpeakingAvatar(Array.Empty<byte>(), null);

        var boundary = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(text))
            boundary["TextObject"] = MpaiJson.ToJson(BasicTextObject.FromText(text));
        if (speech is not null && speech.Data.Length > 0)
            boundary["InputSpeech"] = MpaiJson.ToJson(speech);
        if (!string.IsNullOrWhiteSpace(_lastSummary))
            boundary["Summary"] = _lastSummary;

        var (err, outcome) = _ua.RunAsync(_aiwId!.Value, boundary).GetAwaiter().GetResult();
        if (err != AifError.OK || outcome?.Completed is null || outcome.Completed.IsError)
            return new SpeakingAvatar(Array.Empty<byte>(), null);

        var outs = outcome.Completed.Ports;
        byte[] wav = Array.Empty<byte>(); FaceDescriptorsObject? fdo = null;
        if (outs.TryGetValue("MachineSpeech", out var sj) && !string.IsNullOrWhiteSpace(sj))
            wav = MpaiJson.FromJson<BasicSpeechObject>(sj)?.Data ?? Array.Empty<byte>();
        if (outs.TryGetValue("MachineFaceDescriptors", out var fj) && !string.IsNullOrWhiteSpace(fj))
            fdo = MpaiJson.FromJson<FaceDescriptorsObject>(fj);
        // Thread the running Summary into the next turn.
        if (outs.TryGetValue("EditedSummary", out var es) && !string.IsNullOrWhiteSpace(es))
            _lastSummary = es;

        return new SpeakingAvatar(wav, fdo);
    }

    // Translate one spoken turn through MMC-MAT: the human speaks in one language;
    // the avatar speaks the translation in another, lip-synced. The input language
    // rides on the speech's own Qualifier (ASR reads it there); the target language
    // is named by the Language Selector and carried onward so Text-To-Speech picks
    // the target voice.
    public SpeakingAvatar Translate(BasicSpeechObject speech, string? fromLang, string toLang)
    {
        if (_matAiwId is null)
        {
            if (_ua.MPAI_AIFU_AIW_Start(MatModule, _provider, _settings, out var mid) != AifError.OK)
                return new SpeakingAvatar(Array.Empty<byte>(), null);
            _matAiwId = mid;
        }

        // Tag the speech with the input language so ASR recognises it in that language.
        var tagged = WithInputLanguage(speech, fromLang);
        var selector = BasicSelectorObject.Languages(fromLang, toLang);

        var boundary = new Dictionary<string, string>
        {
            ["InputSpeech"]      = MpaiJson.ToJson(tagged),
            ["LanguageSelector"] = MpaiJson.ToJson(selector)
        };

        var (err, outcome) = _ua.RunAsync(_matAiwId!.Value, boundary).GetAwaiter().GetResult();
        if (err != AifError.OK || outcome?.Completed is null || outcome.Completed.IsError)
            return new SpeakingAvatar(Array.Empty<byte>(), null);

        var outs = outcome.Completed.Ports;
        byte[] wav = Array.Empty<byte>(); FaceDescriptorsObject? fdo = null;
        if (outs.TryGetValue("MachineSpeech", out var sj) && !string.IsNullOrWhiteSpace(sj))
            wav = MpaiJson.FromJson<BasicSpeechObject>(sj)?.Data ?? Array.Empty<byte>();
        if (outs.TryGetValue("MachineFaceDescriptors", out var fj) && !string.IsNullOrWhiteSpace(fj))
            fdo = MpaiJson.FromJson<FaceDescriptorsObject>(fj);
        string? translated = null;
        if (outs.TryGetValue("TranslatedText", out var tj) && !string.IsNullOrWhiteSpace(tj))
            translated = MpaiJson.FromJson<BasicTextObject>(tj)?.GetText();
        return new SpeakingAvatar(wav, fdo, translated);
    }

    // Set the input language on the speech's Speech Qualifier so ASR recognises it
    // in that language (the Qualifier is where ASR reads the input language). The
    // Qualifier types are init-only, so this rebuilds the qualifier immutably,
    // carrying the existing attributes and setting the language metadata.
    private static BasicSpeechObject WithInputLanguage(BasicSpeechObject speech, string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return speech;
        var oldQ = speech.SpeechQualifier;
        var oldAttr = oldQ?.Attributes;
        var oldMeta = oldAttr?.Metadata;
        var newQualifier = new SpeechQualifier
        {
            SpeechQualifierID = oldQ?.SpeechQualifierID ?? System.Guid.NewGuid().ToString(),
            Attributes = new SpeechAttributes
            {
                Source = oldAttr?.Source ?? SpeechSource.Real,
                Metadata = new SpeechMetadata
                {
                    Language = new Language { LanguageCode = lang, LanguageFormat = "Iso639_1" },
                    SpeakerProperties = oldMeta?.SpeakerProperties
                }
            }
        };
        return BasicSpeechObject.FromData(speech.Data, newQualifier);
    }

    // Begin a fresh conversation: forget the running Summary. The Module stays
    // alive; only the dialogue context is cleared.
    public void ResetConversation() => _lastSummary = null;

    public void Dispose()
    {
        if (_aiwId is not null) { _ua.MPAI_AIFU_AIW_Stop(_aiwId.Value); _aiwId = null; }
        if (_matAiwId is not null) { _ua.MPAI_AIFU_AIW_Stop(_matAiwId.Value); _matAiwId = null; }
        _provider.Dispose();
    }
}

// The Speaking Avatar product: Machine Speech (WAV) + the Machine Face Descriptors
// (the facial-animation timeline). The UA presents it on its devices (loudspeaker,
// screen) - the real-world delivery edge.
public sealed record SpeakingAvatar(byte[] MachineSpeechWav, FaceDescriptorsObject? FaceDescriptors, string? TranslatedText = null);
