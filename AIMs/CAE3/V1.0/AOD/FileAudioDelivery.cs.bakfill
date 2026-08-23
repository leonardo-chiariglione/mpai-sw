using System;
using System.IO;
using System.Threading.Tasks;

using Mpai.Core;

namespace Mpai.Aims.Audio;

// Audio Object Delivery (CAE-AOD) â€” file destination.
//
// A destination need not be a loudspeaker: writing the Audio Object to a file
// is delivery too. Lets a system run with no rendering device â€” headless and
// portable â€” and makes results inspectable after the fact.
public sealed class FileAudioDelivery : IAudioDeliveryAim
{
    private readonly string destinationFolder;

    public FileAudioDelivery(
        string destinationFolder)
    {
        this.destinationFolder = destinationFolder;
    }

    public Task DeliverAsync(
        BasicAudioObject audio)
    {
        var fileFormat =
            audio.Qualifier?.Format?.TransportFormats?.FileFormat;

        if (fileFormat is not null &&
            fileFormat != SpeechFileFormat.Wav)
        {
            throw new NotSupportedException(
                $"FileAudioDelivery writes WAV, not '{fileFormat}'.");
        }

        Directory.CreateDirectory(destinationFolder);

        var path =
            Path.Combine(
                destinationFolder,
                $"{audio.BasicAudioObjectID}.wav");

        File.WriteAllBytes(path, audio.Data);

        AimLog.Write(
            "CAE-AOD-V1.0",
            $"delivered {audio.Data.Length:N0} bytes -> {path}");

        return Task.CompletedTask;
    }
}

