using Bullwhip.Core.Costs;
using KSweep;
using Xunit;

namespace Bullwhip.Tests;

// The KSweep option --sigma (sensitivity of chapter 4): parsing, default, rejection of invalid values, refusal on
// modes that would ignore it, and that the chosen standard deviation reaches the stable demand (sd = sigma, DECISIONS C50).
public class SigmaOptionTests
{
    private static readonly CostParameters Costs = new();

    [Fact]
    public void Default_IsBaseSigmaAndLeavesDemandUnchanged()
    {
        var (option, rest) = DemandOption.Extract(new[] { "base-kpi", "out.txt" });
        Assert.Equal(10, option.Sigma);
        Assert.True(option.IsDefault);
        Assert.Equal(new[] { "base-kpi", "out.txt" }, rest);
    }

    [Fact]
    public void Sigma_IsParsedRemovedAndNamedInTheReport()
    {
        var (option, rest) = DemandOption.Extract(new[] { "countermeasure", "--sigma", "5", "lt", "out.txt" });
        Assert.Equal(5, option.Sigma);
        Assert.False(option.IsDefault);
        Assert.Equal(new[] { "countermeasure", "lt", "out.txt" }, rest);
        Assert.Equal(5, option.ApplyTo(new SweepSettings()).Sigma);
        Assert.Contains("sigma = 5", option.Describe());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    public void InvalidSigma_IsRejected(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => DemandOption.Extract(new[] { "--sigma", value }));
    }

    [Fact]
    public void SigmaWithoutValue_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => DemandOption.Extract(new[] { "--sigma" }));
    }

    [Fact]
    public void Sigma_IsRefusedByModesThatIgnoreIt()
    {
        var (option, _) = DemandOption.Extract(new[] { "--sigma", "20" });
        Assert.Throws<ArgumentException>(() => option.EnsureSupported("upstream"));
        option.EnsureSupported("countermeasure");
    }

    // Stable demand has stationary sd = sigma; 2199 weeks give a standard error of about 1.5 % of sigma, so 5 % is a safe bound.
    [Fact]
    public void Sigma_SetsTheStandardDeviationOfStableDemand()
    {
        var (option, _) = DemandOption.Extract(new[] { "--sigma", "20" });
        var settings = option.ApplyTo(new SweepSettings { Costs = Costs });
        double[] demand = new ChainCostEvaluator(settings).Demand(Costs, 1000);
        double mean = demand.Average();
        double sd = Math.Sqrt(demand.Sum(v => (v - mean) * (v - mean)) / (demand.Length - 1));
        Assert.InRange(sd, 19.0, 21.0);
    }
}
