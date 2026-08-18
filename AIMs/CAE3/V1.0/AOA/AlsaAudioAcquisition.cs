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
public sealed class AlsaAudioAcquisition : IAudioAcquisitionAim, IStartStopAcquisition
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

    // ---- press-to-stop acquisition ----
    //
    // Windows had this and Linux did not, so MMC-SOA fell to its fixed-duration
    // branch and the Stop button did nothing here: recording ended by the clock.
    //
    // arecord is asked for RAW PCM on stdout rather than a WAV file, and the WAV
    // header is written here at the end. Killing a process that is writing a WAV
    // leaves the RIFF and data lengths as arecord first guessed them, because it
    // rewrites them on exit; a raw stream has no lengths to be wrong, so this
    // sidesteps signal handling altogether and needs no P/Invoke.
    private Process?     _recorder;
    private MemoryStream? _captured;
    private Task?        _pump;

    public void StartAcquire()
    {
        _captured = new MemoryStream();

        var psi = new ProcessStartInfo
        {
            FileName  = _executable,
            Arguments = $"-q -f S16_LE -r {_sampleRate} -c {_channels} -t raw",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            CreateNoWindow         = true
        };

        _recorder = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start {_executable}.");

        // Drain continuously: a full pipe buffer would stall arecord and the
        // recording would quietly stop while appearing to continue.
        var recorder = _recorder;
        var captured = _captured;
        _pump = Task.Run(async () =>
        {
            try   { await recorder.StandardOutput.BaseStream.CopyToAsync(captured); }
            catch { /* the stream ends when the process is killed - expected */ }
        });
    }

    public async Task<BasicAudioObject> StopAcquireAsync()
    {
        if (_recorder is null || _captured is null)
            throw new InvalidOperationException("StopAcquireAsync called before StartAcquire.");

        try { if (!_recorder.HasExited) _recorder.Kill(); } catch { }

        if (_pump is not null) { try { await _pump; } catch { } }
        try { await _recorder.WaitForExitAsync(); } catch { }

        var pcm = _captured.ToArray();

        _recorder.Dispose();
        _recorder = null;
        _captured = null;
        _pump     = null;

        NormalizeIfQuiet(pcm);

        return BasicAudioObject.FromData(WrapAsWav(pcm), BuildQualifier());
    }

    // The 44-byte canonical WAV header for the PCM just captured.
    private byte[] WrapAsWav(byte[] pcm)
    {
        var blockAlign = _channels * _bits / 8;
        var byteRate   = _sampleRate * blockAlign;

        using var stream = new MemoryStream(44 + pcm.Length);
        using var writer = new BinaryWriter(stream);

        writer.Write(new[] { 'R', 'I', 'F', 'F' });
        writer.Write(36 + pcm.Length);
        writer.Write(new[] { 'W', 'A', 'V', 'E' });

        writer.Write(new[] { 'f', 'm', 't', ' ' });
        writer.Write(16);                       // PCM chunk size
        writer.Write((short)1);                 // PCM
        writer.Write((short)_channels);
        writer.Write(_sampleRate);
        writer.Write(byteRate);
        writer.Write((short)blockAlign);
        writer.Write((short)_bits);

        writer.Write(new[] { 'd', 'a', 't', 'a' });
        writer.Write(pcm.Length);
        writer.Write(pcm);

        writer.Flush();
        return stream.ToArray();
    }

    // In place, on 16-bit samples. Mirrors the Windows AOA: lift a quiet
    // recording towards 0.85 of full scale, and leave alone anything already
    // above 0.5 or so quiet that it is silence rather than a soft voice. A
    // microphone at a low input level otherwise reaches whisper as a whisper.
    private void NormalizeIfQuiet(byte[] pcm, float targetPeak = 0.85f, float triggerBelow = 0.5f)
    {
        if (_bits != 16 || pcm.Length < 2) return;

        var samples = pcm.Length / 2;
        var peak    = 0;

        for (var i = 0; i < samples; i++)
        {
            var value = Math.Abs(BitConverter.ToInt16(pcm, i * 2));
            if (value > peak) peak = value;
        }

        var peakFraction = peak / 32768f;
        if (peakFraction >= triggerBelow || peakFraction < 0.001f) return;

        var gain = targetPeak / peakFraction;

        for (var i = 0; i < samples; i++)
        {
            var scaled = BitConverter.ToInt16(pcm, i * 2) * gain;
            var value  = (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
            BitConverter.GetBytes(value).CopyTo(pcm, i * 2);
        }
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
