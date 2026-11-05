using Bullwhip.Core.Chain;
using Xunit;

namespace Bullwhip.Tests;

// Property tests of the cost accounting (holding, capital, shortage, order cost) over several configurations and 10 seeds.
public class ChainCostPropertyTests
{
    private static IEnumerable<(string Label, ChainConfig Config, int Rep)> Cases()
    {
        foreach (var (name, config) in PropertyTestChains.Configs())
        {
            for (int rep = 0; rep < PropertyTestChains.Seeds; rep++)
            {
                yield return ($"{name} rep {rep}", config with { ShortageEscalation = 0.5 }, rep);
            }
        }
    }

    // Invariant: all four cost components are >= 0 at every stage.
    [Fact]
    public void AllCostComponents_AreNonNegative()
    {
        foreach (var (label, config, rep) in Cases())
        {
            var res = PropertyTestChains.Run(config, rep);
            for (int k = 0; k < 4; k++)
            {
                Assert.True(res.HoldingCost[k] >= 0 && res.CapitalCost[k] >= 0 && res.ShortageCost[k] >= 0 && res.OrderCost[k] >= 0,
                    $"{label} stage {k}");
            }
        }
    }

    // Invariant: TotalCostPerPeriod = (sum of all components over all stages) / MeasuredPeriods, MeasuredPeriods = length - warm-up.
    [Fact]
    public void TotalCostPerPeriod_IsTheComponentSumOverTheMeasuredPeriods()
    {
        foreach (var (label, config, rep) in Cases())
        {
            var res = PropertyTestChains.Run(config, rep);
            double sum = 0;
            for (int k = 0; k < 4; k++)
            {
                sum += res.HoldingCost[k] + res.CapitalCost[k] + res.ShortageCost[k] + res.OrderCost[k];
            }

            Assert.Equal(PropertyTestChains.Length - PropertyTestChains.Warmup, res.MeasuredPeriods);
            Assert.Equal(sum / res.MeasuredPeriods, res.TotalCostPerPeriod(), 9);
        }
    }

    // Invariant: shortage cost is exactly 0 when ShortageCost is null, whatever the escalation.
    [Fact]
    public void ShortageCost_IsExactlyZeroWithoutAShortageRate()
    {
        foreach (var (label, config, rep) in Cases())
        {
            var res = PropertyTestChains.Run(config with { ShortageCost = null }, rep);
            Assert.True(res.ShortageCost.All(c => c == 0.0), label);
        }
    }

    // Invariant: holding cost = h_k * sum of on-hand over the window; order cost = K_k * number of positive orders in the window.
    [Fact]
    public void HoldingAndOrderCost_MatchTheirPeriodRecords()
    {
        foreach (var (label, config, rep) in Cases())
        {
            var res = PropertyTestChains.Run(config, rep);
            for (int k = 0; k < 4; k++)
            {
                double stock = res.OnHandPeriod[k].Skip(PropertyTestChains.Warmup).Sum(v => Math.Max(0.0, v));
                int orders = res.Orders[k].Skip(PropertyTestChains.Warmup).Count(q => q > 0.0);
                Assert.True(Math.Abs(PropertyTestChains.Holding[k] * stock - res.HoldingCost[k]) <= 1e-7 * Math.Max(1, res.HoldingCost[k]), $"{label} holding {k}");
                Assert.Equal(PropertyTestChains.OrderCost[k] * orders, res.OrderCost[k], 9);
            }
        }
    }

    // Invariant: costs are only accounting, they never feed back into the dynamics. Orders and stocks are identical
    // whatever the cost parameters (and the shortage cost is linear in ShortageCost for kappa = 0).
    [Fact]
    public void CostParameters_DoNotChangeTheDynamics_AndShortageCostIsLinearInTheRate()
    {
        foreach (var (label, config, rep) in Cases())
        {
            var plain = PropertyTestChains.Run(config with { ShortageEscalation = 0 }, rep);
            var scaled = PropertyTestChains.Run(config with
            {
                ShortageEscalation = 0, ShortageCost = PropertyTestChains.ShortageBase.Select(b => 3 * b).ToArray(),
                Holding = null, Value = null, OrderCost = null,
            }, rep);
            for (int k = 0; k < 4; k++)
            {
                Assert.Equal(plain.Orders[k], scaled.Orders[k]);
                Assert.Equal(plain.BacklogPeriod[k], scaled.BacklogPeriod[k]);
                Assert.Equal(3 * plain.ShortageCost[k], scaled.ShortageCost[k], 7);
                Assert.True(scaled.HoldingCost[k] == 0 && scaled.CapitalCost[k] == 0 && scaled.OrderCost[k] == 0, $"{label} stage {k}");
            }
        }
    }

    // Invariant: for fixed demand and configuration, raising the escalation kappa never lowers any stage's shortage cost.
    [Fact]
    public void ShortageCost_IsMonotoneInTheEscalationKappa()
    {
        double[] kappas = { 0.0, 0.25, 1.0, 3.0 };
        foreach (var (label, config, rep) in Cases())
        {
            var costs = kappas.Select(kappa => PropertyTestChains.Run(config with { ShortageEscalation = kappa }, rep).ShortageCost).ToArray();
            for (int i = 1; i < kappas.Length; i++)
            {
                for (int k = 0; k < 4; k++)
                {
                    Assert.True(costs[i][k] >= costs[i - 1][k], $"{label} stage {k}: kappa {kappas[i]}");
                }
            }
        }
    }

    // Invariant: for fixed demand, raising a stage's ShortageCost never lowers that stage's shortage cost (and leaves the others).
    [Fact]
    public void ShortageCost_IsMonotoneInTheStageRate()
    {
        foreach (var (label, config, rep) in Cases())
        {
            var low = PropertyTestChains.Run(config, rep).ShortageCost;
            for (int raised = 0; raised < 4; raised++)
            {
                double[] rates = (double[])PropertyTestChains.ShortageBase.Clone();
                rates[raised] *= 2.5;
                var high = PropertyTestChains.Run(config with { ShortageCost = rates }, rep).ShortageCost;
                Assert.True(high[raised] >= low[raised], $"{label} stage {raised}");
                for (int k = 0; k < 4; k++)
                {
                    Assert.True(k == raised ? high[k] >= low[k] : Math.Abs(high[k] - low[k]) <= 1e-9 * Math.Max(1, low[k]), $"{label} stage {k}");
                }
            }
        }
    }
}
