using Bullwhip.Core.Analytic;
using Bullwhip.Core.Forecast;
using Bullwhip.Core.Rng;
using Bullwhip.Core.Stats;
using Xunit;

namespace Bullwhip.Tests;

public class RngAndStatsTests
{
    [Fact]
    public void SameSeedGivesSameSequence()
    {
        var a = new SplitMix64(42);
        var b = new SplitMix64(42);
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(a.NextStandardNormal(), b.NextStandardNormal());
        }
    }

    [Fact]
    public void NormalSamplerHasMeanZeroAndVarianceOne()
    {
        var rng = new SplitMix64(7);
        const int n = 1_000_000;
        double sum = 0.0;
        double sumSq = 0.0;
        for (int i = 0; i < n; i++)
        {
            double x = rng.NextStandardNormal();
            sum += x;
            sumSq += x * x;
        }

        double mean = sum / n;
        double variance = sumSq / n - mean * mean;
        Assert.InRange(mean, -0.005, 0.005);
        Assert.InRange(variance, 0.99, 1.01);
    }

    [Fact]
    public void NormalQuantileMatchesKnownValues()
    {
        Assert.Equal(0.5, Normal.Cdf(0.0), 6);
        Assert.Equal(1.959964, Normal.Quantile(0.975), 3);
        Assert.Equal(-1.644854, Normal.Quantile(0.05), 3);
    }

    [Fact]
    public void MovingAverageMatchesDirectMean()
    {
        var ma = new MovingAverage(3);
        double[] xs = { 1, 2, 3, 4, 10, -2 };
        foreach (double x in xs)
        {
            ma.Observe(x);
        }

        Assert.Equal((4 + 10 + -2) / 3.0, ma.Mean, 12);
    }

    [Fact]
    public void ChenSingleStageFormulaMatchesWorkedValue()
    {
        // Lp = 2, p = 8, iid: 1 + 2/8 + 2·4/64 = 1.625 (plan §5, V1 pre-check)
        Assert.Equal(1.625, ChenFormulas.SingleStageBullwhipRatio(2, 8, 0.0), 12);
    }
}
