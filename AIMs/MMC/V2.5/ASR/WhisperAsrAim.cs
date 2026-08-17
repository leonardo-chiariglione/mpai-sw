using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Mpai.Core;

namespace Mpai.Aims.Asr;

public sealed class WhisperAsrConfiguration
{
    public required string ExecutablePath { get; init; }   // whisper-cli(.exe)
    public required string ModelPath { get; init; }        // ggml-*.bin
    public string LanguageCode { get; init; } = "en";      // model language (e.g. base.en)
}

// ---------------------------------------------------------------------------
//  MMC-ASR worked transform: Basic Speech Object -> Basic Text Object.
//    inherit   : Language from the input Speech Qualifier, if it carried one
//    determine : the recognised Language (from the model) and text Format = UTF-8
//  Mirror image of the TTS transform.
// ---------------------------------------------------------------------------
public sealed class WhisperAsrAim : IAsrAim
{
    private readonly WhisperAsrConfiguration _config;

    public WhisperAsrAim(WhisperAsrConfiguration config) => _config = config;

    public async Task<BasicTextObject> ProcessAsync(BasicSpeechObject speech)
    {
        var wav = Path.Combine(Path.GetTempPath(), $"asr_{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(wav, speech.Data);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _config.ExecutablePath,
                Arguments = BuildArguments(wav, speech),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,

                // whisper-cli writes UTF-8. Unless told so, .NET decodes a child
                // process's output using the console's code page - Windows-1252
                // here - and any transcription outside Latin-1 arrives as
                // mojibake. Japanese came back as "Ã¤Â»â€¢Ã¦â€¹Â¼Ã¥" and MMC-TTT then
                // translated that faithfully into nonsense.
                //
                // The console host hid this by setting Console.OutputEncoding to
                // UTF-8, which fixes the child decoding as a side effect. The
                // Avalonia application does not, so a latent fault surfaced. The
                // encoding belongs here either way: what whisper-cli emits is a
                // property of whisper-cli, not of whoever happens to be hosting
                // this AIM.
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start whisper-cli.");

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            var recognisedText = ExtractTranscription(output);

            // Say what was heard. Without this line a wrong translation is
            // undiagnosable: "Beaucoup de gens sont allÃ©s rentrer chez eux" may be
            // a faithful translation of a misheard sentence or a bad translation
            // of a correct one, and nothing downstream can tell the difference.
            // The Recognised Text is internal to the Composite AIM, so the User
            // Agent cannot see it - but this AIM can say it.
            System.Console.WriteLine($"[MMC-ASR-V2.5] heard: {recognisedText}");

            return BasicTextObject.FromText(recognisedText, BuildTextQualifier(speech));
        }
        finally
        {
            try { File.Delete(wav); } catch { }
        }
    }

    // whisper-cli was invoked with only -m and -f, so it detected the language
    // itself and the configured LanguageCode reached the output Qualifier but
    // never the command line. MMC-TST relies on the input language arriving on
    // the input Speech Qualifier - that is why it has no Input Language Selector
    // - so the Qualifier's language is what we pass, falling back to the
    // configured default.
    private string BuildArguments(string wav, BasicSpeechObject speech)
    {
        var arguments = $"-m \"{_config.ModelPath}\" -f \"{wav}\"";

        var language =
            speech.SpeechQualifier?.Attributes?.Metadata?.Language?.LanguageCode
            ?? _config.LanguageCode;

        // An English-only model (ggml-*.en.bin) accepts no other language, and
        // passing -l to one is an error rather than a hint.
        var englishOnly =
            System.IO.Path.GetFileNameWithoutExtension(_config.ModelPath)
                  .EndsWith(".en", StringComparison.OrdinalIgnoreCase);

        if (!englishOnly && !string.IsNullOrWhiteSpace(language))
        {
            arguments += $" -l {PrimaryLanguage(language)}";
        }

        return arguments;
    }

    // "it" / "it-IT" -> "it"; "auto" is passed through, whisper understands it.
    private static string PrimaryLanguage(string languageCode)
    {
        var head = languageCode.Trim().ToLowerInvariant().Split('-', '_')[0];

        return head is "auto" || head.Length <= 2 ? head : head.Substring(0, 2);
    }

    private TextQualifier BuildTextQualifier(BasicSpeechObject source)
    {
        // inherit Language from the input speech; else determine from the model.
        Language? language =
            source.SpeechQualifier?.Attributes?.Metadata?.Language
            ?? new Language { LanguageCode = _config.LanguageCode, LanguageFormat = LanguageFormat.Iso639_1 };

        return new TextQualifier
        {
            TextQualifierID = Guid.NewGuid().ToString(),
            Format = new TextFormat
            {
                ContentFormat = new TextContentFormat { Static = TextStaticFormat.Utf8 }   // determine: whisper emits UTF-8
            },
            Attributes = new TextAttributes { Language = language }
        };
    }

    private static string ExtractTranscription(string output)
    {
        var sb = new StringBuilder();

        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            if (!line.StartsWith('[')) continue;

            var cleaned = Regex.Replace(line, @"^\[[^\]]+\]\s*", "").Trim();

            if (cleaned is "" or "[silence]" or "[BLANK_AUDIO]") continue;

            sb.AppendLine(cleaned);
        }

        return sb.ToString().Trim();
    }
}
