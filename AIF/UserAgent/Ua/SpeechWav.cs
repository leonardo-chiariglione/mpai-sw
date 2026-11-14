using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace Mpai.Ua;

// SPEECH AS A PERSON HEARS IT AND GIVES IT: a Basic Speech Object (OSD-BSO-V1.5) made into one WAV to play, and a WAV file a person chose made into the 16 kHz mono samples a
// client puts in a Speech Object. What the Object's data is, its Speech Qualifier says (TFA-SPQ-V1.5): Formats.TransportFormats.FileFormat "WAV", each data item a WAV file;
// Formats.ContentFormats.RawData, raw 16-bit PCM at PCM.SamplingFrequency; neither, a WAV file. The data items are played one after the other.
public static class SpeechWav
{
    // One WAV of all the data items of the Speech Object, or null if it has none this can decode.
    public static byte[]? ToWav(string speechObject)
    {
        var o = JsonNode.Parse(speechObject);
        // THE SAME OBJECT IN THE FORM OF MPAI.CORE (one Data, the Qualifier as SpeechQualifier.Format), as a client receives it through the MPAI-MAS Service
        if (o?["BasicSpeechObjectData"] is null && o?["Data"] is JsonValue flat)
        {
            var format = o["SpeechQualifier"]?["Format"];
            var pcm = format?["ContentFormats"]?["RawData"];
            var fileFormat = format?["TransportFormats"]?["FileFormat"]?.GetValue<string>();
            var bytes = Convert.FromBase64String(flat.GetValue<string>());
            if (pcm is not null && fileFormat is not "WAV")
            {
                var samples16 = new short[bytes.Length / 2];
                Buffer.BlockCopy(bytes, 0, samples16, 0, samples16.Length * 2);
                return samples16.Length == 0 ? null : Encode(samples16, pcm["SamplingFrequency"]?.GetValue<int>() ?? 16000);
            }
            return Decode(bytes) is { } one ? Encode(one.Mono, one.Rate) : null;
        }
        var formats = o?["BasicSpeechDataQualifier"]?["Formats"];
        var raw = formats?["ContentFormats"]?["RawData"];
        var file = formats?["TransportFormats"]?["FileFormat"]?.GetValue<string>();
        var rate = raw?["PCM"]?["SamplingFrequency"]?.GetValue<int>() ?? 16000;
        var samples = new List<short>();
        foreach (var item in (o?["BasicSpeechObjectData"] as JsonArray) ?? [])
        {
            if (item?["Data"]?.GetValue<string>() is not { Length: > 0 } b64) continue;
            var bytes = Convert.FromBase64String(b64);
            if (raw is not null && file is not "WAV")
                for (var i = 0; i + 1 < bytes.Length; i += 2) samples.Add(BitConverter.ToInt16(bytes, i));
            else if (Decode(bytes) is { } wav) { rate = wav.Rate; samples.AddRange(wav.Mono); }
        }
        return samples.Count == 0 ? null : Encode(samples.ToArray(), rate);
    }

    // The samples of a WAV file, mono, 16-bit, at 16 kHz (the channels averaged, the rate changed by linear interpolation); null if it is not a PCM WAV file.
    public static byte[]? Pcm16kMono(byte[] wavFile)
    {
        if (Decode(wavFile) is not { } wav) return null;
        var mono = wav.Mono;
        var count = (int)((long)mono.Length * 16000 / wav.Rate);
        var pcm = new byte[count * 2];
        for (var i = 0; i < count; i++)
        {
            var at = (double)i * wav.Rate / 16000;
            var k = (int)at; var f = at - k;
            var a = mono[Math.Min(k, mono.Length - 1)]; var b = mono[Math.Min(k + 1, mono.Length - 1)];
            BitConverter.TryWriteBytes(pcm.AsSpan(i * 2), (short)Math.Round(a + (b - a) * f));
        }
        return pcm;
    }

    // READY-MADE SPEECH A WORKFLOW NAMES BY ITS ADDRESS: the part of a Speech Object it gives an acquisition - BasicSpeechObjectData[0].DataURI, and the
    // BasicSpeechObjectID as its name (else the name of the file) - or null where the part names no address.
    public static (string Label, string Uri)? Example(string? wanted)
    {
        if (string.IsNullOrEmpty(wanted) || !wanted.Contains("DataURI", StringComparison.Ordinal)) return null;
        try
        {
            var node = JsonNode.Parse(wanted);
            if (node?["BasicSpeechObjectData"]?[0]?["DataURI"]?.GetValue<string>() is not { Length: > 0 } uri) return null;
            return (node["BasicSpeechObjectID"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(new Uri(uri).AbsolutePath), uri);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or UriFormatException) { return null; }
    }

    private sealed record Wav(int Rate, short[] Mono);

    private static Wav? Decode(byte[] b)
    {
        if (b.Length < 44 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'W' || b[9] != 'A' || b[10] != 'V' || b[11] != 'E') return null;
        int channels = 0, rate = 0, bits = 0, at = 12;
        while (at + 8 <= b.Length)
        {
            var id = System.Text.Encoding.ASCII.GetString(b, at, 4); var size = BitConverter.ToInt32(b, at + 4); var body = at + 8;
            if (id == "fmt ") { if (BitConverter.ToInt16(b, body) != 1) return null; channels = BitConverter.ToInt16(b, body + 2); rate = BitConverter.ToInt32(b, body + 4); bits = BitConverter.ToInt16(b, body + 14); }
            else if (id == "data")
            {
                if (channels <= 0 || rate <= 0 || bits is not (8 or 16 or 24 or 32)) return null;
                var bytesPer = bits / 8; var length = Math.Min(size, b.Length - body); var frames = length / (bytesPer * channels);
                var mono = new short[frames];
                for (var i = 0; i < frames; i++)
                {
                    double sum = 0;
                    for (var c = 0; c < channels; c++)
                    {
                        var p = body + (i * channels + c) * bytesPer;
                        sum += bits switch
                        {
                            8 => (b[p] - 128) * 256.0,
                            16 => BitConverter.ToInt16(b, p),
                            24 => ((b[p + 2] << 24) | (b[p + 1] << 16) | (b[p] << 8)) >> 16,
                            _ => BitConverter.ToInt32(b, p) >> 16
                        };
                    }
                    mono[i] = (short)Math.Clamp(Math.Round(sum / channels), short.MinValue, short.MaxValue);
                }
                return new Wav(rate, mono);
            }
            at = body + size + (size & 1);
        }
        return null;
    }

    private static byte[] Encode(short[] samples, int rate)
    {
        using var m = new MemoryStream();
        using var w = new BinaryWriter(m);
        w.Write("RIFF"u8); w.Write(36 + samples.Length * 2); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(samples.Length * 2);
        foreach (var s in samples) w.Write(s);
        w.Flush();
        return m.ToArray();
    }
}
