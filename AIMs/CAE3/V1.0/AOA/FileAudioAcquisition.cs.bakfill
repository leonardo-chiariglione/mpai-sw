using System;
using System.IO;
using System.Threading.Tasks;

using Mpai.Core;

namespace Mpai.Aims.Audio;

// Audio Object Acquisition (CAE-AOA) â€” file source.
//
// The source of an Audio Object need not be a microphone: a file and a network
// stream are equally valid sources. This implementation reads a WAV file, so a
// system can run with no capture device at all â€” headless, reproducible, and
// portable to any platform.
public sealed class FileAudioAcquisition : IAudioAcquisitionAim
{
    private readonly string sourcePath;
    private readonly int sampleRate;
    private readonly int bits;
    private readonly int channels;

    public FileAudioAcquisition(
        string sourcePath,
        int sampleRate = 16000,
        int bits = 16,
        int channels = 1)
    {
        this.sourcePath = sourcePath;
        this.sampleRate = sampleRate;
        this.bits = bits;
        this.channels = channels;
    }

    public Task<BasicAudioObject> AcquireAsync(
        AcquisitionRequest request)
    {
        var path = sourcePath;

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Audio source not found: {path}",
                path);
        }

        AimLog.Write(
            "CAE-AOA-V1.0",
            $"acquired audio: {path}");

        return Task.FromResult(
            BasicAudioObject.FromData(
                File.ReadAllBytes(path),
                BuildQualifier(path)));
    }

    // The acquisition AIM determines the Qualifier: what was acquired, from
    // where, and in what format.
    private SpeechQualifier BuildQualifier(
        string path)
    {
        return new SpeechQualifier
        {
            SpeechQualifierID = Guid.NewGuid().ToString(),
            SubType = new SubType(),
            Format = new SpeechFormat
            {
                ContentFormats = new SpeechContentFormats
                {
                    RawData = new Pcm
                    {
                        PCM =
                        {
                            new PcmChannel
                            {
                                SamplingFrequency = sampleRate,
                                SamplePrecision = bits
                            }
                        }
                    }
                },
                TransportFormats = new SpeechTransportFormats
                {
                    FileFormat = SpeechFileFormat.Wav
                }
            },
            Attributes = new SpeechAttributes
            {
                Source = SpeechSource.Real,
                Device = new AudioDevice
                {
                    DeviceID = path,
                    DeviceRole = "Capture",
                    DeviceType = "Other",          // a file, not a microphone
                    CaptureConfiguration = new CaptureConfiguration
                    {
                        ChannelCount = channels,
                        SamplingMode = channels == 1 ? "Mono" : "Stereo"
                    }
                }
            }
        };
    }
}

