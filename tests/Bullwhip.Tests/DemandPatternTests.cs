using Bullwhip.Core.Demand;
using Bullwhip.Core.Rng;
using Xunit;

namespace Bullwhip.Tests;

// DECISIONS C51: mean profiles m_t and the generator D_t = m_t + sigma * N(0,1). mu = 100, sigma = 10.
public class DemandPatternTests
{
    private static readonly DemandPatternParameters P = new();
    private const double Mu = 100;

    private static double M(DemandPattern d, int t) => DemandPatterns.MeanProfile(d, P, t);

    private static SplitMix64 Rng(ulong seed) => new(SplitMix64.Derive(20261103UL, seed));

    // Stable: m_t = 100 everywhere; path identical to Ar1Demand with rho 0 (D_0 = mu, no draw at t = 0).
    [Fact]
    public void Stable_ProfileIsMu_AndPathEqualsAr1WithRhoZero()
    {
        for (int t = 0; t < 2400; t += 37)
        {
            Assert.Equal(Mu, M(DemandPattern.Stable, t));
        }

        var a = DemandPatterns.Generate(DemandPattern.Stable, P, 2200, Rng(7));
        var b = Ar1Demand.Generate(2200, 100, 0, 10, Rng(7));
        Assert.Equal(b, a);
        Assert.Equal(Mu, a[0]);
    }

    // Seasonal: t = 13 is a quarter period, sin(pi/2) = 1, so m_13 = 100 * 1.2 = 120; t = 0 and 26 give 100.
    [Fact]
    public void Seasonal_QuarterPeriodPeak_Periodic_AndMeanOverYears()
    {
        Assert.Equal(Mu * (1 + 0.2 * Math.Sin(2 * Math.PI * 13 / 52)), M(DemandPattern.Seasonal, 13), 10);
        Assert.Equal(120.0, M(DemandPattern.Seasonal, 13), 9);
        Assert.Equal(100.0, M(DemandPattern.Seasonal, 26), 9);
        for (int t = 0; t < 200; t++)
        {
            Assert.Equal(M(DemandPattern.Seasonal, t), M(DemandPattern.Seasonal, t + 52), 9);
        }

        // 40 full years = 2080 weeks: the profile averages to mu exactly, so the sample mean has std 10/sqrt(2080) = 0.22;
        // tolerance 5 standard errors = 1.1.
        const int n = 2080;
        var d = DemandPatterns.Generate(DemandPattern.Seasonal, P, n, Rng(3));
        Assert.InRange(d.Average(), Mu - 5 * 10 / Math.Sqrt(n), Mu + 5 * 10 / Math.Sqrt(n));
    }

    // Trend: g = 0.5 * 100 / 2000 = 0.025 per week; midpoint t = 1200 gives 100 + 0.025 * 1000 = 125; t = 2200 gives 100 + 50 = 150.
    [Fact]
    public void Trend_FlatBeforeT0_LinearToOnePointFiveMu()
    {
        Assert.Equal(0.025, P.TrendSlope, 12);
        Assert.Equal(Mu, M(DemandPattern.Trend, 0));
        Assert.Equal(Mu, M(DemandPattern.Trend, 199));
        Assert.Equal(Mu, M(DemandPattern.Trend, 200));
        Assert.Equal(125.0, M(DemandPattern.Trend, 1200), 9);
        Assert.Equal(150.0, M(DemandPattern.Trend, 2200), 9);
    }

    // Jump: 100 before t = 1200 (middle of the measurement window 200..2199), 130 from t = 1200 on.
    [Fact]
    public void Jump_StepAtJumpTime()
    {
        Assert.Equal(Mu, M(DemandPattern.Jump, 0));
        Assert.Equal(Mu, M(DemandPattern.Jump, 1199));
        Assert.Equal(130.0, M(DemandPattern.Jump, 1200), 9);
        Assert.Equal(130.0, M(DemandPattern.Jump, 2199), 9);
    }

    // Promotion: weeks 0-3 -> 150, weeks 4-11 -> 70, weeks 12-51 -> 100, every year (t = 52k + w).
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(20)]
    public void Promotion_RepeatsEveryYear(int k)
    {
        for (int w = 0; w < 52; w++)
        {
            double expected = w < 4 ? 150.0 : w < 12 ? 70.0 : 100.0;
            Assert.Equal(expected, M(DemandPattern.Promotion, 52 * k + w), 9);
        }
    }

    // Noise: over 400 replications x 2200 weeks (880 000 values) the std of D_t - m_t is 10 with relative standard error
    // about 1/sqrt(2 * 880000) = 0.075 %; tolerance 1 % (0.1) is over 10 standard errors. The mean of the residuals has
    // standard error 10/sqrt(880000) = 0.011; tolerance 0.1.
    [Theory]
    [InlineData(DemandPattern.Stable)]
    [InlineData(DemandPattern.Seasonal)]
    [InlineData(DemandPattern.Trend)]
    [InlineData(DemandPattern.Jump)]
    [InlineData(DemandPattern.Promotion)]
    public void Noise_HasStdTenAroundProfile(DemandPattern pattern)
    {
        const int reps = 400, len = 2200;
        double sum = 0, sumSq = 0;
        for (int r = 0; r < reps; r++)
        {
            var d = DemandPatterns.Generate(pattern, P, len, Rng((ulong)r));
            for (int t = 1; t < len; t++)
            {
                double e = d[t] - M(pattern, t);
                sum += e;
                sumSq += e * e;
            }
        }

        double n = reps * (len - 1.0);
        double mean = sum / n;
        double sd = Math.Sqrt(sumSq / n - mean * mean);
        Assert.InRange(mean, -0.1, 0.1);
        Assert.InRange(sd, 9.9, 10.1);
    }

    // C52: Seasonal with A = 0 uses the same noise as Stable and the same D_0, so the paths are bit-identical.
    [Fact]
    public void SeasonalWithZeroAmplitude_IsBitIdenticalToStable()
    {
        var p = P with { Amplitude = 0 };
        var a = DemandPatterns.Generate(DemandPattern.Seasonal, p, 2200, Rng(11));
        var b = DemandPatterns.Generate(DemandPattern.Stable, p, 2200, Rng(11));
        for (int t = 0; t < a.Length; t++)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(b[t]), BitConverter.DoubleToInt64Bits(a[t]));
        }
    }

    // C52: D_0 is the mean profile without noise; D_t for t >= 1 uses the t-th normal of the stream, as in Stable.
    [Theory]
    [InlineData(DemandPattern.Seasonal)]
    [InlineData(DemandPattern.Trend)]
    [InlineData(DemandPattern.Jump)]
    [InlineData(DemandPattern.Promotion)]
    public void FirstValueIsMeanProfileAtZero_AndNoiseSharedWithStable(DemandPattern pattern)
    {
        var d = DemandPatterns.Generate(pattern, P, 300, Rng(5));
        var stable = DemandPatterns.Generate(DemandPattern.Stable, P, 300, Rng(5));
        Assert.Equal(M(pattern, 0), d[0]);
        for (int t = 1; t < 300; t++)
        {
            Assert.Equal(stable[t] - Mu, d[t] - M(pattern, t), 9);
        }
    }

    // Non-default mu = 80: Seasonal Period 26 (t = 13 is a half period, t = 26 is full), Promotion period 40.
    [Fact]
    public void NonDefaultMuAndParameters_GiveHandComputableValues()
    {
        var s = new DemandPatternParameters { Mu = 80, Period = 26, Amplitude = 0.25 };
        Assert.Equal(80.0, DemandPatterns.MeanProfile(DemandPattern.Seasonal, s, 0), 9);
        Assert.Equal(80.0 * (1 + 0.25 * Math.Sin(2 * Math.PI * 6 / 26)), DemandPatterns.MeanProfile(DemandPattern.Seasonal, s, 6), 9);
        Assert.Equal(80.0, DemandPatterns.MeanProfile(DemandPattern.Seasonal, s, 13), 9);
        Assert.Equal(80.0, DemandPatterns.MeanProfile(DemandPattern.Seasonal, s, 26), 9);
        Assert.Equal(DemandPatterns.MeanProfile(DemandPattern.Seasonal, s, 5), DemandPatterns.MeanProfile(DemandPattern.Seasonal, s, 31), 9);

        var q = new DemandPatternParameters { Mu = 80, PromoPeriod = 40 };
        for (int k = 0; k < 3; k++)
        {
            Assert.Equal(120.0, DemandPatterns.MeanProfile(DemandPattern.Promotion, q, 40 * k + 3), 9);
            Assert.Equal(56.0, DemandPatterns.MeanProfile(DemandPattern.Promotion, q, 40 * k + 4), 9);
            Assert.Equal(56.0, DemandPatterns.MeanProfile(DemandPattern.Promotion, q, 40 * k + 11), 9);
            Assert.Equal(80.0, DemandPatterns.MeanProfile(DemandPattern.Promotion, q, 40 * k + 12), 9);
            Assert.Equal(80.0, DemandPatterns.MeanProfile(DemandPattern.Promotion, q, 40 * k + 39), 9);
        }
    }

    [Fact]
    public void InvalidParameters_Throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DemandPatterns.MeanProfile(DemandPattern.Seasonal, P with { Period = 0 }, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DemandPatterns.MeanProfile(DemandPattern.Promotion, P with { PromoPeriod = 0 }, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DemandPatterns.MeanProfile(DemandPattern.Promotion, P with { PromoPeriod = -3 }, 1));
        Assert.Throws<ArgumentException>(() => DemandPatterns.MeanProfile(DemandPattern.Trend, P with { TrendEndTime = 200 }, 1));
    }
}
