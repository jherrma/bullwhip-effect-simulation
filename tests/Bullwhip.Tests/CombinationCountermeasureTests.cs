using Bullwhip.Core.Chain;
using Bullwhip.Core.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Bullwhip.Tests;

/// <summary>Combination of information sharing (IS-F) and the dose w = 1 (lead time 2 at every link) against the closed forms (story COMB-2).</summary>
public class CombinationCountermeasureTests
{
    private static readonly double[] BaseZ = { 2.5, 1.25, 1.25, 1.25 };
    private readonly ITestOutputHelper _out;

    public CombinationCountermeasureTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static ChainConfig IsAtW1(bool clipped = false)
    {
        var cfg = CountermeasureTestChains.BaseChain(1) with { InfoSharing = true };
        return clipped ? cfg with { Z = BaseZ, AllowNegativeOrders = false } : cfg;
    }

    private static ChainResult RunRep(ChainConfig cfg, int rep)
    {
        var demand = GateA.Demand(rep, 2200, CountermeasureTestChains.Mu, 0, CountermeasureTestChains.Sigma);
        return SerialChain.Run(cfg with { LeadTimeSeed = GateA.LeadTimeSeed(rep) }, demand);
    }

    [Fact]
    public void InfoSharingAtLeadTimeTwo_ClosedFormBr4()
    {
        var sim = CountermeasureTestChains.Br4PerStage(IsAtW1(), CountermeasureTestChains.Reps);
        CountermeasureTestChains.AssertClosedForm("IS-F w=1", CountermeasureTestChains.InfoSharingBr4(new double[] { 5, 4, 4, 4 }, 8), sim, _out);
    }

    [Fact]
    public void InfoSharingAtLeadTimeTwo_CoversAreFiveAndFour()
    {
        // L = 2, R = 4: retailer L + R − 1 = 5; upstream one lump of 4 periods.
        Assert.Equal(new double[] { 5, 4, 4, 4 }, RunRep(IsAtW1(), 0).CoverPeriods);
    }

    [Fact]
    public void InfoSharingAtLeadTimeTwo_ErrorRmsIsEqualAcrossStagesAndPosLevel()
    {
        const int reps = 50;
        double sum = 0;
        for (int rep = 0; rep < reps; rep++)
        {
            var rms = RunRep(IsAtW1(true), rep).ProtectionErrorRms;
            for (int k = 1; k < 4; k++)
            {
                Assert.Equal(rms[0], rms[k]);
            }

            sum += rms[0] * rms[0];
        }

        double pooled = Math.Sqrt(sum / reps);
        // POS error over Lp = L + R − 1 = 5 periods: σ·√(Lp + Lp²/p) = 10·√(5 + 25/8) = 28.504.
        _out.WriteLine($"pooled error rms = {pooled:0.000} (expected 28.504)");
        Assert.InRange(pooled, 28.504 * 0.98, 28.504 * 1.02);
    }

    [Fact]
    public void InfoSharingAtLeadTimeTwo_EqualsDeterministicLeadTimeTwo()
    {
        var kaplan = IsAtW1(true);
        var deterministic = kaplan with { LeadTimes = new double[] { 2, 2, 2, 2 }, StochasticLeadTimes = null };
        for (int rep = 0; rep < 5; rep++)
        {
            var a = RunRep(kaplan, rep);
            var b = RunRep(deterministic, rep);
            Assert.Equal(a.Orders, b.Orders);
            Assert.Equal(a.OnHandPeriod, b.OnHandPeriod);
            Assert.Equal(a.BacklogPeriod, b.BacklogPeriod);
            Assert.Equal(a.Shipped, b.Shipped);
            Assert.Equal(a.ProtectionErrorRms, b.ProtectionErrorRms);
            Assert.Equal(a.MeanSigmaHat, b.MeanSigmaHat);
        }
    }
}
