using Bullwhip.Core.Chain;
using Bullwhip.Core.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Bullwhip.Tests;

/// <summary>Combination of information sharing (IS-F) and q = 0 (lead time 1 at every link) against the closed forms (story COMB-2).</summary>
public class CombinationCountermeasureTests
{
    private static readonly double[] BaseZ = { 2.5, 1.25, 1.25, 1.25 };
    private readonly ITestOutputHelper _out;

    public CombinationCountermeasureTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static ChainConfig IsAtQ0(bool clipped = false)
    {
        var cfg = CountermeasureTestChains.BaseChain(0) with { InfoSharing = true };
        return clipped ? cfg with { Z = BaseZ, AllowNegativeOrders = false } : cfg;
    }

    private static ChainResult RunRep(ChainConfig cfg, int rep)
    {
        var demand = GateA.Demand(rep, 2200, CountermeasureTestChains.Mu, 0, CountermeasureTestChains.Sigma);
        return SerialChain.Run(cfg with { LeadTimeSeed = GateA.LeadTimeSeed(rep) }, demand);
    }

    [Fact]
    public void InfoSharingAtLeadTimeOne_ClosedFormBr4()
    {
        var sim = CountermeasureTestChains.Br4PerStage(IsAtQ0(), CountermeasureTestChains.Reps);
        CountermeasureTestChains.AssertClosedForm("IS-F q=0", CountermeasureTestChains.InfoSharingBr4(new double[] { 4, 4, 4, 4 }, 8), sim, _out);
    }

    [Fact]
    public void InfoSharingAtLeadTimeOne_CoversAreFour()
    {
        Assert.Equal(new double[] { 4, 4, 4, 4 }, RunRep(IsAtQ0(), 0).CoverPeriods);
    }

    [Fact]
    public void InfoSharingAtLeadTimeOne_ErrorRmsIsEqualAcrossStagesAndSigmaRootSix()
    {
        const int reps = 50;
        double sum = 0;
        for (int rep = 0; rep < reps; rep++)
        {
            var rms = RunRep(IsAtQ0(true), rep).ProtectionErrorRms;
            for (int k = 1; k < 4; k++)
            {
                Assert.Equal(rms[0], rms[k]);
            }

            sum += rms[0] * rms[0];
        }

        double pooled = Math.Sqrt(sum / reps);
        _out.WriteLine($"pooled error rms = {pooled:0.000} (expected 24.495)");
        Assert.InRange(pooled, 24.495 * 0.98, 24.495 * 1.02);
    }

    [Fact]
    public void InfoSharingAtLeadTimeOne_EqualsDeterministicLeadTimeOne()
    {
        var kaplan = IsAtQ0(true);
        var deterministic = kaplan with { LeadTimes = new double[] { 1, 1, 1, 1 }, StochasticLeadTimes = null };
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
