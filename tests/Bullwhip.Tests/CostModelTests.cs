using Bullwhip.Core.Chain;
using Bullwhip.Core.Costs;
using Bullwhip.Core.Validation;
using Xunit;

namespace Bullwhip.Tests;

// Base cost model (DECISIONS F4, C28; research/k-bestellkosten.md, section 3a). Expected values are the hand-derived ones
// in that note, not outputs of the code under test.
public class CostModelTests
{
    private static readonly CostParameters Base = new();

    [Fact]
    public void StageValues_AreGeometricFrom30To10()
    {
        var values = BaseCostModel.StageValues(Base);
        Assert.Equal(30.0, values[0], 9);
        Assert.Equal(20.80, values[1], 2);
        Assert.Equal(14.42, values[2], 2);
        Assert.Equal(10.0, values[3], 9);
    }

    [Fact]
    public void OrderCost_BaseValuesAre46_2_32_0_22_2_15_4()
    {
        var k = BaseCostModel.OrderCost(Base);
        Assert.Equal(46.2, k[0], 1);
        Assert.Equal(32.0, k[1], 1);
        Assert.Equal(22.2, k[2], 1);
        Assert.Equal(15.4, k[3], 1);
    }

    [Fact]
    public void OrderCost_CalibratesOptimalIntervalToFour()
    {
        var k = BaseCostModel.OrderCost(Base);
        var h = BaseCostModel.HoldingRatePerUnitWeek(Base);
        for (int stage = 0; stage < Base.Stages; stage++)
        {
            Assert.Equal(4.0, BaseCostModel.OptimalInterval(k[stage], h[stage], Base.Mu), 9);
        }
    }

    [Fact]
    public void OrderCost_ScalesLinearlyWithTheSweepFactor()
    {
        var doubled = BaseCostModel.OrderCost(Base with { OrderCostScale = 2 });
        var baseline = BaseCostModel.OrderCost(Base);
        for (int stage = 0; stage < Base.Stages; stage++)
        {
            Assert.Equal(2 * baseline[stage], doubled[stage], 9);
        }
    }

    [Fact]
    public void HoldingRate_IsStoragePlusCapital()
    {
        var h = BaseCostModel.HoldingRatePerUnitWeek(Base);
        var storage = BaseCostModel.StorageRatePerUnitWeek(Base);
        var values = BaseCostModel.StageValues(Base);
        double capitalWeekly = BaseCostModel.CapitalRatePerWeek(Base);
        for (int stage = 0; stage < Base.Stages; stage++)
        {
            Assert.Equal(h[stage], storage[stage] + capitalWeekly * values[stage], 12);
        }

        Assert.Equal(0.057692, h[0], 6);
    }

    [Fact]
    public void ShortageCost_IsNineTimesRetailerHoldingAndGivesCriticalRatioNinetyPercent()
    {
        double b = BaseCostModel.ShortageCost(Base);
        double h1 = BaseCostModel.HoldingRatePerUnitWeek(Base)[0];
        Assert.Equal(0.5192, b, 4);
        Assert.Equal(9 * h1, b, 12);
        Assert.Equal(0.9, b / (b + h1), 9);
    }

    [Fact]
    public void ChainCosts_MatchComponentRatesAndOrderCountTimesK()
    {
        var kaplan = new KaplanLeadTime(new[] { 0.57, 0.43 });
        var cfg = new ChainConfig
        {
            LeadTimes = Enumerable.Repeat(kaplan.MeanLeadTime, 4).ToArray(),
            StochasticLeadTimes = Enumerable.Repeat(kaplan, 4).ToArray(),
            LeadTimeSeed = GateA.LeadTimeSeed(0), R = 4, Z = new[] { 1.0, 1.0, 1.0, 1.0 },
            Holding = BaseCostModel.StorageRatePerUnitWeek(Base), Value = BaseCostModel.StageValues(Base),
            CapitalRate = BaseCostModel.CapitalRatePerWeek(Base), ShortageCost = BaseCostModel.ShortageCostPerStage(Base, 0),
            OrderCost = BaseCostModel.OrderCost(Base), AllowNegativeOrders = false, InitialMean = 100, Warmup = 200, Window = 8,
        };
        var res = SerialChain.Run(cfg, GateA.Demand(0, 2200, 100, 0, 10));
        var k = BaseCostModel.OrderCost(Base);

        Assert.Equal(2000, res.MeasuredPeriods);
        double total = res.ShortageCost[0];
        for (int stage = 0; stage < 4; stage++)
        {
            int orders = 0;
            for (int t = cfg.Warmup; t < res.Orders[stage].Length; t++)
            {
                if (res.Orders[stage][t] > 0) orders++;
            }

            Assert.Equal(k[stage] * orders, res.OrderCost[stage], 6);
            total += res.HoldingCost[stage] + res.CapitalCost[stage] + res.OrderCost[stage];
        }

        Assert.Equal(total / 2000, res.TotalCostPerPeriod(), 6);
    }

    [Fact]
    public void ChainCosts_NullOrderCostChargesNothing()
    {
        var kaplan = new KaplanLeadTime(new[] { 0.57, 0.43 });
        var cfg = new ChainConfig
        {
            LeadTimes = Enumerable.Repeat(kaplan.MeanLeadTime, 4).ToArray(),
            StochasticLeadTimes = Enumerable.Repeat(kaplan, 4).ToArray(),
            LeadTimeSeed = GateA.LeadTimeSeed(0), R = 1, Z = new[] { 1.0, 1.0, 1.0, 1.0 },
            AllowNegativeOrders = false, InitialMean = 100, Warmup = 200, Window = 8,
        };
        var res = SerialChain.Run(cfg, GateA.Demand(0, 2200, 100, 0, 10));
        Assert.All(res.OrderCost, c => Assert.Equal(0.0, c));
    }
}
