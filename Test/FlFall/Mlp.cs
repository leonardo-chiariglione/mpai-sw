using System.Numerics;

namespace Mpai.Aih.FlFall;

// The network of the thesis: 32 inputs, hidden layers of 256, 128 and 64 (ReLU, dropout 0.3), one output (the logit of "fall"). Its weights are ONE array (so that the
// Back End's methods treat a model as numbers, as FlMethods does). Training: Adam (learning rate 0.0008, weight decay 0.0001), batches of 256, logistic loss on a target
// that may be soft (a probability: the teacher's answer).
public sealed class Mlp
{
    public static readonly int[] Sizes = [32, 256, 128, 64, 1];
    public static int ParameterCount { get; } = Enumerable.Range(0, Sizes.Length - 1).Sum(l => Sizes[l] * Sizes[l + 1] + Sizes[l + 1]);
    private static int W(int l) => Enumerable.Range(0, l).Sum(k => Sizes[k] * Sizes[k + 1] + Sizes[k + 1]);      // where layer l's weights begin (its biases follow them)

    public static float[] Init(Random rng)
    {
        var p = new float[ParameterCount];
        for (var l = 0; l < Sizes.Length - 1; l++)
        {
            var bound = Math.Sqrt(6.0 / Sizes[l]);                           // He-uniform for ReLU
            for (var i = 0; i < Sizes[l] * Sizes[l + 1]; i++) p[W(l) + i] = (float)((rng.NextDouble() * 2 - 1) * bound * (l == Sizes.Length - 2 ? 0.5 : 1));
        }
        return p;
    }

    // The logit for one input (no dropout).
    public static float Logit(float[] p, float[] x)
    {
        var a = x;
        for (var l = 0; l < Sizes.Length - 1; l++)
        {
            int nIn = Sizes[l], nOut = Sizes[l + 1], w = W(l), b = w + nIn * nOut;
            var z = new float[nOut];
            for (var o = 0; o < nOut; o++) { z[o] = Dot(p.AsSpan(w + o * nIn, nIn), a) + p[b + o]; if (l < Sizes.Length - 2 && z[o] < 0) z[o] = 0; }
            a = z;
        }
        return a[0];
    }

    public static double[] Probabilities(float[] p, IReadOnlyList<float[]> xs) => xs.Select(x => 1 / (1 + Math.Exp(-Logit(p, x)))).ToArray();

    // epochs of Adam on (x, target), starting from p; returns the new parameters. targets in [0, 1].
    public static float[] Train(float[] start, IReadOnlyList<float[]> xs, IReadOnlyList<float> ys, int epochs, Random rng, float lr = 0.0008f, float wd = 0.0001f,
        int batch = 256, float dropout = 0.3f)
    {
        var p = (float[])start.Clone();
        var m = new float[p.Length]; var v = new float[p.Length]; var g = new float[p.Length];
        int t = 0; var order = Enumerable.Range(0, xs.Count).ToArray();
        int L = Sizes.Length - 1;
        var acts = new float[L + 1][]; var masks = new float[L][]; var deltas = new float[L + 1][];
        for (var l = 0; l <= L; l++) { acts[l] = new float[Sizes[l]]; deltas[l] = new float[Sizes[l]]; }
        for (var l = 0; l < L; l++) masks[l] = new float[Sizes[l + 1]];
        for (var e = 0; e < epochs; e++)
        {
            rng.Shuffle(order);
            for (var s0 = 0; s0 < order.Length; s0 += batch)
            {
                Array.Clear(g);
                var n = Math.Min(batch, order.Length - s0);
                for (var k = 0; k < n; k++)
                {
                    var i = order[s0 + k];
                    xs[i].CopyTo(acts[0], 0);
                    for (var l = 0; l < L; l++)
                    {
                        int nIn = Sizes[l], nOut = Sizes[l + 1], w = W(l), b = w + nIn * nOut;
                        for (var o = 0; o < nOut; o++)
                        {
                            var z = Dot(p.AsSpan(w + o * nIn, nIn), acts[l]) + p[b + o];
                            if (l < L - 1)
                            {
                                var keep = rng.NextDouble() >= dropout ? 1f / (1 - dropout) : 0f;
                                masks[l][o] = z > 0 ? keep : 0;
                                z = z > 0 ? z * keep : 0;
                            }
                            acts[l + 1][o] = z;
                        }
                    }
                    var prob = 1 / (1 + MathF.Exp(-acts[L][0]));
                    deltas[L][0] = (prob - ys[i]) / n;
                    for (var l = L - 1; l >= 0; l--)
                    {
                        int nIn = Sizes[l], nOut = Sizes[l + 1], w = W(l), b = w + nIn * nOut;
                        Array.Clear(deltas[l]);
                        for (var o = 0; o < nOut; o++)
                        {
                            var d = deltas[l + 1][o];
                            if (d == 0) continue;
                            g[b + o] += d;
                            Axpy(d, acts[l], g.AsSpan(w + o * nIn, nIn));
                            if (l > 0) Axpy(d, p.AsSpan(w + o * nIn, nIn), deltas[l]);
                        }
                        if (l > 0) for (var j = 0; j < nIn; j++) deltas[l][j] *= masks[l - 1][j];
                    }
                }
                t++;
                float b1 = 0.9f, b2 = 0.999f, c1 = 1 - MathF.Pow(b1, t), c2 = 1 - MathF.Pow(b2, t);
                for (var j = 0; j < p.Length; j++)
                {
                    var gj = g[j] + wd * p[j];
                    m[j] = b1 * m[j] + (1 - b1) * gj; v[j] = b2 * v[j] + (1 - b2) * gj * gj;
                    p[j] -= lr * (m[j] / c1) / (MathF.Sqrt(v[j] / c2) + 1e-8f);
                }
            }
        }
        return p;
    }

    private static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        int i = 0, n = a.Length, w = Vector<float>.Count; var acc = Vector<float>.Zero;
        for (; i <= n - w; i += w) acc += new Vector<float>(a.Slice(i, w)) * new Vector<float>(b.Slice(i, w));
        var s = Vector.Sum(acc); for (; i < n; i++) s += a[i] * b[i];
        return s;
    }

    private static void Axpy(float k, ReadOnlySpan<float> x, Span<float> y)
    {
        int i = 0, n = x.Length, w = Vector<float>.Count; var kv = new Vector<float>(k);
        for (; i <= n - w; i += w) (new Vector<float>(y.Slice(i, w)) + kv * new Vector<float>(x.Slice(i, w))).CopyTo(y.Slice(i, w));
        for (; i < n; i++) y[i] += k * x[i];
    }
}
