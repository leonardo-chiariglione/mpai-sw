using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

using AIF.Controller;

namespace Mpai.Pgm;

// PGM-ASD-V1.0 - Audio Scene Description, a Sub-AIM of Context Description. From an Audio Object (a Basic Audio Object, OSD-BAO) it gives the Audio Scene Descriptors
// (Basic Audio Scene Descriptors, OSD-BAS-V1.5): the scene of the one Audio Object, its Audio Qualifier SubTypes SoundEffects. The spectral classifier of AudioEvents is inside it.
// WHAT THE SOUND IS (a Bell, a Knock): the published schemas have no element for the class of an Audio Event - the Audio Event Descriptors (OSD-AEV) carry only a set of Audio
// Scene Descriptors, the Audio Qualifier only SubTypes (Speech, Music, SoundEffects, Noise, Mixed). It is written in the Descriptive Metadata of the Audio Scene Descriptors,
// "Audio Event: Bell; why", as the only place the schemas leave (a proposal for an element is in MKG\pending-changes.md).
public sealed class AsdAim : PgmAim
{
    private readonly string _audio, _request, _scene, _response;

    public AsdAim(string instanceId, AimPortReader ports) : base(instanceId, ports)
    {
        _audio = In("OSD-BAO-V1.5"); _request = In(Auc.Cxt); _scene = Out("OSD-BAS-V1.5"); _response = Out(Auc.Cxt);
    }

    public override Task<Message> ProcessAsync(Message message)
    {
        var outputs = new List<(string, JsonNode?)>();
        if (Read(message, _audio) is { } audio && AudioEvents.PcmOf(audio) is { } pcm)
        {
            var (cls, why) = AudioEvents.Classify(pcm);
            var id = (string?)audio["BasicAudioObjectID"] ?? NewId("bao");
            var member = (JsonObject)audio.DeepClone();
            outputs.Add((_scene, new JsonObject
            {
                ["Header"] = "OSD-BAS-V1.5", ["MInstanceID"] = (string?)audio["MInstanceID"] ?? "MI1", ["BasicAudioSceneDescriptorsID"] = NewId("bas"),
                ["BASSpaceTime"] = new JsonObject { ["Header"] = "OSD-SPT-V1.5", ["SpaceTimeID"] = NewId("spt") },
                ["UserPoV"] = new JsonObject { ["Header"] = "OSD-OPV-V1.5", ["PointOfViewID"] = NewId("pov"), ["CartPosition"] = new JsonArray(0.0, 0.0, 0.0), ["Orientation"] = new JsonArray(0.0, 0.0, 0.0) },
                ["AudioObjectCount"] = 1,
                ["BasicAudioSceneDescriptorsData"] = new JsonArray(new JsonObject { ["AObjectIDOrAObject"] = new JsonArray(member) }),
                ["DescrMetadata"] = $"{BknAim.AudioEventPrefix}{cls}; {id}: {why}"
            }));
        }
        if (Read(message, _request) is { } rq && Auc.IsRequest(rq))
        {
            var rs = Auc.Make(Auc.Cxt, Auc.Response, Session.Of(rq), Auc.IdOf(rq));
            rs["Outcome"] = "achieved";
            outputs.Add((_response, rs));
        }
        return Task.FromResult(Give(message, outputs));
    }
}

// THE AUDIO EVENTS Context Description recognises in a Basic Audio Object (OSD-BAO-V1.5) whose data is 16 kHz, 16 bit, mono PCM: a Bell, a Knock, Silence or Other.
// A bell is tonal - a few strong partials, the rest of the spectrum quiet (low spectral flatness) - its strongest partial between 400 and 3000 Hz, and it dies away slowly after
// the strike; a knock is short and noisy. A small classifier of the implementation (one of the Sub-AIMs of CXT, Audio Scene Description, is the implementer's business).
public static class AudioEvents
{
    public const string Bell = "Bell", Knock = "Knock", Silence = "Silence", Other = "Other";

    // The PCM of a Basic Audio Object (its first inline Data), or null.
    public static byte[]? PcmOf(JsonObject bao)
    {
        foreach (var d in bao["BasicAudioObjectData"] as JsonArray ?? [])
            if ((string?)d?["Data"] is { Length: > 0 } b64)
                try { return Convert.FromBase64String(b64); } catch (FormatException) { return null; }
        return null;
    }

    public static (string Class, string Why) Classify(byte[] pcm16k)
    {
        const int Rate = 16000, N = 4096;
        var x = new float[pcm16k.Length / 2];
        for (var i = 0; i < x.Length; i++) x[i] = BitConverter.ToInt16(pcm16k, i * 2) / 32768f;
        if (x.Length < N) return (Other, "too short");
        double Rms(int from, int len) { double s = 0; for (var i = from; i < from + len && i < x.Length; i++) s += x[i] * x[i]; return Math.Sqrt(s / len); }
        var frames = Enumerable.Range(0, x.Length / N).Select(k => Rms(k * N, N)).ToList();
        var loudest = frames.IndexOf(frames.Max());
        var c = new Complex[N];
        for (var i = 0; i < N; i++) c[i] = x[loudest * N + i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (N - 1)));
        Fft(c);
        var mag = Enumerable.Range(1, N / 2 - 1).Select(k => c[k].Magnitude + 1e-12).ToArray();
        var flat = Math.Exp(mag.Average(Math.Log)) / mag.Average();
        var peak = (Array.IndexOf(mag, mag.Max()) + 1) * (double)Rate / N;
        var rings = frames.Skip(loudest).Count(f => f > frames[loudest] * 0.1) * N / (double)Rate;
        var why = $"flatness {flat:0.000}, strongest partial {peak:0} Hz, rings {rings:0.0} s";
        if (frames.Max() < 0.01) return (Silence, why);
        if (flat < 0.05 && peak is > 400 and < 3000 && rings >= 0.5) return (Bell, why);
        if (rings < 0.3) return (Knock, why);
        return (Other, why);
    }

    // The Audio Event an Audio Scene Description gave, from its Descriptive Metadata ("Audio Event: Bell; ..."): see AsdAim.
    public static (string Class, string Why)? EventOf(JsonObject? bas)
    {
        if ((string?)bas?["DescrMetadata"] is not { } d || !d.StartsWith(BknAim.AudioEventPrefix, StringComparison.Ordinal)) return null;
        var rest = d[BknAim.AudioEventPrefix.Length..];
        var cut = rest.IndexOf(';');
        return cut < 0 ? (rest.Trim(), "") : (rest[..cut].Trim(), rest[(cut + 1)..].Trim());
    }

    private static void Fft(Complex[] a)
    {
        var n = a.Length;
        for (int i = 1, j = 0; i < n; i++) { var bit = n >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit; if (i < j) (a[i], a[j]) = (a[j], a[i]); }
        for (var len = 2; len <= n; len <<= 1)
        {
            var w = Complex.FromPolarCoordinates(1, -2 * Math.PI / len);
            for (var i = 0; i < n; i += len) { var wn = Complex.One; for (var k = 0; k < len / 2; k++) { var u = a[i + k]; var v = a[i + k + len / 2] * wn; a[i + k] = u + v; a[i + k + len / 2] = u - v; wn *= w; } }
        }
    }
}
