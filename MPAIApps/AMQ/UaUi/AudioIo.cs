using System;
using System.IO;
using System.Threading;

using NAudio.Wave;

namespace UaUi;

public interface IAudioRecorder : IDisposable
{
    void Start();
    byte[] Stop();          // returns a complete, finalized WAV (header + PCM)
    bool IsRecording { get; }
}

public interface IAudioPlayer : IDisposable
{
    void PlayWav(byte[] wav);
}

// Windows recorder using NAudio WaveInEvent. Produces 16 kHz mono 16-bit PCM
// WAV — the format the ASR AIM (Whisper) expects.
//
// Correctness notes:
//  * StopRecording() is asynchronous; we wait for RecordingStopped before
//    reading the buffer, so the final audio buffers are captured.
//  * WaveFileWriter writes the RIFF/data chunk sizes on Dispose (not Flush),
//    so we DISPOSE the writer before reading the MemoryStream — otherwise the
//    WAV header carries a zero data length and Whisper hears silence.
public sealed class WindowsAudioRecorder : IAudioRecorder
{
    private WaveInEvent? _waveIn;
    private MemoryStream? _buffer;
    private WaveFileWriter? _writer;
    private readonly WaveFormat _format = new WaveFormat(16000, 16, 1);
    private ManualResetEventSlim? _stopped;

    public bool IsRecording { get; private set; }

    public void Start()
    {
        if (IsRecording) return;

        _buffer  = new MemoryStream();
        _writer  = new WaveFileWriter(_buffer, _format);
        _stopped = new ManualResetEventSlim(false);
        _waveIn  = new WaveInEvent { WaveFormat = _format, BufferMilliseconds = 50 };

        _waveIn.DataAvailable += (_, a) =>
        {
            if (_writer is not null)
                _writer.Write(a.Buffer, 0, a.BytesRecorded);
        };

        _waveIn.RecordingStopped += (_, _) =>
        {
            _stopped?.Set();
        };

        _waveIn.StartRecording();
        IsRecording = true;
    }

    public byte[] Stop()
    {
        if (!IsRecording || _waveIn is null) return Array.Empty<byte>();

        IsRecording = false;
        _waveIn.StopRecording();

        // Wait for the asynchronous RecordingStopped so all buffers are flushed.
        _stopped!.Wait(2000);

        _waveIn.Dispose();
        _waveIn = null;

        // Dispose the writer FIRST so it finalizes the RIFF/data chunk sizes,
        // THEN read the finalized WAV bytes from the buffer.
        _writer!.Dispose();
        _writer = null;

        var bytes = _buffer!.ToArray();
        _buffer.Dispose();
        _buffer = null;

        _stopped.Dispose();
        _stopped = null;

        return bytes;
    }

    public void Dispose()
    {
        try { if (IsRecording) Stop(); } catch { }
        _waveIn?.Dispose();
        _writer?.Dispose();
        _buffer?.Dispose();
        _stopped?.Dispose();
    }
}

public sealed class WindowsAudioPlayer : IAudioPlayer
{
    private WaveOutEvent? _out;
    private WaveFileReader? _reader;
    private MemoryStream? _stream;

    public void PlayWav(byte[] wav)
    {
        if (wav is null || wav.Length == 0) return;
        StopInternal();

        _stream = new MemoryStream(wav);
        _reader = new WaveFileReader(_stream);
        _out    = new WaveOutEvent();
        _out.Init(_reader);
        _out.Play();
    }

    private void StopInternal()
    {
        try { _out?.Stop(); } catch { }
        _out?.Dispose();    _out = null;
        _reader?.Dispose(); _reader = null;
        _stream?.Dispose(); _stream = null;
    }

    public void Dispose() => StopInternal();
}
