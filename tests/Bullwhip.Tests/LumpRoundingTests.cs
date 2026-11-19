using Bullwhip.Core.Chain;
using Xunit;

namespace Bullwhip.Tests;

/// <summary>
/// Regression for a rounding failure at R = 8 under the base lead time (DECISIONS C82): the lumpy cover of exactly R lumps
/// came out as R·(1 − 1.1e-16) because the pmf sums to 1 − 1.1e-16, and the protection-window weights rejected it.
/// </summary>
public class LumpRoundingTests
{
    private const int R = 8;

    [Fact]
    public void BasePmf_CoverOfExactlyREightLumps_DoesNotThrowAtAnyOffset()
    {
        var pmf = CoverRules.LeadPmf(new KaplanLeadTime(BaseLeadTime.LagPmf()), 0);
        int exactlyR = 0;
        for (int a = 1; a <= R; a++)
        {
            double cover = CoverRules.Cover(CoverRule.CycleAverage, pmf, R, a);
            Assert.True(cover >= R - 1e-9, $"cover {cover:R} at offset {a} is below one lump");
            var weights = CoverRules.Window(cover, R, a, planningSpan: 16);
            if (Math.Abs(cover - R) < 1e-9)
            {
                exactlyR++;
                Assert.Equal(CoverRules.Window(R, R, a, planningSpan: 16), weights);
            }
        }

        Assert.True(exactlyR > 0, "the base pmf must give at least one offset whose cover is exactly R");
    }

    [Fact]
    public void FractionalLumpCount_IsNotSnapped()
    {
        // 8.5 periods = 1.0625 lumps: not within 1e-9 of an integer, so the weights are those of 1.0625 lumps.
        var weights = CoverRules.Window(8.5, R, customerOffset: 1, planningSpan: 16);
        double total = weights.Sum();
        Assert.Equal(1.0625, total, 9);
    }

    [Fact]
    public void Chain_AtREight_UnderTheBaseLeadTime_Runs()
    {
        var lag = new KaplanLeadTime(BaseLeadTime.LagPmf());
        var rng = new Random(1);
        var demand = Enumerable.Range(0, 600).Select(_ => 100 + 10 * (rng.NextDouble() - 0.5)).ToArray();
        var cfg = new ChainConfig
        {
            LeadTimes = Enumerable.Repeat(lag.MeanLeadTime, 4).ToArray(),
            StochasticLeadTimes = Enumerable.Repeat(lag, 4).ToArray(),
            LeadTimeSeed = 1,
            R = R,
            Z = new double[] { 1, 1, 1, 1 },
            AllowNegativeOrders = false,
            InitialMean = 100,
            Warmup = 100,
            Window = 8,
        };

        var result = SerialChain.Run(cfg, demand);
        Assert.NotNull(result);
    }
}
