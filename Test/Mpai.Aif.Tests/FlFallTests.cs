using Mpai.Aih.FlFall;
using Xunit;

namespace Mpai.Aif.Tests;

// The fall-detection experiment of 2026-11-19 (Test\FlFall: Leonor Ramos's thesis setting on UP-Fall, run the AIH-HSP way): its network and its measures.
[Trait("Group", "Fast")]
public class FlFallTests
{
    [Fact]
    public void TheNetworkLearnsARareEventThatOneFeatureGivesAway()
    {
        var rng = new Random(3);
        var xs = Enumerable.Range(0, 1500).Select(_ => Enumerable.Range(0, 32).Select(_ => (float)(rng.NextDouble() * 2 - 1)).ToArray()).ToList();
        var ys = xs.Select(x => x[0] > 0.9f ? 1f : 0f).ToList();                     // about 5 % "falls"
        var p = Mlp.Train(Mlp.Init(new Random(1)), xs, ys, 60, new Random(2));
        var m = Metrics.Of(Mlp.Probabilities(p, xs), ys);
        Assert.True(m.PrAuc > 0.9, $"PR-AUC {m.PrAuc}");
        Assert.Equal(Mlp.ParameterCount, p.Length);
        Assert.Equal(32 * 256 + 256 + 256 * 128 + 128 + 128 * 64 + 64 + 64 + 1, Mlp.ParameterCount);
    }

    [Fact]
    public void TheMeasuresOfAKnownRanking()
    {
        // two falls, ranked first and third; threshold 0.5 catches the first fall and one non-fall
        double[] p = [0.9, 0.6, 0.4, 0.1];
        float[] y = [1, 0, 1, 0];
        var m = Metrics.Of(p, y);
        Assert.Equal((1.0 + 2.0 / 3) / 2, m.PrAuc, 6);                                // average precision: precision 1 at recall 0.5, 2/3 at recall 1
        Assert.Equal(0.75, m.RocAuc, 6);                                             // 3 of the 4 fall/non-fall pairs ordered right
        Assert.Equal(0.5, m.MissRate, 6);
        Assert.Equal(0.5, m.FalseAlarmRate, 6);
        Assert.Equal(0.5, m.F1, 6);
    }
}
