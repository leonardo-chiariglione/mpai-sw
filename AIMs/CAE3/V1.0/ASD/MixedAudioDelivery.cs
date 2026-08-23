using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using Mpai.Core;

namespace Mpai.Cae.Asd;

// ---------------------------------------------------------------------------
//  Plays a composition AS A COMPOSITION: every component at once, each panned
//  to where it sits and delayed to when it starts, summed into one stereo
//  signal and played through the loudspeaker.
//
//  This is what makes an arrangement audible. PannedAudioDelivery pans, but
//  writes a file per component; WinmmAudioDelivery plays, but one after another
//  and with position ignored. Neither lets you HEAR a scene.
//
//  WHAT IT DOES NOT DO, deliberately, and matching PannedAudioDelivery's own
//  first-version limits:
//
//   - Pan is computed from the left-right offset alone. Forward/back and
//     up/down are ignored.
//   - The listener's own orientation is not applied: this pans as though the
//     listener always faces the same way.
//   - Distance does not attenuate. A thing forty metres away is as loud as a
//     thing beside you.
//
//  Those are the next steps, and each is a real one. This is stereo panning,
//  not binaural rendering.
//
//  NOTE: NAudio cannot be reached from the sandbox this was written in, so the
//  build check happens on your machine - the same caveat PannedAudioDelivery
//  carries.
// ---------------------------------------------------------------------------
public sealed class MixedAudioDelivery : IMixingAudioDeliveryAim
{
    private readonly double panWidthMetres;
    private readonly double referenceDistanceMetres;
    private readonly int mixRate;

    private readonly List<Collected> collected = new();

    private sealed record Collected(byte[] Wav, double Pan, double Gain, double StartSeconds);

    // panWidthMetres: how far to the side is a FULL hard pan.
    //
    // Three metres saturated immediately: an editing space runs to fifty metres
    // each way, so everything placed anywhere near the edges panned hard left or
    // hard right, and moving the listener between them changed nothing that
    // could be heard. Twenty metres leaves room for a position to be somewhere
    // rather than at one extreme.
    //
    // referenceDistanceMetres: nearer than this, nothing gets louder. Without it
    // a listener standing on top of something divides by nearly zero.
    //
    // mixRate: everything is resampled to this before mixing. Components can
    // differ - a Piper voice is 22050 Hz and a captured file is often 16000 -
    // and a mixer needs one format.
    public MixedAudioDelivery(
        double panWidthMetres = 20.0,
        double referenceDistanceMetres = 1.0,
        int mixRate = 48000)
    {
        this.panWidthMetres = panWidthMetres;
        this.referenceDistanceMetres = referenceDistanceMetres;
        this.mixRate = mixRate;
    }

    public void Begin() => collected.Clear();

    // The plain path: no position known, so centred and starting at once.
    public Task DeliverAsync(BasicAudioObject audio) => DeliverAsync(audio, (SpaceTime?)null, null);

    public Task DeliverAsync(
        BasicAudioObject audio,
        SpaceTime? placement,
        PointOfView? listenerPointOfView)
    {
        if (audio.Data.Length == 0) return Task.CompletedTask;

        collected.Add(new Collected(
            audio.Data,
            ComputePan(placement, listenerPointOfView),
            ComputeGain(placement, listenerPointOfView),
            StartSecondsOf(placement)));

        return Task.CompletedTask;
    }

    public async Task FinishAsync()
    {
        if (collected.Count == 0) return;

        var temporary = new List<string>();
        var readers = new List<WaveFileReader>();

        try
        {
            var mixer = new MixingSampleProvider(
                WaveFormat.CreateIeeeFloatWaveFormat(mixRate, 2))
            {
                ReadFully = true    // silence rather than the end of the stream
            };

            foreach (var item in collected)
            {
                // NAudio's readers want a file. The bytes are a WAV already, so
                // this is a hand-off rather than a conversion.
                var path = Path.Combine(Path.GetTempPath(), $"mix_{Guid.NewGuid():N}.wav");
                File.WriteAllBytes(path, item.Wav);
                temporary.Add(path);

                var reader = new WaveFileReader(path);
                readers.Add(reader);

                ISampleProvider source = reader.ToSampleProvider();

                // Panning wants mono in and gives stereo out.
                if (source.WaveFormat.Channels > 1) source = source.ToMono();

                if (source.WaveFormat.SampleRate != mixRate)
                {
                    source = new WdlResamplingSampleProvider(source, mixRate);
                }

                // FARTHER IS QUIETER, which is what makes moving towards
                // something feel like moving towards it. Panning alone tells you
                // which side a thing is on and nothing about how far.
                if (item.Gain < 1.0)
                {
                    source = new VolumeSampleProvider(source) { Volume = (float)item.Gain };
                }

                ISampleProvider panned = new PanningSampleProvider(source)
                {
                    Pan = (float)item.Pan
                };

                // WHEN it starts, rather than a pause between components. Two
                // Objects that begin at the same moment begin together.
                if (item.StartSeconds > 0)
                {
                    panned = new OffsetSampleProvider(panned)
                    {
                        DelayBy = TimeSpan.FromSeconds(item.StartSeconds)
                    };
                }

                mixer.AddMixerInput(panned);
            }

            using var output = new WaveOutEvent();
            output.Init(mixer);
            output.Play();

            // ReadFully means the mixer never ends, so it is stopped when the
            // longest component has had time to finish rather than waited on.
            await Task.Delay(LongestOf(readers, collected));

            output.Stop();
        }
        finally
        {
            foreach (var reader in readers)
            {
                try { reader.Dispose(); } catch { }
            }

            foreach (var path in temporary)
            {
                try { File.Delete(path); } catch { }
            }

            collected.Clear();
        }
    }

    private static TimeSpan LongestOf(List<WaveFileReader> readers, List<Collected> items)
    {
        var longest = TimeSpan.Zero;

        for (var i = 0; i < readers.Count && i < items.Count; i++)
        {
            var ends = TimeSpan.FromSeconds(items[i].StartSeconds) + readers[i].TotalTime;
            if (ends > longest) longest = ends;
        }

        // A little after, so nothing is clipped by stopping the device.
        return longest + TimeSpan.FromMilliseconds(250);
    }

    // WHEN it starts, from the placement's own time segment.
    private static double StartSecondsOf(SpaceTime? placement) =>
        placement?.Time?.SimpleTimeData is { Count: > 0 } segments
            ? Math.Max(0, segments[0].StartTime)
            : 0;

    // INVERSE, not inverse-square.
    //
    // Sound PRESSURE falls with 1/r - the six decibels per doubling everyone
    // quotes - while inverse-square describes intensity, which is power. A
    // sample stream carries pressure, so squaring would attenuate twice over: at
    // forty metres it gives sixty-four decibels down where the physical answer
    // is thirty-two, which is silence rather than distance.
    private double ComputeGain(SpaceTime? placement, PointOfView? listenerPointOfView)
    {
        var objectAt = placement?.SpatialAttitude1?.Position?.CartPosition;
        var listenerAt = listenerPointOfView?.CartPosition;

        if (objectAt is not { Length: >= 3 } || listenerAt is not { Length: >= 3 }) return 1.0;

        // The whole distance, not the left-right part of it: a thing straight
        // ahead is as far away as one to the side, and should sound it.
        var dx = objectAt[0] - listenerAt[0];
        var dy = objectAt[1] - listenerAt[1];
        var dz = objectAt[2] - listenerAt[2];

        var distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

        return referenceDistanceMetres / Math.Max(distance, referenceDistanceMetres);
    }

    private double ComputePan(SpaceTime? placement, PointOfView? listenerPointOfView)
    {
        var objectX = placement?.SpatialAttitude1?.Position?.CartPosition is { Length: >= 1 } op ? op[0] : (double?)null;
        var listenerX = listenerPointOfView?.CartPosition is { Length: >= 1 } lp ? lp[0] : (double?)null;

        if (objectX is null || listenerX is null) return 0.0;

        return Math.Clamp((objectX.Value - listenerX.Value) / panWidthMetres, -1.0, 1.0);
    }
}