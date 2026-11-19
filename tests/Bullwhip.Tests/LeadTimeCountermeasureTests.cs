using Bullwhip.Core.Chain;
using Bullwhip.Core.Rng;
using Bullwhip.Core.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Bullwhip.Tests;

/// <summary>
/// Countermeasure 02 (shorter lead time): the lag pmf per stage as the only lever (stories LT-1, LT-2). The dose w raises
/// the transit hazards (<see cref="BaseLeadTime"/>): w = 0 is the base case, w = 1 the deterministic lead time L = 2.
/// </summary>
public class LeadTimeCountermeasureTests
{
    private static readonly double[] BaseZ = { 2.5, 1.25, 1.25, 1.25 };
    private readonly ITestOutputHelper _out;

    public LeadTimeCountermeasureTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static ChainConfig PointMassKaplan() => CountermeasureTestChains.BaseChain(1) with { Z = BaseZ, AllowNegativeOrders = false };

    private static ChainConfig Deterministic() => new()
    {
        LeadTimes = new double[] { 2, 2, 2, 2 },
        R = 4,
        Window = 8,
        Z = BaseZ,
        AllowNegativeOrders = false,
        InitialMean = 100,
        Warmup = CountermeasureTestChains.Warmup,
    };

    private static void AssertSameRuns(ChainConfig a, ChainConfig b)
    {
        for (int rep = 0; rep < 5; rep++)
        {
            var demand = GateA.Demand(rep, 2200, CountermeasureTestChains.Mu, 0, CountermeasureTestChains.Sigma);
            var seed = GateA.LeadTimeSeed(rep);
            var x = SerialChain.Run(a with { LeadTimeSeed = seed }, demand);
            var y = SerialChain.Run(b with { LeadTimeSeed = seed }, demand);
            Assert.Equal(y.Orders, x.Orders);
            Assert.Equal(y.OnHandPeriod, x.OnHandPeriod);
            Assert.Equal(y.BacklogPeriod, x.BacklogPeriod);
            Assert.Equal(y.Shipped, x.Shipped);
            Assert.Equal(y.ProtectionErrorRms, x.ProtectionErrorRms);
            Assert.Equal(y.MeanSigmaHat, x.MeanSigmaHat);
            Assert.Equal(y.MeanPipeline, x.MeanPipeline);
        }
    }

    [Fact]
    public void ClosedFormHelper_ReproducesTheIndexTable()
    {
        var c = new[] { 4.43, 4, 4, 4 };
        var dec = CountermeasureTestChains.DecentralisedBr4(c, 8);
        var inf = CountermeasureTestChains.InfoSharingBr4(c, 8);
        var expDec = new[] { 2.7208, 8.0925, 26.5187, 91.8450 };
        var expInf = new[] { 2.7208, 5.3283, 8.9358, 13.5433 };
        for (int k = 0; k < 4; k++)
        {
            Assert.InRange(dec[k], expDec[k] - 5e-5, expDec[k] + 5e-5);
            Assert.InRange(inf[k], expInf[k] - 5e-5, expInf[k] + 5e-5);
        }
    }

    [Fact]
    public void PointMassLag_IsBitIdenticalToDeterministicLeadTimeTwo() => AssertSameRuns(PointMassKaplan(), Deterministic());

    [Fact]
    public void ExplicitCycleAverage_IsBitIdenticalAtLeadTimeTwo() =>
        AssertSameRuns(PointMassKaplan() with { Cover = CoverRule.CycleAverage }, PointMassKaplan());

    [Fact]
    public void KaplanArrivals_AreMonotoneInW()
    {
        // A larger dose raises every hazard, so on the same uniforms no dispatch arrives later. The doses are listed from
        // the largest to the base, so the lead times rise along the list. All four share the maximum lag 4.
        var ws = new[] { 0.30, 0.20, 0.10, 0.0 };
        int[] Draw(double w) => new KaplanLeadTime(CountermeasureTestChains.LagPmf(w))
            .DrawArrivals(new SplitMix64(GateA.LeadTimeSeed(7)), 2199);
        for (int i = 0; i < ws.Length; i++)
        {
            for (int j = i + 1; j < ws.Length; j++)
            {
                var low = Draw(ws[i]);
                var high = Draw(ws[j]);
                Assert.Equal(low.Length, high.Length);
                for (int s = 0; s < low.Length; s++)
                {
                    Assert.True(low[s] <= high[s], $"w {ws[i]} vs {ws[j]} at {s}");
                }
            }
        }

        // w = 1: maximum lag 1, element s + 2 is the dispatch of period s and arrives at s + 2 (L = 2).
        var point = Draw(1);
        for (int s = 0; s < 2199; s++)
        {
            Assert.Equal(s + 2, point[s + 2]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.10)]
    [InlineData(0.20)]
    [InlineData(0.30)]
    [InlineData(1)]
    public void Covers_AreMeanLeadTimePlusThree_AndTheLumpCoverUpstream(double w)
    {
        var cfg = CountermeasureTestChains.BaseChain(w) with { Z = BaseZ, AllowNegativeOrders = false, LeadTimeSeed = GateA.LeadTimeSeed(0) };
        var res = SerialChain.Run(cfg, GateA.Demand(0, 2200, CountermeasureTestChains.Mu, 0, CountermeasureTestChains.Sigma));
        var expected = CountermeasureTestChains.Covers(w, 4);
        for (int k = 0; k < 4; k++)
        {
            Assert.InRange(res.CoverPeriods[k], expected[k] - 1e-12, expected[k] + 1e-12);
        }
    }

    [Fact]
    public void MeanPipeline_IsMuTimesMeanTransit_AtLeadTimeTwoAndAtTheBase()
    {
        // Little's law: mean pipeline μ·(E[L] − 1), i.e. 100 at w = 1 (one week of transit) and 143 at the base.
        Assert.All(MeanPipeline(1), v => Assert.InRange(v, 100 * 0.99, 100 * 1.01));
        Assert.All(MeanPipeline(0), v => Assert.InRange(v, 143 * 0.99, 143 * 1.01));
    }

    private static double[] MeanPipeline(double w)
    {
        var cfg = CountermeasureTestChains.BaseChain(w) with { Z = BaseZ, AllowNegativeOrders = false };
        var sums = new double[4];
        for (int rep = 0; rep < 100; rep++)
        {
            var res = SerialChain.Run(cfg with { LeadTimeSeed = GateA.LeadTimeSeed(rep) }, GateA.Demand(rep, 2200, CountermeasureTestChains.Mu, 0, CountermeasureTestChains.Sigma));
            for (int k = 0; k < 4; k++)
            {
                sums[k] += res.MeanPipeline[k] / 100;
            }
        }

        return sums;
    }

    [Fact]
    public void ClosedFormBr4_LeadTimeTwo() => ClosedForm(1);

    [Fact]
    public void ClosedFormBr4_WPointTwo() => ClosedForm(0.2);

    private void ClosedForm(double w)
    {
        var refs = CountermeasureTestChains.DecentralisedBr4(CountermeasureTestChains.Covers(w, 4), 8);
        var sim = CountermeasureTestChains.Br4PerStage(CountermeasureTestChains.BaseChain(w), CountermeasureTestChains.Reps);
        CountermeasureTestChains.AssertClosedForm($"LT w={w}", refs, sim, _out);
    }
}
