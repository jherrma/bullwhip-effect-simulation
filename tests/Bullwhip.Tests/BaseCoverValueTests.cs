using Bullwhip.Core.Chain;
using Bullwhip.Core.Validation;
using Xunit;

namespace Bullwhip.Tests;

/// <summary>
/// Cover values of the options A (WorstPhase) and C (LotsInTransit) under the base lead time, P(L = 2, 3, 4, 5) =
/// 0.6897, 0.2140, 0.0728, 0.0234, and the lumpy cover at customer offset a = 1. The expectations are closed forms in
/// the pmf entries, derived from the exposure definition: own order times at multiples of R, the newest arrived lot is
/// the last order time τ ≤ t − L, and the exposure is X = t − τ.
/// </summary>
public class BaseCoverValueTests
{
    private static double[] BasePmf() => CoverRules.LeadPmf(new KaplanLeadTime(BaseLeadTime.LagPmf()), 0);

    [Fact]
    public void LotsInTransit_IsRTimesTheSumOfTailProbabilities()
    {
        // c = R·Σ_j P(L > jR). P(L > 0) = 1, P(L > 2) = 1 − P(L = 2), P(L > 4) = P(L = 5), P(L > 8) = 0.
        // R = 1: E[L] = 2.43. R = 2: 2·(1 + P(L > 2) + P(L > 4)) = 2.6675. R = 4: 4·(1 + P(L = 5)) = 4.0938. R = 8: 8.
        var pmf = BasePmf();
        double p2 = pmf[2], p5 = pmf[5];
        Assert.InRange(CoverRules.Cover(CoverRule.LotsInTransit, pmf, 1, null), 2.43 - 1e-9, 2.43 + 1e-9);
        AssertCover(2 * (1 + (1 - p2) + p5), 2.6675, CoverRules.Cover(CoverRule.LotsInTransit, pmf, 2, null));
        AssertCover(4 * (1 + p5), 4.0938, CoverRules.Cover(CoverRule.LotsInTransit, pmf, 4, null));
        AssertCover(8, 8, CoverRules.Cover(CoverRule.LotsInTransit, pmf, 8, null));

        // The customer phase is ignored: the same value at every lumpy offset.
        for (int a = 1; a <= 4; a++)
        {
            AssertCover(4 * (1 + p5), 4.0938, CoverRules.Cover(CoverRule.LotsInTransit, pmf, 4, a));
        }
    }

    [Fact]
    public void WorstPhase_Smooth_IsTheExposureOnePeriodAfterAnOrderTime()
    {
        // The worst phase is t = mR + 1. R = 2: L = 2, 3 give τ = 2m − 2 and X = 3; L = 4, 5 give τ = 2m − 4 and X = 5, so
        // c = 3 + 2·P(L ≥ 4) = 3.1925. R = 4: t − L in 4m − 4 .. 4m − 1 for every L in 2..5, so τ = 4m − 4 and X = 5 for all
        // L: c = 5. R = 8: likewise τ = 8m − 8 and X = 9 for all L: c = 9. R = 1: X = L, c = E[L].
        var pmf = BasePmf();
        double p4 = pmf[4], p5 = pmf[5];
        Assert.InRange(CoverRules.Cover(CoverRule.WorstPhase, pmf, 1, null), 2.43 - 1e-9, 2.43 + 1e-9);
        AssertCover(3 + 2 * (p4 + p5), 3.1925, CoverRules.Cover(CoverRule.WorstPhase, pmf, 2, null));
        AssertCover(5, 5, CoverRules.Cover(CoverRule.WorstPhase, pmf, 4, null));
        AssertCover(9, 9, CoverRules.Cover(CoverRule.WorstPhase, pmf, 8, null));
    }

    [Theory]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(5, 4)]
    [InlineData(4, 8)]
    public void WorstPhase_PointMass_IsLPlusRMinusOne(int leadTime, int r)
    {
        var point = CoverRules.LeadPmf(null, leadTime);
        Assert.Equal(leadTime + r - 1, CoverRules.Cover(CoverRule.WorstPhase, point, r, null));
    }

    [Fact]
    public void LumpyCover_AtOffsetOne_IsExactlyTwoLumpsAtRFourAndEight()
    {
        // a = 1: the customer's lumps land at t0 + 1, t0 + 1 + R, …; the integer window (t0, t0 + L + R − 1] holds two of
        // them for every L in 2..5 at R = 4 (L + 3 in 5..8) and R = 8 (L + 7 in 9..12), so c = 2R under A and B. R = 2:
        // two lumps for L = 2, 3 and three for L = 4, 5: c = 4 + 2·P(L ≥ 4) = 4.1925.
        var pmf = BasePmf();
        foreach (int r in new[] { 4, 8 })
        {
            foreach (var rule in new[] { CoverRule.CycleAverage, CoverRule.WorstPhase })
            {
                double cover = CoverRules.Cover(rule, pmf, r, 1);
                AssertCover(2 * r, 2 * r, cover);

                // Two whole lumps: weight 1 on the lumps at t0 + 1 and t0 + 1 + R, zero elsewhere in the span.
                var expected = new double[2 * r + 2];
                expected[0] = 1;
                expected[r] = 1;
                Assert.Equal(expected, CoverRules.Window(cover, r, 1, 2 * r + 2));
            }
        }

        double p4 = pmf[4], p5 = pmf[5];
        AssertCover(4 + 2 * (p4 + p5), 4.1925, CoverRules.Cover(CoverRule.CycleAverage, pmf, 2, 1));
        AssertCover(4 + 2 * (p4 + p5), 4.1925, CoverRules.Cover(CoverRule.WorstPhase, pmf, 2, 1));
    }

    [Fact]
    public void Chain_AtRFour_ReportsTheWorstPhaseAndLotsInTransitCovers()
    {
        // Base chain, equal phases (upstream a = 4). A: retailer 5 (above), upstream the lumpy worst phase, which equals the
        // integer lump cover 4·(⌊(L − 1)/4⌋ + 1) averaged over L: 4 + 4·P(L = 5) = 4.0938. C: 4·(1 + P(L = 5)) everywhere.
        double p5 = BasePmf()[5];
        var cfg = CountermeasureTestChains.BaseChain() with { LeadTimeSeed = GateA.LeadTimeSeed(0) };
        var demand = GateA.Demand(0, 2200, CountermeasureTestChains.Mu, 0, CountermeasureTestChains.Sigma);
        var worst = SerialChain.Run(cfg with { Cover = CoverRule.WorstPhase }, demand);
        var lots = SerialChain.Run(cfg with { Cover = CoverRule.LotsInTransit }, demand);
        var expectedWorst = new[] { 5, 4 + 4 * p5, 4 + 4 * p5, 4 + 4 * p5 };
        for (int k = 0; k < 4; k++)
        {
            Assert.InRange(worst.CoverPeriods[k], expectedWorst[k] - 1e-9, expectedWorst[k] + 1e-9);
            Assert.InRange(lots.CoverPeriods[k], 4 * (1 + p5) - 1e-9, 4 * (1 + p5) + 1e-9);
        }
    }

    // The closed form to 1e-9 and its four-digit value (computed separately) to 5e-5.
    private static void AssertCover(double closedForm, double fourDigits, double actual)
    {
        Assert.InRange(actual, closedForm - 1e-9, closedForm + 1e-9);
        Assert.InRange(actual, fourDigits - 5e-5, fourDigits + 5e-5);
    }
}
