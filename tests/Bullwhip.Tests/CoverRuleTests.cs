using Bullwhip.Core.Chain;
using Bullwhip.Core.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Bullwhip.Tests;

/// <summary>Cover rules A, B, C at R = 4 (ChainConfig.Cover, CoverRules): reduction to the integer rule and Kaplan identities.</summary>
public class CoverRuleTests
{
    private const int R = 4;
    private readonly ITestOutputHelper _out;

    public CoverRuleTests(ITestOutputHelper output)
    {
        _out = output;
    }

    [Fact]
    public void PointMassL_WorstPhaseAndCycleAverageReproduceIntegerRule_LotsInTransitDoesNot()
    {
        // Fixed integer L (deterministic arrivals = point-mass pmf): X_u runs through L .. L+R−1, so (A) max_u and (B) E_L give
        // L + R − 1 at the retailer and n·R upstream, the default path, and the whole run must be bit-identical (z = 1, iid).
        // (C) gives R·⌈L/R⌉ at every stage: 4, 4, 8, 8 for L = 2, 3, 6, 7 against 5, 6, 9, 10 at the retailer.
        var d = GateA.Demand(0, 2200, 100, 0, 10);
        foreach (int l in new[] { 2, 3, 6, 7 })
        {
            foreach (var ph in new[] { new[] { 0, 0, 0, 0 }, new[] { 0, 1, 2, 3 }, new[] { 0, 3, 2, 1 }, new[] { 0, 2, 0, 2 } })
            {
                var cfg = new ChainConfig
                {
                    LeadTimes = new double[] { l, l, l, l }, Window = 8, R = R, Phases = ph, Z = new[] { 1.0, 1, 1, 1 }, InitialMean = 100,
                    Warmup = 200,
                };
                var baseline = SerialChain.Run(cfg, d);
                foreach (var rule in new[] { CoverRule.WorstPhase, CoverRule.CycleAverage })
                {
                    var res = SerialChain.Run(cfg with { Cover = rule }, d);
                    Assert.Equal(baseline.CoverPeriods, res.CoverPeriods);
                    for (int k = 0; k < 4; k++)
                    {
                        Assert.True(baseline.Orders[k].SequenceEqual(res.Orders[k]), $"L={l} {rule} stage {k + 1} orders");
                        Assert.True(baseline.OnHandPeriod[k].SequenceEqual(res.OnHandPeriod[k]), $"L={l} {rule} stage {k + 1} on-hand");
                    }

                    Assert.Equal(baseline.ProtectionErrorRms, res.ProtectionErrorRms);
                }

                var c = SerialChain.Run(cfg with { Cover = CoverRule.LotsInTransit }, d);
                _out.WriteLine($"L={l} phases {string.Join("", ph)}: Integer = A = B cover {string.Join(" ", baseline.CoverPeriods)}; C {string.Join(" ", c.CoverPeriods)}");
                Assert.All(c.CoverPeriods, v => Assert.Equal(R * ((l + R - 1) / R), v));
            }
        }
    }

    [Fact]
    public void Kaplan_SigmaZero_ExposureIdentities_NoPhantomSafetyStock()
    {
        // Base-case lag (E[L] = 6.4), R = 4, phases 0123 (upstream a = 3), perfect supplier, constant demand 100. With S = c·μ
        // and no forecast error NS(t) = (c − X(t))·μ exactly, X the exposure (CoverRules), so c − mean NS/μ is the time-average
        // exposure: E[t − τ] = E[L] + (R − 1)/2 = 7.9 at the retailer, R·E[N] = E[L] + R − a = 7.4 upstream, for every option.
        // σ̂ must be 0 (no phantom safety stock, V12): z = 2 gives the same on-hand as z = 0. Covers: B retailer = E[L] + R − 1,
        // A = B upstream (lumpy worst phase u = a − 1 for every L), A = C at the retailer for this pmf (worst phase u = R − 1).
        var lt = new KaplanLeadTime(GateACloseoutTests.LagBase);
        double el = lt.MeanLeadTime;
        const int reps = GateA.Replications, warm = GateA.Warmup, periods = GateA.Warmup + GateA.Periods;
        var flat = Enumerable.Repeat(100.0, periods).ToArray();
        double zB = GateA.BonferroniZ(12);
        bool ok = true;
        var covers = new Dictionary<CoverRule, double[]>();
        foreach (var rule in new[] { CoverRule.WorstPhase, CoverRule.CycleAverage, CoverRule.LotsInTransit })
        {
            var cfg = new ChainConfig
            {
                LeadTimes = new[] { el, el, el, el }, Window = 8, R = R, Phases = new[] { 0, 1, 2, 3 }, InitialMean = 100, Warmup = warm,
                PerfectSupplier = true, StochasticLeadTimes = new[] { lt, lt, lt, lt }, Cover = rule,
            };
            var exposure = new double[4][];
            for (int k = 0; k < 4; k++)
            {
                exposure[k] = new double[reps];
            }

            for (int rep = 0; rep < reps; rep++)
            {
                var c = cfg with { LeadTimeSeed = GateA.LeadTimeSeed(rep) };
                var r0 = SerialChain.Run(c with { Z = new[] { 0.0, 0, 0, 0 } }, flat);
                var r2 = SerialChain.Run(c with { Z = new[] { 2.0, 2, 2, 2 } }, flat);
                covers[rule] = r0.CoverPeriods;
                for (int k = 0; k < 4; k++)
                {
                    Assert.True(Math.Abs(r0.MeanOnHand[k] - r2.MeanOnHand[k]) < 1e-9, $"{rule} stage {k + 1}: phantom safety stock");
                    Assert.True(r2.MeanSigmaHat[k] < 1e-9, $"{rule} stage {k + 1}: σ̂ = {r2.MeanSigmaHat[k]:E2} at σ = 0");
                    double ns = 0;
                    for (int t = warm; t < periods; t++)
                    {
                        ns += r0.OnHandPeriod[k][t] - r0.BacklogPeriod[k][t];
                    }

                    exposure[k][rep] = r0.CoverPeriods[k] - ns / (periods - warm) / 100.0;
                }
            }

            for (int k = 0; k < 4; k++)
            {
                double reference = k == 0 ? el + (R - 1) / 2.0 : el + R - 3;
                var (mean, se) = GateA.MeanAndSe(exposure[k]);
                bool pass = GateA.Passes(reference, mean, se, zB);
                _out.WriteLine($"{rule} stage {k + 1}: cover {covers[rule][k]:0.0000}, exposure c − NS/μ = {mean:0.0000} ± {se:0.0000} vs {reference:0.0000} {(pass ? "PASS" : "FAIL")}");
                ok &= pass;
            }
        }

        Assert.True(ok);
        Assert.True(Math.Abs(covers[CoverRule.CycleAverage][0] - (el + R - 1)) < 1e-9);
        for (int k = 1; k < 4; k++)
        {
            Assert.True(Math.Abs(covers[CoverRule.WorstPhase][k] - covers[CoverRule.CycleAverage][k]) < 1e-12);
        }

        Assert.True(Math.Abs(covers[CoverRule.WorstPhase][0] - covers[CoverRule.LotsInTransit][0]) < 1e-12);
    }
}
