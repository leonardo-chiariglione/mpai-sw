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
                Arguments = $"-m \"{_config.ModelPath}\" -f \"{wav}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start whisper-cli.");

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            var recognisedText = ExtractTranscription(output);
            return BasicTextObject.FromText(recognisedText, BuildTextQualifier(speech));
        }
        finally
        {
            try { File.Delete(wav); } catch { }
        }
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
