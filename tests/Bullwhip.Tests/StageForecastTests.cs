using Bullwhip.Core.Chain;
using Bullwhip.Core.Forecast;
using Xunit;

namespace Bullwhip.Tests;

public class StageForecastTests
{
    [Fact]
    public void EmptyStart_R4_Phase0_MeanIsExactAfterEachClose()
    {
        var f = new StageForecast(8, 4);
        Assert.Equal(0.0, f.Mean);

        // Phase 0: the first close is at t = 0 after one real period, then every R = 4 periods (t = 4, 8, ...).
        for (int t = 0; t < 20; t++)
        {
            f.Observe(100.0);
            if (t % 4 == 0)
            {
                f.CloseBucket();
                Assert.Equal(100.0, f.Mean);
            }
        }
    }

    [Fact]
    public void EmptyStart_R4_Phase2_FirstBucketHasThreePeriods()
    {
        var f = new StageForecast(8, 4);
        for (int t = 0; t <= 2; t++)
        {
            f.Observe(100.0);
        }

        f.CloseBucket();
        Assert.Equal(100.0, f.Mean);
    }

    [Fact]
    public void AbsErrorEwma_RunningMeanStartThenRecursion()
    {
        // α_s = 0.3: weights max(0.3, 1/n) = 1, 1/2, 1/3, then 0.3 (n = 4: 1/4 < 0.3). |e| = 2, 4, 6, 8, 10.
        var f = new AbsErrorEwma(0.3);
        Assert.Equal(0.0, f.Mad);
        var expected = new[] { 2.0, 3.0, 4.0, 0.3 * 8 + 0.7 * 4.0, 0.3 * 10 + 0.7 * (0.3 * 8 + 0.7 * 4.0) };
        var errors = new[] { -2.0, 4.0, -6.0, 8.0, -10.0 };
        for (int i = 0; i < errors.Length; i++)
        {
            f.Add(errors[i]);
            Assert.True(Math.Abs(f.Mad - expected[i]) < 1e-12, $"n={i + 1}: {f.Mad} vs {expected[i]}");
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new AbsErrorEwma(0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AbsErrorEwma(1.5));

        // SerialChain: α_s must be set, R = 1 only; constant demand at the prefilled mean gives zero errors and σ̂ = 0.
        var cfg = new ChainConfig { LeadTimes = new double[] { 2 }, Window = 8, Safety = SafetySource.EwmaAbsError };
        var d = Enumerable.Repeat(100.0, 50).ToArray();
        Assert.Throws<ArgumentException>(() => SerialChain.Run(cfg, d));
        Assert.Throws<ArgumentException>(() => SerialChain.Run(cfg with { AbsErrorSmoothing = 0.2, R = 4 }, d));
        Assert.Equal(0.0, SerialChain.Run(cfg with { AbsErrorSmoothing = 0.2, Z = new[] { 1.0 }, InitialMean = 100 }, d).MeanSigmaHat[0]);
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(8, 4, 0)]
    [InlineData(8, 4, 3)]
    [InlineData(8, 2, 1)]
    public void Prefilled_MeanIsMuExactly(int window, int r, int phase)
    {
        var f = new StageForecast(window, r);
        f.Prefill(100.0, phase);
        Assert.Equal(100.0, f.Mean);

        // First close at t = phase after phase + 1 real periods at mu: still mu.
        for (int t = 0; t <= phase; t++)
        {
            f.Observe(100.0);
        }

        f.CloseBucket();
        Assert.Equal(100.0, f.Mean);
    }
}
