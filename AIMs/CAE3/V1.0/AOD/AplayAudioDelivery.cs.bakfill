using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using Mpai.Core;

namespace Mpai.Aims.Audio;

// Audio Object Delivery (CAE-AOD) on Linux, via `aplay`.
public sealed class AplayAudioDelivery : IAudioDeliveryAim
{
    private readonly string _executable;

    public AplayAudioDelivery(string executable = "aplay") => _executable = executable;

    public async Task DeliverAsync(BasicAudioObject audio)
    {
        var fileFormat = audio.Qualifier?.Format?.TransportFormats?.FileFormat;
        if (fileFormat is not null && fileFormat != SpeechFileFormat.Wav)
            throw new NotSupportedException($"AplayAudioDelivery plays WAV, not '{fileFormat}'.");

        var wavPath = Path.Combine(Path.GetTempPath(), $"aod_{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(wavPath, audio.Data);

        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = _executable,
                Arguments = $"-q \"{wavPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (p is not null) await p.WaitForExitAsync();
        }
        finally
        {
            try { File.Delete(wavPath); } catch { }
        }
    }
}
