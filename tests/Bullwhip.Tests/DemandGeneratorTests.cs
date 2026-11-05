using Bullwhip.Core.Chain;
using Bullwhip.Core.Stats;
using Bullwhip.Core.Validation;
using Xunit;

namespace Bullwhip.Tests;

// Common random numbers and the demand generator (GateA.Demand), not the chain model.
public class DemandGeneratorTests
{
    // Invariant: the same replication gives the identical series, different replications differ, for every parameter set.
    [Fact]
    public void Demand_IsDeterministicPerRep_AndDiffersAcrossReps()
    {
        for (int rep = 0; rep < PropertyTestChains.Seeds; rep++)
        {
            var first = GateA.Demand(rep, 500, 100, 0.5, 20);
            Assert.Equal(first, GateA.Demand(rep, 500, 100, 0.5, 20));
            Assert.NotEqual(first, GateA.Demand(rep + 1, 500, 100, 0.5, 20));
            Assert.NotEqual(GateA.LeadTimeSeed(rep), GateA.LeadTimeSeed(rep + 1));
        }
    }

    // Invariant (common random numbers): two configurations differing only in R see identical customer demand, and
    // the retailer's incoming series is that demand unchanged.
    [Fact]
    public void ConfigurationsDifferingOnlyInR_ShareTheSameDemand()
    {
        var config = PropertyTestChains.Configs().First(c => c.Name == "fixedL3-R2-z1").Config;
        var demand = PropertyTestChains.Demand(1);
        var r1 = SerialChain.Run(config with { R = 1 }, demand);
        var r4 = SerialChain.Run(config with { R = 4 }, demand);

        Assert.Equal(demand, r1.Incoming[0]);
        Assert.Equal(r1.Incoming[0], r4.Incoming[0]);
        Assert.NotEqual(r1.Orders[0], r4.Orders[0]);
    }

    // Invariant: over 20,000 periods the sample mean is within 1 % of mu and the sample standard deviation within 3 % of sigma.
    [Theory]
    [InlineData(100.0, 20.0)]
    [InlineData(50.0, 10.0)]
    public void LongRun_SampleMeanAndStandardDeviation_MatchMuAndSigma(double mu, double sigma)
    {
        var demand = GateA.Demand(0, 20000, mu, 0.0, sigma);
        double mean = Descriptive.Mean(demand, 0, demand.Length);
        double sd = Math.Sqrt(Descriptive.Variance(demand, 0, demand.Length));

        Assert.InRange(mean, 0.99 * mu, 1.01 * mu);
        Assert.InRange(sd, 0.97 * sigma, 1.03 * sigma);
    }
}
