using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

using Mpai.Core;

namespace Mpai.Aims.Audio;

// Audio Object Acquisition (CAE-AOA) on Linux, via `arecord`. Same interface and
// same determined qualifier as the Windows AOA — that is what lets the core
// (ASR-TIQ-TTS) attach to either edge without knowing which.
public sealed class AlsaAudioAcquisition : IAudioAcquisitionAim
{
    private readonly int _sampleRate;
    private readonly int _bits;
    private readonly int _channels;
    private readonly string _executable;

    public AlsaAudioAcquisition(int sampleRate = 16000, int bits = 16, int channels = 1, string executable = "arecord")
    {
        _sampleRate = sampleRate;
        _bits = bits;
        _channels = channels;
        _executable = executable;
    }

    public async Task<BasicAudioObject> AcquireAsync(AcquisitionRequest request)
    {
        var wavPath = Path.Combine(Path.GetTempPath(), $"aoa_{Guid.NewGuid():N}.wav");
        var seconds = ((int)Math.Ceiling(request.Duration.TotalSeconds))
            .ToString(CultureInfo.InvariantCulture);

        var psi = new ProcessStartInfo
        {
            FileName = _executable,
            Arguments = $"-q -d {seconds} -f S16_LE -r {_sampleRate} -c {_channels} \"{wavPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (var p = Process.Start(psi) ?? throw new InvalidOperationException("Could not start arecord."))
        {
            await p.WaitForExitAsync();
        }

        var bytes = await File.ReadAllBytesAsync(wavPath);
        try { File.Delete(wavPath); } catch { }

        return BasicAudioObject.FromData(bytes, BuildQualifier());
    }

    private SpeechQualifier BuildQualifier() => new()
    {
        SpeechQualifierID = Guid.NewGuid().ToString(),
        SubType = new SubType(),
        Format = new SpeechFormat
        {
            ContentFormats = new SpeechContentFormats
            {
                RawData = new Pcm
                {
                    PCM = { new PcmChannel { SamplingFrequency = _sampleRate, SamplePrecision = _bits } }
                }
            },
            TransportFormats = new SpeechTransportFormats { FileFormat = SpeechFileFormat.Wav }
        },
        Attributes = new SpeechAttributes
        {
            Source = SpeechSource.Real,
            Device = new AudioDevice
            {
                DeviceRole = "Capture",
                DeviceType = "Microphone",
                CaptureConfiguration = new CaptureConfiguration
                {
                    ChannelCount = _channels,
                    SamplingMode = _channels == 1 ? "Mono" : "Stereo"
                }
            }
        }
    };
}
