using System;
using System.IO;
using System.Threading.Tasks;

using NAudio.Wave;
using NAudio.Wave.SampleProviders;

using Mpai.Core;

namespace Mpai.Cae.Asd;

// ---------------------------------------------------------------------------
//  Basic stereo panning delivery backend. Writes each Basic Audio Object as
//  a stereo WAV file, panned left/right based on the object's position
//  relative to the listener - using NAudio's own PanningSampleProvider
//  rather than reimplementing a stereo mixer.
//
//  Deliberately simple for a first version, matching what was agreed as
//  step 1 of spatial audio (see the earlier discussion of Windows spatial
//  audio options - XAudio2/X3DAudio, ISpatialAudioClient - for what a fuller
//  implementation would build on instead of this):
//
//   - Pan is computed purely from the object's and listener's relative X
//     (left-right) position. Y (forward/back) and Z (up/down) are ignored.
//   - The listener's own Orientation (which way they're facing) is NOT
//     factored in - this always pans as if the listener faces a fixed
//     direction, not wherever they've actually been oriented.
//   - This is basic stereo panning, not full 3D or binaural audio.
//
//  NOTE: this file could not be compiled in the sandbox this was developed
//  in - NAudio comes from nuget.org, which that sandbox's network policy
//  blocks (the same restriction that blocked TIQ's OnnxRuntime/ImageSharp
//  packages earlier). Written carefully against NAudio's well-established
//  API, but the real build check has to happen on your machine.
// ---------------------------------------------------------------------------
public sealed class PannedAudioDelivery : ISpatialAudioDeliveryAim
{
    private readonly string destinationFolder;
    private readonly double panWidthMetres;

    // panWidthMetres: how many metres of left/right offset correspond to a
    // full hard pan (NAudio's Pan range is -1..1). Default 3m is a rough,
    // adjustable starting point, not a value derived from any spec.
    public PannedAudioDelivery(string destinationFolder, double panWidthMetres = 3.0)
    {
        this.destinationFolder = destinationFolder;
        this.panWidthMetres = panWidthMetres;
        Directory.CreateDirectory(destinationFolder);
    }

    // Plain IAudioDeliveryAim.DeliverAsync - no position known, so centre pan.
    public Task DeliverAsync(BasicAudioObject audio) => DeliverAsync(audio, null, null);

    public Task DeliverAsync(BasicAudioObject audio, SpatialAttitude? objectPosition, PointOfView? listenerPointOfView)
    {
        var pan = ComputePan(objectPosition, listenerPointOfView);

        // BasicAudioObject.Data is the raw WAV bytes (see Mpai.Core's
        // compatibility surface) - write to a temp file so NAudio's file-based
        // readers can work with it directly, same pattern FileAudioDelivery
        // already uses elsewhere.
        var tempPath = Path.Combine(Path.GetTempPath(), $"pan_in_{Guid.NewGuid():N}.wav");
        File.WriteAllBytes(tempPath, audio.Data);

        try
        {
            using var reader = new WaveFileReader(tempPath);
            var sampleProvider = reader.ToSampleProvider();

            // PanningSampleProvider requires a MONO source.
            var mono = sampleProvider.WaveFormat.Channels == 1
                ? sampleProvider
                : sampleProvider.ToMono();

            var panner = new PanningSampleProvider(mono) { Pan = (float)pan };

            var outputPath = Path.Combine(destinationFolder, $"{audio.BasicAudioObjectID}.wav");
            WaveFileWriter.CreateWaveFile16(outputPath, panner);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* best effort cleanup */ }
        }

        return Task.CompletedTask;
    }

    private double ComputePan(SpatialAttitude? objectPosition, PointOfView? listenerPointOfView)
    {
        var objectX = objectPosition?.Position?.CartPosition is { Length: >= 1 } op ? op[0] : (double?)null;
        var listenerX = listenerPointOfView?.CartPosition is { Length: >= 1 } lp ? lp[0] : (double?)null;

        // No position data for either side - deliver centred rather than
        // guessing.
        if (objectX is null || listenerX is null) return 0.0;

        var relativeX = objectX.Value - listenerX.Value;
        return Math.Clamp(relativeX / panWidthMetres, -1.0, 1.0);
    }
}