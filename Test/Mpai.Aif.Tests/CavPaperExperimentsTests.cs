using Mpai.Cav.Paper;

namespace Mpai.Aif.Tests;

// N-35: the experiments for the paper, on one context (the CAV following a car that brakes): each injection at the scene layer does what it says and the
// run goes through; the three motion implementations behind the one interface each drive the run to its end on every surface.
[Trait("Group", "Fast")]
public class CavPaperExperimentsTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("miss-p20")]
    [InlineData("miss-stretch2")]
    [InlineData("phantom-pedestrian-1s")]
    [InlineData("phantom-car-1s")]
    [InlineData("phantom-obstacle-frame")]
    public void AnInjectionChangesTheSceneAndTheRunGoesThrough(string injection)
    {
        var r = Runner.Run(0, new Config("core", Surface.Dry, 150, 0.15, 1, injection));
        Assert.DoesNotContain("error", r.Result);
        Assert.Equal(injection == "none", r.Injected == 0);
        if (injection.StartsWith("phantom")) Assert.InRange(r.Injected, 1, 10);   // one frame, or 1 s at 0.1 s
        Assert.True(r.Harm >= 0);
    }

    [Theory]
    [InlineData("core")]
    [InlineData("idm")]
    [InlineData("planner")]
    public void EachMotionImplementationDrivesBehindTheSameInterface(string motion)
    {
        foreach (var surface in new[] { Surface.Dry, Surface.Ice })
        {
            var r = Runner.Run(0, new Config(motion, surface, 150, 0.15, 1));
            Assert.DoesNotContain("error", r.Result);
            Assert.True(r.Time > 0);
            Assert.Equal(motion, r.Motion);
        }
    }

    [Fact]
    public void TheHarmScoreWeighsAPedestrianAboveACarAndAStopAboveNothing()
    {
        Assert.True(Runner.Harm(true, "Pedestrian", 5, false, 0, false, 10, 10, 0) > Runner.Harm(true, "Car", 5, false, 0, false, 10, 10, 0));
        Assert.True(Runner.Harm(false, null, 0, false, 0, true, 90, 30, 0) > Runner.Harm(false, null, 0, false, 0, false, 30, 30, 0));
        Assert.Equal(0, Runner.Harm(false, null, 0, false, 0, false, 30, 30, 0));
    }
}

// The whole experiments are run by the program (Test\CavPaper: --matrix, --failures), not by the test suite: 7,920 and 1,584 runs.
