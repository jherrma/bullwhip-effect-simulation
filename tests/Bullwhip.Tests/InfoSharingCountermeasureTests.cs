using Bullwhip.Core.Chain;
using Bullwhip.Core.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Bullwhip.Tests;

/// <summary>Countermeasure 01 (information sharing, IS-F) at R = 4 against the closed forms (story IS-2).</summary>
public class InfoSharingCountermeasureTests
{
    private static readonly double[] BaseZ = { 2.5, 1.25, 1.25, 1.25 };
    private static readonly double[] Covers = { 4.43, 4, 4, 4 };
    private readonly ITestOutputHelper _out;

    public InfoSharingCountermeasureTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static ChainConfig Clipped(bool infoSharing, bool perfect = false) =>
        CountermeasureTestChains.BaseChain() with { Z = BaseZ, AllowNegativeOrders = false, InfoSharing = infoSharing, PerfectSupplier = perfect };

    [Fact]
    public void InfoSharingR4_ClosedFormBr4()
    {
        var cfg = CountermeasureTestChains.BaseChain() with { InfoSharing = true };
        var sim = CountermeasureTestChains.Br4PerStage(cfg, CountermeasureTestChains.Reps);
        CountermeasureTestChains.AssertClosedForm("IS-F R4", CountermeasureTestChains.InfoSharingBr4(Covers, 8), sim, _out);
    }

    [Fact]
    public void InfoSharingR4_ErrorStatisticsAreBitIdenticalAcrossStages()
    {
        const int rep = 5;
        var demand = GateA.Demand(rep, 2200, CountermeasureTestChains.Mu, 0, CountermeasureTestChains.Sigma);
        var res = SerialChain.Run(Clipped(true) with { LeadTimeSeed = GateA.LeadTimeSeed(rep) }, demand);
        _out.WriteLine($"ProtectionErrorRms[0] = {res.ProtectionErrorRms[0]:R}, MeanSigmaHat[0] = {res.MeanSigmaHat[0]:R}");
        for (int k = 1; k < 4; k++)
        {
            Assert.Equal(res.ProtectionErrorRms[0], res.ProtectionErrorRms[k]);
            Assert.Equal(res.MeanSigmaHat[0], res.MeanSigmaHat[k]);
        }
    }

    [Fact]
    public void InfoSharing_LeavesRetailerOrdersUnchanged()
    {
        foreach (bool perfect in new[] { true, false })
        {
            double maxDiff = 0;
            for (int rep = 0; rep < 10; rep++)
            {
                var demand = GateA.Demand(rep, 2200, CountermeasureTestChains.Mu, 0, CountermeasureTestChains.Sigma);
                var seed = GateA.LeadTimeSeed(rep);
                var off = SerialChain.Run(Clipped(false, perfect) with { LeadTimeSeed = seed }, demand).Orders[0];
                var on = SerialChain.Run(Clipped(true, perfect) with { LeadTimeSeed = seed }, demand).Orders[0];
                if (perfect)
                {
                    Assert.Equal(off, on);
                }

                maxDiff = Math.Max(maxDiff, off.Zip(on, (a, b) => Math.Abs(a - b)).Max());
            }

            _out.WriteLine($"perfectSupplier={perfect}: max |delta| of retailer orders = {maxDiff:R}");
            if (!perfect)
            {
                Assert.True(maxDiff <= 1e-9, $"max |delta| = {maxDiff}");
            }
        }
    }

    [Fact]
    public void InfoSharingR4_ErrorRmsMatchesPosLevel()
    {
        const double expected = 26.236; // 10 * sqrt(4.43 + 4.43^2 / 8)
        var sums = new double[4];
        const int reps = 50;
        for (int rep = 0; rep < reps; rep++)
        {
            var demand = GateA.Demand(rep, 2200, CountermeasureTestChains.Mu, 0, CountermeasureTestChains.Sigma);
            var res = SerialChain.Run(Clipped(true) with { LeadTimeSeed = GateA.LeadTimeSeed(rep) }, demand);
            for (int k = 0; k < 4; k++)
            {
                sums[k] += res.ProtectionErrorRms[k] * res.ProtectionErrorRms[k];
            }
        }

        for (int k = 0; k < 4; k++)
        {
            double pooled = Math.Sqrt(sums[k] / reps);
            _out.WriteLine($"stage {k + 1}: pooled error rms = {pooled:0.000} (expected {expected})");
            Assert.InRange(pooled, expected * 0.98, expected * 1.02);
        }
    }

    [Fact]
    public void IsM_FalseExplicit_MatchesStoredFingerprints()
    {
        Assert.Equal(FingerprintTests.Expected, FingerprintTests.Fingerprint(c => c with { InfoSharingSigmaFromIncomingOrders = false }));
        Assert.Equal(
            FingerprintTests.ExpectedInfoSharing,
            FingerprintTests.Fingerprint(c => c with { InfoSharing = true, InfoSharingSigmaFromIncomingOrders = false }));
    }

    private static ChainResult RunRep(ChainConfig cfg, int rep)
    {
        var demand = GateA.Demand(rep, 2200, CountermeasureTestChains.Mu, 0, CountermeasureTestChains.Sigma);
        return SerialChain.Run(cfg with { LeadTimeSeed = GateA.LeadTimeSeed(rep) }, demand);
    }

    [Fact]
    public void IsM_AtZeroZ_OrdersEqualIsF_AndOnlyUpstreamErrorsDiffer()
    {
        var isF = CountermeasureTestChains.BaseChain() with { InfoSharing = true };
        var isM = isF with { InfoSharingSigmaFromIncomingOrders = true };
        for (int rep = 0; rep < 5; rep++)
        {
            var f = RunRep(isF, rep);
            var m = RunRep(isM, rep);
            for (int k = 0; k < 4; k++)
            {
                Assert.Equal(f.Orders[k], m.Orders[k]);
            }

            Assert.Equal(f.ProtectionErrorRms[0], m.ProtectionErrorRms[0]);
            for (int k = 1; k < 4; k++)
            {
                Assert.NotEqual(f.ProtectionErrorRms[k], m.ProtectionErrorRms[k]);
            }
        }
    }

    [Fact]
    public void IsM_RetailerErrorsEqualTheBase()
    {
        var baseCfg = Clipped(false);
        var isM = Clipped(true) with { InfoSharingSigmaFromIncomingOrders = true };
        for (int rep = 0; rep < 5; rep++)
        {
            var b = RunRep(baseCfg, rep);
            var m = RunRep(isM, rep);
            Assert.Equal(b.ProtectionErrorRms[0], m.ProtectionErrorRms[0]);
            Assert.Equal(b.MeanSigmaHat[0], m.MeanSigmaHat[0]);
        }
    }

    [Fact]
    public void IsM_Stage2ErrorRms_MatchesClosedForm()
    {
        const double expected = 38.856; // 20 * sqrt((1 + c1/8)^2 + 0.5^2 + (c1/8 + 0.5)^2), c1 = 4.43
        const int reps = 100;
        var cfg = CountermeasureTestChains.BaseChain() with { InfoSharing = true, InfoSharingSigmaFromIncomingOrders = true };
        double sum = 0;
        for (int rep = 0; rep < reps; rep++)
        {
            double rms = RunRep(cfg, rep).ProtectionErrorRms[1];
            sum += rms * rms;
        }

        double pooled = Math.Sqrt(sum / reps);
        _out.WriteLine($"IS-M stage 2: pooled error rms = {pooled:0.000} (expected {expected})");
        Assert.InRange(pooled, expected * 0.98, expected * 1.02);
    }

    [Fact]
    public void IsM_WithoutInfoSharing_Throws()
    {
        var cfg = CountermeasureTestChains.BaseChain() with { InfoSharingSigmaFromIncomingOrders = true };
        var ex = Assert.Throws<ArgumentException>(() => RunRep(cfg, 0));
        Assert.Contains("InfoSharingSigmaFromIncomingOrders", ex.Message);
    }

    public static IEnumerable<object[]> OtherSafetyModes()
    {
        var r1 = CountermeasureTestChains.BaseChain(0.43, 1);
        var r4 = CountermeasureTestChains.BaseChain();
        yield return new object[] { "chen-R1", r1 with { Safety = SafetySource.ChenOnePeriodError } };
        yield return new object[] { "ewma-R1", r1 with { Safety = SafetySource.EwmaAbsError, AbsErrorSmoothing = 2.0 / 9 } };
        yield return new object[] { "known-R4", r4 with { Safety = SafetySource.KnownSigma, KnownSigma = 10 } };
        yield return new object[] { "constmean-R4", r4 with { Safety = SafetySource.EstimatedError, ConstantMean = 100 } };
    }

    [Theory]
    [MemberData(nameof(OtherSafetyModes))]
    public void IsM_UnderOtherSafetyModes_Throws(string name, ChainConfig plain)
    {
        var withInfoSharing = plain with { InfoSharing = true };
        RunRep(withInfoSharing, 0); // the same configuration without the switch runs
        var ex = Assert.Throws<ArgumentException>(() => RunRep(withInfoSharing with { InfoSharingSigmaFromIncomingOrders = true }, 0));
        Assert.Contains("InfoSharingSigmaFromIncomingOrders", ex.Message);
        _out.WriteLine($"{name}: {ex.Message}");
    }
}
