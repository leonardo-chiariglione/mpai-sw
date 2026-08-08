using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using Mpai.Core;
using Mmc.Tts;

namespace Mpai.Aims.Tts;

// ---------------------------------------------------------------------------
//  Voice facts that Piper "determines" for the output SpeechQualifier: the
//  PCM sample rate/precision it emits and its language. Loaded from the
//  voice's .onnx.json config so the values are what Piper actually produces.
// ---------------------------------------------------------------------------
public sealed class PiperVoiceProfile
{
    public int SampleRate { get; init; } = 22050;          // Piper "medium" default
    public int SamplePrecisionBits { get; init; } = 16;
    public string? LanguageCode { get; init; }             // e.g. "en-US" (BCP 47)

    public static PiperVoiceProfile Load(string onnxJsonConfigPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(onnxJsonConfigPath));
            var root = doc.RootElement;

            int sampleRate = 22050;
            if (root.TryGetProperty("audio", out var audio) &&
                audio.TryGetProperty("sample_rate", out var sr))
            {
                sampleRate = sr.GetInt32();
            }

            string? language = null;
            if (root.TryGetProperty("language", out var lang) &&
                lang.TryGetProperty("code", out var code))
            {
                // Piper writes "en_US"; normalise to BCP 47 "en-US".
                language = code.GetString()?.Replace('_', '-');
            }

            return new PiperVoiceProfile { SampleRate = sampleRate, LanguageCode = language };
        }
        catch
        {
            return new PiperVoiceProfile();   // fall back to the medium-voice defaults
        }
    }
}

// ---------------------------------------------------------------------------
//  MMC-TTS worked example: Basic Text Object -> Basic Speech Object.
//
//  The output Speech Qualifier is built by the three-part transform:
//    inherit   : Language carries over from the input Text Qualifier
//    determine : Format (WAV + PCM), Source = Synthetic, SpeakerType = Agent
//                — facts only this (Piper) AIM knows, from what it builds
//    provenance: the spoken text is embedded as ContentDescription.TextObject
// ---------------------------------------------------------------------------
public sealed class PiperTtsAim : ITtsAim
{
    private readonly IMpaiTtsV1 _piper;
    private readonly PiperVoiceProfile _voice;

    public PiperTtsAim(IMpaiTtsV1 piper, PiperVoiceProfile voice)
    {
        _piper = piper;
        _voice = voice;
    }

    public async Task<BasicSpeechObject> ProcessAsync(BasicTextObject text)
    {
        // Synthesise: Piper produces WAV bytes from the inline text.
        var wav = await _piper.GenerateAsync(text.GetText(), "{}");

        // Build the output Speech Qualifier and attach it to the Basic Speech Object.
        var qualifier = BuildSpeechQualifier(text);

        return BasicSpeechObject.FromData(wav.SpeechData, qualifier);
    }

    private SpeechQualifier BuildSpeechQualifier(BasicTextObject sourceText)
    {
        // ---- inherit: Language from the input Text Qualifier (fallback: the voice) ----
        Language? language =
            sourceText.TextQualifier?.Attributes?.Language
            ?? (_voice.LanguageCode is not null
                ? new Language { LanguageCode = _voice.LanguageCode, LanguageFormat = LanguageFormat.Bcp47 }
                : null);

        // ---- determine: what only Piper knows, from what it produced ----
        var format = new SpeechFormat
        {
            ContentFormats = new SpeechContentFormats
            {
                RawData = new Pcm
                {
                    PCM =
                    {
                        new PcmChannel
                        {
                            SamplingFrequency = _voice.SampleRate,
                            SamplePrecision = _voice.SamplePrecisionBits
                        }
                    }
                }
            },
            TransportFormats = new SpeechTransportFormats
            {
                FileFormat = SpeechFileFormat.Wav          // Piper emits WAV
            }
        };

        // ---- provenance: embed the spoken text as a one-element Text Object ----
        var contentDescription = new ContentDescription
        {
            TextObject = TextObject.FromBasic(sourceText)
        };

        return new SpeechQualifier
        {
            SpeechQualifierID = Guid.NewGuid().ToString(),
            SubType = new SubType(),
            Format = format,
            Attributes = new SpeechAttributes
            {
                Source = SpeechSource.Synthetic,           // determine: synthetic speech
                Metadata = new SpeechMetadata
                {
                    Language = language,                   // inherited
                    SpeakerProperties = new SpeakerProperties
                    {
                        SpeakerType = SpeakerType.Agent,   // determine: spoken by an agent
                        SpeakerCount = 1
                    },
                    ContentDescription = contentDescription
                }
            }
        };
    }
}
