using System;
using System.IO;
using System.Threading.Tasks;

using NAudio.Wave;

using Mpai.Core;

namespace Mpai.Aims.Audio;

// Audio Object Delivery (CAE-AOD) on Windows.
//
// Consumes the Qualifier (expects WAV) and renders the object to the sound
// device. The file it needs in order to play is temporary and is removed
// afterwards: delivering to a loudspeaker should not leave files behind. Use
// FileAudioDelivery when a file IS the destination.
public sealed class WinmmAudioDelivery : IAudioDeliveryAim
{
    public async Task DeliverAsync(
        BasicAudioObject audio)
    {
        var fileFormat =
            audio.Qualifier?.Format?.TransportFormats?.FileFormat;

        if (fileFormat is not null &&
            fileFormat != SpeechFileFormat.Wav)
        {
            throw new NotSupportedException(
                $"WinmmAudioDelivery plays WAV, not '{fileFormat}'.");
        }

        if (audio.Data.Length == 0)
        {
            AimLog.Write(
                "CAE-AOD-V1.0",
                "no audio to play.");

            return;
        }

        var wavPath =
            Path.Combine(
                Path.GetTempPath(),
                $"aod_{Guid.NewGuid():N}.wav");

        try
        {
            await File.WriteAllBytesAsync(wavPath, audio.Data);

            AimLog.Write(
                "CAE-AOD-V1.0",
                $"playing {audio.Data.Length:N0} bytes");

            using var reader = new WaveFileReader(wavPath);
            using var output = new WaveOutEvent();

            output.Init(reader);
            output.Play();

            while (output.PlaybackState == PlaybackState.Playing)
            {
                await Task.Delay(100);
            }
        }
        finally
        {
            // Always, including when playback failed.
            try { File.Delete(wavPath); } catch { }
        }
    }
}

