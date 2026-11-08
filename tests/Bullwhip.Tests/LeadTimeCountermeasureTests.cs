using Bullwhip.Core.Chain;
using Bullwhip.Core.Rng;
using Bullwhip.Core.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Bullwhip.Tests;

/// <summary>Countermeasure 02 (shorter lead time): the lag pmf per stage as the only lever (stories LT-1, LT-2).</summary>
public class LeadTimeCountermeasureTests
{
    private static readonly double[] BaseZ = { 2.5, 1.25, 1.25, 1.25 };
    private readonly ITestOutputHelper _out;

    public LeadTimeCountermeasureTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static ChainConfig PointMassKaplan() => CountermeasureTestChains.BaseChain(0) with { Z = BaseZ, AllowNegativeOrders = false };

    private static ChainConfig Deterministic() => new()
    {
        LeadTimes = new double[] { 1, 1, 1, 1 },
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
    public void PointMassLag_IsBitIdenticalToDeterministicLeadTimeOne() => AssertSameRuns(PointMassKaplan(), Deterministic());

    [Fact]
    public void ExplicitCycleAverage_IsBitIdenticalAtLeadTimeOne() =>
        AssertSameRuns(PointMassKaplan() with { Cover = CoverRule.CycleAverage }, PointMassKaplan());

    [Fact]
    public void KaplanArrivals_AreMonotoneInQ()
    {
        var qs = new[] { 0.10, 0.20, 0.30, 0.43 };
        int[] Draw(double q) => new KaplanLeadTime(CountermeasureTestChains.LagPmf(q))
            .DrawArrivals(new SplitMix64(GateA.LeadTimeSeed(7)), 2199);
        for (int i = 0; i < qs.Length; i++)
        {
            for (int j = i + 1; j < qs.Length; j++)
            {
                var low = Draw(qs[i]);
                var high = Draw(qs[j]);
                Assert.Equal(low.Length, high.Length);
                for (int s = 0; s < low.Length; s++)
                {
                    Assert.True(low[s] <= high[s], $"q {qs[i]} vs {qs[j]} at {s}");
                }
            }
        }

        var point = Draw(0);
        for (int s = 0; s < 2199; s++)
        {
            Assert.Equal(s + 1, point[s + 1]);
        }
    }

    [Theory]
    [InlineData(0.43)]
    [InlineData(0.30)]
    [InlineData(0.20)]
    [InlineData(0.10)]
    [InlineData(0)]
    public void Covers_AreFourPlusQ_AndFourUpstream(double q)
    {
        var cfg = CountermeasureTestChains.BaseChain(q) with { Z = BaseZ, AllowNegativeOrders = false, LeadTimeSeed = GateA.LeadTimeSeed(0) };
        var res = SerialChain.Run(cfg, GateA.Demand(0, 2200, CountermeasureTestChains.Mu, 0, CountermeasureTestChains.Sigma));
        var expected = new[] { 4 + q, 4, 4, 4 };
        for (int k = 0; k < 4; k++)
        {
            Assert.InRange(res.CoverPeriods[k], expected[k] - 1e-12, expected[k] + 1e-12);
        }
    }

    [Fact]
    public void MeanPipeline_IsZeroAtLeadTimeOne_AndMuTimesQAtTheBase()
    {
        var zero = CountermeasureTestChains.BaseChain(0) with { Z = BaseZ, AllowNegativeOrders = false };
        for (int rep = 0; rep < 5; rep++)
        {
            var res = SerialChain.Run(zero with { LeadTimeSeed = GateA.LeadTimeSeed(rep) }, GateA.Demand(rep, 2200, CountermeasureTestChains.Mu, 0, CountermeasureTestChains.Sigma));
            Assert.All(res.MeanPipeline, v => Assert.InRange(Math.Abs(v), 0, 1e-9));
        }

        var baseCfg = CountermeasureTestChains.BaseChain(0.43) with { Z = BaseZ, AllowNegativeOrders = false };
        var sums = new double[4];
        for (int rep = 0; rep < 100; rep++)
        {
            var res = SerialChain.Run(baseCfg with { LeadTimeSeed = GateA.LeadTimeSeed(rep) }, GateA.Demand(rep, 2200, CountermeasureTestChains.Mu, 0, CountermeasureTestChains.Sigma));
            for (int k = 0; k < 4; k++)
            {
                sums[k] += res.MeanPipeline[k] / 100;
            }
        }

        Assert.All(sums, v => Assert.InRange(v, 43 * 0.97, 43 * 1.03));
    }

    [Fact]
    public void ClosedFormBr4_LeadTimeOne() => ClosedForm(0);

    [Fact]
    public void ClosedFormBr4_QPointTwo() => ClosedForm(0.2);

    private void ClosedForm(double q)
    {
        var refs = CountermeasureTestChains.DecentralisedBr4(new[] { 4 + q, 4, 4, 4 }, 8);
        var sim = CountermeasureTestChains.Br4PerStage(CountermeasureTestChains.BaseChain(q), CountermeasureTestChains.Reps);
        CountermeasureTestChains.AssertClosedForm($"LT q={q}", refs, sim, _out);
    }
}
