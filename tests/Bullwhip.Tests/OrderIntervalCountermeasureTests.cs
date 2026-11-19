using Bullwhip.Core.Chain;
using Bullwhip.Core.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Bullwhip.Tests;

/// <summary>Countermeasure 03 (smaller order intervals): the R dimension of the model against its closed forms (story RI-2).</summary>
public class OrderIntervalCountermeasureTests
{
    private readonly ITestOutputHelper _out;

    public OrderIntervalCountermeasureTests(ITestOutputHelper output)
    {
        _out = output;
    }

    /// <summary>
    /// Base-case covers (L in 2..5, E[L] = 2.43): retailer E[L] + R − 1; upstream E[L] at R = 1, 2 + 2·P(L ≥ 3) + 2·P(L = 5)
    /// at R = 2 and 4 + 4·P(L = 5) at R = 4 (<see cref="CountermeasureTestChains.Covers"/>).
    /// </summary>
    private static double[] Covers(int r) => CountermeasureTestChains.Covers(0, r);

    private static double[] ExpectedBr4(int r) => r switch
    {
        1 => new[] { 1.7920, 3.5250, 7.4411, 16.5040 },
        2 => new[] { 2.2252, 4.7485, 10.8798, 26.1454 },
        _ => new[] { 3.2789, 10.1149, 34.1792, 121.6558 },
    };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void DecentralisedCovers_PerInterval(int r)
    {
        var cfg = CountermeasureTestChains.BaseChain(0, r) with { LeadTimeSeed = GateA.LeadTimeSeed(0) };
        var demand = GateA.Demand(0, CountermeasureTestChains.Warmup + CountermeasureTestChains.Periods, CountermeasureTestChains.Mu, 0, CountermeasureTestChains.Sigma);
        var covers = SerialChain.Run(cfg, demand).CoverPeriods;
        var expected = Covers(r);
        for (int k = 0; k < 4; k++)
        {
            Assert.InRange(covers[k], expected[k] - 1e-12, expected[k] + 1e-12);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void DecentralisedBr4_ClosedForm(int r)
    {
        var refs = CountermeasureTestChains.DecentralisedBr4(Covers(r), 8);
        var expected = ExpectedBr4(r);
        for (int k = 0; k < 4; k++)
        {
            Assert.InRange(refs[k], expected[k] - 5e-5, expected[k] + 5e-5);
        }

        var sim = CountermeasureTestChains.Br4PerStage(CountermeasureTestChains.BaseChain(0, r), CountermeasureTestChains.Reps);
        CountermeasureTestChains.AssertClosedForm($"RI R={r}", refs, sim, _out);
    }
}
