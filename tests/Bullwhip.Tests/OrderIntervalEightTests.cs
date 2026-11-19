using Bullwhip.Core.Chain;
using Bullwhip.Core.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Bullwhip.Tests;

/// <summary>
/// Order interval R = 8 under the base lead time (L in 2..5, E[L] = 2.43), the largest interval of the K sweep
/// (experiments/KSweep, Intervals {1, 2, 4, 8}; equal order phases, p = 8). The bookkeeping invariants at R = 8 run in
/// <see cref="ChainInvariantTests"/> and <see cref="ChainCostPropertyTests"/> (configuration "kaplan-R8-z1").
/// </summary>
public class OrderIntervalEightTests
{
    private const int R = 8;
    private readonly ITestOutputHelper _out;

    public OrderIntervalEightTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static ChainConfig SweepLikeChain() => CountermeasureTestChains.BaseChain(0, R) with
    {
        Z = new double[] { 1, 1, 1, 1 },
        AllowNegativeOrders = false,
    };

    [Fact]
    public void Covers_AreMeanLeadTimePlusSeven_AndOneLumpUpstream()
    {
        // Cycle-average rule. Retailer: E[L] + R − 1 = 2.43 + 7 = 9.43. Upstream with equal phases the customer's lumps land
        // at t0 + 8, t0 + 16, …, and the integer window (t0, t0 + L + 7] holds exactly one of them for every L in 2..5
        // (L + 7 ≤ 12 < 16), so c = 8 at every stage k > 0.
        var res = SerialChain.Run(
            SweepLikeChain() with { LeadTimeSeed = GateA.LeadTimeSeed(0) },
            GateA.Demand(0, 2200, CountermeasureTestChains.Mu, 0, CountermeasureTestChains.Sigma));
        var expected = new[] { 9.43, 8.0, 8.0, 8.0 };
        for (int k = 0; k < 4; k++)
        {
            Assert.InRange(res.CoverPeriods[k], expected[k] - 1e-9, expected[k] + 1e-9);
        }
    }

    [Fact]
    public void ClippedRun_OrdersOnlyEveryEighthPeriod_AndCountsThem()
    {
        // Orders are placed only at t ≡ 0 (mod 8); OrderCount counts the positive ones, at most 2200 / 8 = 275 per stage.
        var res = SerialChain.Run(
            SweepLikeChain() with { LeadTimeSeed = GateA.LeadTimeSeed(1) },
            GateA.Demand(1, 2200, CountermeasureTestChains.Mu, 0, CountermeasureTestChains.Sigma));
        for (int k = 0; k < 4; k++)
        {
            for (int t = 0; t < 2200; t++)
            {
                Assert.True(t % R == 0 || res.Orders[k][t] == 0.0, $"stage {k} t {t}");
            }

            Assert.InRange(res.OrderCount[k], 1, 275);
            Assert.Equal(res.Orders[k].Count(q => q > 0), res.OrderCount[k]);
        }
    }

    [Fact]
    public void ClosedFormBr8_UnclippedAtZeroSafety()
    {
        // At R = p = 8 the forecast window is one 8-period bucket B_t, so S_t = c·B_t/8 and q_t = B_t + (c/8)·(B_t − B_{t−1})
        // (unclipped, z = 0; arrivals do not enter q). Upstream the bucket is the customer's single order, so the 8-period
        // sums of stage k's orders are the product over i ≤ k of ((1 + c_i/8) − (c_i/8)·x) applied to independent demand
        // sums: the DecentralisedBr4 polynomial with covers {9.43, 8, 8, 8}, measured on 8-period sums (one order per sum).
        var refs = CountermeasureTestChains.DecentralisedBr4(new[] { 9.43, 8, 8, 8 }, 8);
        var anchors = new[] { 6.1364, 40.9548, 305.2294, 2397.1529 };
        for (int k = 0; k < 4; k++)
        {
            Assert.InRange(refs[k], anchors[k] - 5e-4, anchors[k] + 5e-4);
        }

        var sim = CountermeasureTestChains.BlockRatioPerStage(CountermeasureTestChains.BaseChain(0, R), CountermeasureTestChains.Reps, R);
        CountermeasureTestChains.AssertClosedForm("R=8 BR8", refs, sim, _out);
    }
}
