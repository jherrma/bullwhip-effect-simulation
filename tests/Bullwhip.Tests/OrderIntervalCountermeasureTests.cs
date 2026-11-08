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

    private static double[] Covers(int r) => r switch
    {
        1 => new[] { 1.43, 1.43, 1.43, 1.43 },
        2 => new[] { 2.43, 2, 2, 2 },
        _ => new[] { 4.43, 4, 4, 4 },
    };

    private static double[] ExpectedBr4(int r) => r switch
    {
        1 => new[] { 1.4214, 2.1092, 3.2504, 5.1701 },
        2 => new[] { 1.7920, 3.1596, 5.8865, 11.4291 },
        _ => new[] { 2.7208, 8.0925, 26.5187, 91.8450 },
    };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void DecentralisedCovers_PerInterval(int r)
    {
        var cfg = CountermeasureTestChains.BaseChain(0.43, r) with { LeadTimeSeed = GateA.LeadTimeSeed(0) };
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

        var sim = CountermeasureTestChains.Br4PerStage(CountermeasureTestChains.BaseChain(0.43, r), CountermeasureTestChains.Reps);
        CountermeasureTestChains.AssertClosedForm($"RI R={r}", refs, sim, _out);
    }
}
