using System;
using System.Collections.Generic;
using System.Linq;

using Mpai.Core;       // BasicAudioObject, PointOfView
using Mpai.Core.OSD;   // BasicAudioSceneDescriptors, BasicAudioSceneEntry
using Mpai.Aims.Audio; // MicrophoneArrayGeometry (defined in the AOA / CAE3 project)

namespace Mpai.Osd.AudioScene;

// =============================================================================
//  AVS ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â Audio pipeline (Slice 2)
// -----------------------------------------------------------------------------
//  Consumes what the microphone-array AOA produces ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â a BasicAudioObject (the
//  interleaved multichannel WAV) plus the MicrophoneArrayGeometry ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â and
//  localises speaking humans: is anyone speaking (VAD), and from which
//  direction (DOA). This is the audio half of AV Scene Description; the visual
//  half and the face<->voice fusion are separate and marked as extension points.
//  Output is the real BasicAudioSceneDescriptors (OSD-BAS); a located speaker is
//  a BasicAudioSceneEntry whose PointOfView carries the DOA azimuth.
//
//  STATUS
//    Real and usable:  WAV de-interleave, energy VAD, GCC-PHAT DOA over the
//                      per-microphone CartPosition. No model, no external
//                      dependency ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â pure C#, runs out of the box.
//    Extension points: model-based VAD (sherpa-onnx/WebRTC), multi-source DOA,
//                      and fusion with the visual pipeline / speaker identity.
//
//  Not compile-verified here (no .NET SDK in the authoring environment); written
//  against the real repo types (BasicAudioObject.Data, PointOfView.CartPosition).
// =============================================================================
// ---------------------------------------------------------------------------
//  FUTURE INPUT (reference model, not yet in the system): AVS is also to
//  receive the PREVIOUS cycle's Full Environment Descriptors (CAV-FED-V1.1)
//  as prior context - FED(t-1) -> AVS(t) - to inform the current scene
//  description (temporal continuity, tracking, disambiguation).
//
//  TRUST: HCI is a separate trust domain from the other Ego-CAV subsystems
//  (ESS/AMS/MAS). So FED arriving here crosses a trust boundary and carries
//  MPAI-PTF Data Exchange Metadata (DataXMData -> PTF/V1.0): it is external,
//  provenance/authorisation/confidence-checked data, to be trust-evaluated,
//  NOT treated as HCI-internal state. (FED from a REMOTE CAV crosses the same
//  way, arriving in the Ego-Remote HCI Message (CAV-ERH-V1.1), also PTF-borne.)
//
//  Depends on: fused AudioVisualSceneDescriptors (OSD-BMS) and the FED type,
//  neither of which exists yet. Add when those are built.
// ---------------------------------------------------------------------------
public sealed class AvsAudioPipeline
{
    private const double SpeedOfSound = 343.0;   // m/s, ~20 C

    private readonly double _vadEnergyThreshold;

    public AvsAudioPipeline(double vadEnergyThreshold = 0.01)
    {
        _vadEnergyThreshold = vadEnergyThreshold;
    }

    // Main entry: audio + geometry -> a localisation result.
    public BasicAudioSceneDescriptors Process(BasicAudioObject audio, MicrophoneArrayGeometry geometry)
    {
        if (audio is null) throw new ArgumentNullException(nameof(audio));
        if (geometry is null) throw new ArgumentNullException(nameof(geometry));

        var wav = audio.Data;                      // base64-decoded WAV bytes
        var (channels, sampleRate, samples) = DecodeWavPcm16(wav);

        if (channels != geometry.MicrophoneArrayAttributes.NumberofMicrophones)
        {
            // Not fatal, but the geometry must match the capture to localise.
            // A single channel is the degraded fallback: VAD only, no DOA.
        }

        // ---- VAD (real, energy-based) --------------------------------------
        var speaking = DetectVoiceActivity(samples, channels);

        // ---- DOA (real, GCC-PHAT) ------------------------------------------
        double? azimuthDeg = null;
        if (speaking && channels >= 2)
        {
            azimuthDeg = EstimateAzimuthGccPhat(samples, channels, sampleRate, geometry);
        }

        // Build the real output: BasicAudioSceneDescriptors (OSD-BAS). Because AVS
        // captures Basic Objects, this is the BASIC (flat) scene, not the
        // hierarchical AudioSceneDescriptors ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â one entry per located speaking
        // human, each carrying the source audio and its direction as a PointOfView.
        var entries = new List<BasicAudioSceneEntry>();
        if (speaking)
        {
            entries.Add(new BasicAudioSceneEntry
            {
                AudioObjectIDOrAudioObject = audio,   // the located Basic Audio Object
                // DOA azimuth expressed as the entry's PointOfView: direction as a
                // spherical bearing (r unknown at this stage, theta = azimuth).
                PointOfView = new PointOfView
                {
                    PointOfViewID = Guid.NewGuid().ToString(),
                    SpherPosition = azimuthDeg is double az
                        ? new double[] { 0.0, 0.0, az }   // (r, phi, theta=azimuth)
                        : null
                }
            });
        }

        return new BasicAudioSceneDescriptors
        {
            BasicAudioSceneDescriptorsID = Guid.NewGuid().ToString(),
            AudioObjectCount = entries.Count,
            BasicAudioSceneDescriptorsEntries = entries
            // EXTENSION POINT: multi-source DOA -> multiple entries; ListenerPointOfView
            // and AcousticProfile; fusion with the visual pipeline to associate each
            // direction with a face (produces BasicAudioVisualSceneDescriptors, OSD-BMS).
        };
    }

    // ---- Voice Activity Detection (REAL, usable) ----------------------------
    // Energy over the first channel vs a threshold. Deliberately simple and
    // dependency-free so the pipeline runs; swap for a model-based VAD at the
    // seam below.
    private bool DetectVoiceActivity(short[][] samples, int channels)
    {
        if (samples.Length == 0 || samples[0].Length == 0) return false;

        var ch0 = samples[0];
        double sumSq = 0;
        for (int i = 0; i < ch0.Length; i++)
        {
            double s = ch0[i] / 32768.0;
            sumSq += s * s;
        }
        double rms = Math.Sqrt(sumSq / ch0.Length);
        return rms >= _vadEnergyThreshold;

        // EXTENSION POINT: replace with sherpa-onnx VAD or a WebRTC-VAD binding
        // for robust speech/non-speech under cabin noise.
    }

    // ---- Direction of Arrival (REAL, GCC-PHAT) ------------------------------
    // Estimates azimuth from the time-difference-of-arrival between the first
    // two microphones, using their CartPosition from the geometry. GCC-PHAT is
    // model-free and robust to reverberation. Single-pair here (2 mics); the
    // extension point generalises to N-pairs / multi-source.
    private double EstimateAzimuthGccPhat(
        short[][] samples, int channels, int sampleRate, MicrophoneArrayGeometry geometry)
    {
        var mics = geometry.MicrophoneAttributes;
        // Use mic 0 and mic 1 (first pair). Their positions give the baseline.
        var p0 = mics[0].MicrophonePointOfView.CartPosition;   // (X,Y,Z) m
        var p1 = mics[1].MicrophonePointOfView.CartPosition;
        double baseline = Distance(p0, p1);                    // metres

        // TDOA (samples) between channel 0 and channel 1 via GCC-PHAT.
        int tdoaSamples = GccPhatDelay(samples[0], samples[1], sampleRate);
        double tdoaSeconds = (double)tdoaSamples / sampleRate;

        // cos(theta) = (c * tdoa) / baseline, clamped to [-1, 1].
        double cosTheta = (SpeedOfSound * tdoaSeconds) / Math.Max(baseline, 1e-6);
        cosTheta = Math.Max(-1.0, Math.Min(1.0, cosTheta));
        double azimuthRad = Math.Acos(cosTheta);
        return azimuthRad * 180.0 / Math.PI;

        // EXTENSION POINT: combine all mic pairs (least-squares over the array
        // geometry) for a full 2-D/3-D bearing and multiple simultaneous sources.
    }

    // GCC-PHAT: cross-correlation whitened by magnitude, argmax = delay in
    // samples. Real implementation over a naive DFT (fine for short frames;
    // swap for an FFT library for long frames).
    private int GccPhatDelay(short[] a, short[] b, int sampleRate)
    {
        int n = Math.Min(a.Length, b.Length);
        if (n == 0) return 0;

        // Real DFTs of a and b.
        var (ar, ai) = Dft(a, n);
        var (br, bi) = Dft(b, n);

        // Cross-spectrum A * conj(B), PHAT-normalised by magnitude.
        var cr = new double[n];
        var ci = new double[n];
        for (int k = 0; k < n; k++)
        {
            double rr = ar[k] * br[k] + ai[k] * bi[k];   // real of A*conj(B)
            double ii = ai[k] * br[k] - ar[k] * bi[k];   // imag of A*conj(B)
            double mag = Math.Sqrt(rr * rr + ii * ii);
            if (mag < 1e-12) mag = 1e-12;
            cr[k] = rr / mag;
            ci[k] = ii / mag;
        }

        // Inverse DFT -> cross-correlation; argmax gives the delay.
        var corr = IdftReal(cr, ci, n);
        int best = 0; double bestVal = double.NegativeInfinity;
        for (int lag = 0; lag < n; lag++)
        {
            if (corr[lag] > bestVal) { bestVal = corr[lag]; best = lag; }
        }
        // Map [0, n) to signed lag [-n/2, n/2).
        return best >= n / 2 ? best - n : best;
    }

    // ---- WAV / DSP helpers (real) -------------------------------------------
    // Decode a canonical PCM16 WAV into per-channel short[] arrays.
    private static (int channels, int sampleRate, short[][] samples) DecodeWavPcm16(byte[] wav)
    {
        if (wav.Length < 44) return (0, 0, Array.Empty<short[]>());

        int channels   = BitConverter.ToInt16(wav, 22);
        int sampleRate  = BitConverter.ToInt32(wav, 24);
        int bitsPerSamp = BitConverter.ToInt16(wav, 34);
        if (channels < 1 || bitsPerSamp != 16) return (channels, sampleRate, Array.Empty<short[]>());

        // Find the 'data' chunk (canonical writer puts it at 36, but scan to be safe).
        int pos = 12;
        int dataOffset = 44, dataLen = wav.Length - 44;
        while (pos + 8 <= wav.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
            int len = BitConverter.ToInt32(wav, pos + 4);
            if (id == "data") { dataOffset = pos + 8; dataLen = len; break; }
            pos += 8 + len + (len & 1);
        }
        dataLen = Math.Min(dataLen, wav.Length - dataOffset);

        int frameCount = dataLen / (2 * channels);
        var samples = new short[channels][];
        for (int c = 0; c < channels; c++) samples[c] = new short[frameCount];

        int idx = dataOffset;
        for (int f = 0; f < frameCount; f++)
            for (int c = 0; c < channels; c++)
            {
                samples[c][f] = BitConverter.ToInt16(wav, idx);
                idx += 2;
            }
        return (channels, sampleRate, samples);
    }

    private static double Distance(double[] a, double[] b)
    {
        double dx = a[0] - b[0], dy = a[1] - b[1], dz = a[2] - b[2];
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    // Naive DFT (O(n^2)) ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â adequate for short localisation frames; replace with
    // an FFT (e.g. MathNet.Numerics) for long windows.
    private static (double[] re, double[] im) Dft(short[] x, int n)
    {
        var re = new double[n]; var im = new double[n];
        for (int k = 0; k < n; k++)
        {
            double sr = 0, si = 0;
            for (int t = 0; t < n; t++)
            {
                double ang = -2.0 * Math.PI * k * t / n;
                double v = x[t] / 32768.0;
                sr += v * Math.Cos(ang);
                si += v * Math.Sin(ang);
            }
            re[k] = sr; im[k] = si;
        }
        return (re, im);
    }

    private static double[] IdftReal(double[] re, double[] im, int n)
    {
        var outp = new double[n];
        for (int t = 0; t < n; t++)
        {
            double s = 0;
            for (int k = 0; k < n; k++)
            {
                double ang = 2.0 * Math.PI * k * t / n;
                s += re[k] * Math.Cos(ang) - im[k] * Math.Sin(ang);
            }
            outp[t] = s / n;
        }
        return outp;
    }
}

// NB: the pipeline now returns the REAL type BasicAudioSceneDescriptors
// (OSD-BAS-V1.5, Mpai.Core.OSD) ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â the Basic (flat) audio scene, appropriate
// because AVS captures Basic Objects. The full hierarchical AudioSceneDescriptors
// (ASD) is deliberately NOT used here; CAE-ASE already builds BAS from
// BasicAudioObjects (CreateBasicScene/MaterializeBasicScene) and this pipeline
// composes with that path rather than duplicating it.
