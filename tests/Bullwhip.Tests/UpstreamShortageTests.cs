using Bullwhip.Core.Chain;
using Bullwhip.Core.Costs;
using Bullwhip.Core.Validation;
using Xunit;

namespace Bullwhip.Tests;

public class UpstreamShortageTests
{
    private static readonly CostParameters Base = new();

    private static ChainResult Run(double[]? shortage, double z = 0.0)
    {
        var kaplan = new KaplanLeadTime(new[] { 0.57, 0.43 });
        var cfg = new ChainConfig
        {
            LeadTimes = Enumerable.Repeat(kaplan.MeanLeadTime, 4).ToArray(),
            StochasticLeadTimes = Enumerable.Repeat(kaplan, 4).ToArray(),
            LeadTimeSeed = GateA.LeadTimeSeed(0), R = 2, Z = new[] { 1.0, z, z, z },
            Holding = BaseCostModel.StorageRatePerUnitWeek(Base), Value = BaseCostModel.StageValues(Base),
            CapitalRate = BaseCostModel.CapitalRatePerWeek(Base), ShortageCost = shortage,
            OrderCost = BaseCostModel.OrderCost(Base), AllowNegativeOrders = false, InitialMean = 100, Warmup = 50, Window = 8,
        };
        return SerialChain.Run(cfg, GateA.Demand(0, 450, 100, 0, 10));
    }

    [Fact]
    public void NullShortageCost_EqualsAZeroArray()
    {
        var withNull = Run(null);
        var withZeros = Run(new double[4]);

        Assert.All(withNull.ShortageCost, c => Assert.Equal(0.0, c));
        Assert.Equal(withZeros.ShortageCost, withNull.ShortageCost);
        Assert.Equal(withZeros.HoldingCost, withNull.HoldingCost);
        Assert.Equal(withZeros.CapitalCost, withNull.CapitalCost);
        Assert.Equal(withZeros.OrderCost, withNull.OrderCost);
        Assert.Equal(withZeros.TotalCostPerPeriod(), withNull.TotalCostPerPeriod());
    }

    [Fact]
    public void UpstreamShortage_AccruesRateTimesBacklogOverTheWindow()
    {
        double[] b = BaseCostModel.ShortageCostPerStage(Base, 0.5);
        var res = Run(b, z: -1.0);

        for (int stage = 0; stage < 4; stage++)
        {
            double backlog = res.BacklogPeriod[stage].Skip(res.Warmup).Sum();
            Assert.Equal(b[stage] * backlog, res.ShortageCost[stage], 6);
        }

        Assert.True(res.BacklogPeriod[1].Skip(res.Warmup).Sum() > 0, "the test needs an upstream stage with backlog");
        Assert.True(res.ShortageCost[1] > 0);
    }

    [Fact]
    public void ShortageCostPerStage_UpstreamIsBetaTimesTwentySixTimesHoldingRate()
    {
        var h = BaseCostModel.HoldingRatePerUnitWeek(Base);
        var b = BaseCostModel.ShortageCostPerStage(Base, 0.25);

        Assert.Equal(BaseCostModel.ShortageCost(Base), b[0], 12);
        Assert.Equal(26 * h[0], b[0], 12);
        for (int k = 1; k < 4; k++)
        {
            Assert.Equal(0.25 * 26 * h[k], b[k], 12);
        }

        Assert.All(BaseCostModel.ShortageCostPerStage(Base, 0).Skip(1), c => Assert.Equal(0.0, c));
    }
}
