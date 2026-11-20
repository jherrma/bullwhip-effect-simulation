using Bullwhip.Core.Costs;
using Bullwhip.Core.Demand;
using KSweep;
using Xunit;

namespace Bullwhip.Tests;

// The KSweep options --pattern and --rho (DECISIONS C50-C52): parsing, defaults, refusal on modes that would ignore them,
// and that the chosen demand reaches the evaluator's demand path (one held-out replication, 200 + 2000 weeks).
public class DemandOptionTests
{
    private static readonly CostParameters Costs = new();
    private const double Sigma = 10;

    private static double[] DemandFor(params string[] args)
    {
        var (option, _) = DemandOption.Extract(args);
        var settings = option.ApplyTo(new SweepSettings { Costs = Costs });
        return new ChainCostEvaluator(settings).Demand(Costs, 1000);
    }

    private static (double Mean, double Sd) MeanAndSd(IReadOnlyList<double> x)
    {
        double mean = x.Average();
        return (mean, Math.Sqrt(x.Sum(v => (v - mean) * (v - mean)) / (x.Count - 1)));
    }

    // Without the options nothing is removed from the arguments and the demand is the stable base case, bit for bit.
    [Fact]
    public void NoOption_LeavesArgumentsAndBaseDemandUnchanged()
    {
        string[] args = { "countermeasure", "lt", "--split", "0.02", "0.08", "out.txt" };
        var (option, rest) = DemandOption.Extract(args);
        Assert.True(option.IsDefault);
        Assert.Equal(args, rest);
        var settings = option.ApplyTo(new SweepSettings());
        Assert.Equal(DemandPattern.Stable, settings.Pattern);
        Assert.Equal(0, settings.Rho);
        Assert.Equal(new ChainCostEvaluator(new SweepSettings { Costs = Costs }).Demand(Costs, 1000), DemandFor(args));
    }

    // --pattern: the residual D_t - m_t of the chosen pattern has mean 0 and standard deviation sigma = 10. Had the stable
    // demand been used instead, the residual would carry the profile: its sd would be about 17 (seasonal), 16 (trend),
    // 18 (jump) or 15 (promotion), far outside the 5 % band. Bounds: 4 standard errors of the mean (10/sqrt(2199) = 0.21)
    // and 5 % on the sd (the standard error of the sd is about 1.5 %).
    [Theory]
    [InlineData("seasonal", DemandPattern.Seasonal)]
    [InlineData("trend", DemandPattern.Trend)]
    [InlineData("jump", DemandPattern.Jump)]
    [InlineData("Promotion", DemandPattern.Promotion)]
    public void PatternOption_AppliesMeanProfileToDemand(string name, DemandPattern pattern)
    {
        var (option, rest) = DemandOption.Extract(new[] { "base-kpi", "--pattern", name, "out.txt" });
        Assert.Equal(pattern, option.Pattern);
        Assert.Equal(new[] { "base-kpi", "out.txt" }, rest);

        var d = DemandFor("--pattern", name);
        var p = new DemandPatternParameters();
        var residual = Enumerable.Range(1, d.Length - 1).Select(t => d[t] - DemandPatterns.MeanProfile(pattern, p, t)).ToArray();
        var (mean, sd) = MeanAndSd(residual);
        Assert.InRange(mean, -4 * Sigma / Math.Sqrt(residual.Length), 4 * Sigma / Math.Sqrt(residual.Length));
        Assert.InRange(sd, 0.95 * Sigma, 1.05 * Sigma);
    }

    // --rho: the demand has lag-1 autocorrelation rho and stationary sd sigma = 10 (innovation sigma * sqrt(1 - rho^2), C50).
    // Bounds: 0.06 on the autocorrelation (about 3 standard errors of sqrt((1 - rho^2)/n) at n = 2000) and 8 % on the sd
    // (the sd of a positively correlated series has a larger standard error than in the iid case).
    [Theory]
    [InlineData(-0.3)]
    [InlineData(0.3)]
    [InlineData(0.6)]
    public void RhoOption_SetsAutocorrelationAndKeepsStationarySigma(double rho)
    {
        var (option, _) = DemandOption.Extract(new[] { "k-base", "--rho", rho.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        Assert.Equal(rho, option.Rho);

        var d = DemandFor("--rho", rho.ToString(System.Globalization.CultureInfo.InvariantCulture)).Skip(200).ToArray();
        var (mean, sd) = MeanAndSd(d);
        double lag1 = Enumerable.Range(1, d.Length - 1).Sum(t => (d[t] - mean) * (d[t - 1] - mean)) / d.Sum(v => (v - mean) * (v - mean));
        Assert.InRange(lag1, rho - 0.06, rho + 0.06);
        Assert.InRange(sd, 0.92 * Sigma, 1.08 * Sigma);
    }

    [Theory]
    [InlineData("--pattern", "jump", "--rho", "0.3")]
    [InlineData("--pattern", "weekly")]
    [InlineData("--pattern", "2")]
    [InlineData("--rho", "1")]
    [InlineData("--rho")]
    [InlineData("--rho", "0.3", "--rho", "0.6")]
    public void InvalidOptions_Throw(params string[] args)
    {
        Assert.ThrowsAny<ArgumentException>(() => DemandOption.Extract(args));
    }

    // A mode that does not build on the base sweep would ignore the option; it must refuse it. The default passes everywhere.
    [Fact]
    public void NonDefaultOption_IsRefusedByModesThatIgnoreIt()
    {
        var (jump, _) = DemandOption.Extract(new[] { "--pattern", "jump" });
        foreach (string mode in new[] { "upstream", "larger-beta", "single-stage", "gate-b", "k-sweep", "k-factor", "countermeasure-structural" })
        {
            Assert.Throws<ArgumentException>(() => jump.EnsureSupported(mode));
            new DemandOption().EnsureSupported(mode);
        }

        foreach (string mode in DemandOption.SupportedModes)
        {
            jump.EnsureSupported(mode);
        }
    }
}
