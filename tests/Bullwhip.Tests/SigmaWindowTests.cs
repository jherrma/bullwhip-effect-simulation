using Bullwhip.Core.Chain;
using Bullwhip.Core.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Bullwhip.Tests;

/// <summary>Countermeasure 04b (longer moving window): the separate σ̂ window SigmaWindow and the longer forecast window p (stories MAW-1, MAW-2).</summary>
public class SigmaWindowTests
{
    private readonly ITestOutputHelper _out;

    public SigmaWindowTests(ITestOutputHelper output)
    {
        _out = output;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SigmaWindow_BelowOne_Throws(int window)
    {
        var cfg = CountermeasureTestChains.BaseChain() with { SigmaWindow = window };
        var ex = Assert.Throws<ArgumentException>(() => RunRep(cfg, 0));
        Assert.Contains("SigmaWindow", ex.Message);
    }

    [Fact]
    public void SigmaWindow_NullAndEqualToP_AreBitIdentical()
    {
        Assert.Equal(FingerprintTests.Expected, FingerprintTests.Fingerprint(c => c with { SigmaWindow = c.Window }));
        Assert.Equal(
            FingerprintTests.ExpectedInfoSharing,
            FingerprintTests.Fingerprint(c => c with { InfoSharing = true, SigmaWindow = c.Window }));
    }

    private static ChainConfig ShockChain(int? sigmaWindow) => new()
    {
        LeadTimes = new double[] { 1 },
        R = 1,
        Window = 8,
        Z = new double[] { 0 },
        InitialMean = 100,
        Warmup = 0,
        SigmaWindow = sigmaWindow,
    };

    internal static double[] ShockDemand()
    {
        var demand = Enumerable.Repeat(100.0, 40).ToArray();
        demand[20] = 180;
        return demand;
    }

    /// <summary>The order-time errors of the shock chain (L = 1, R = 1, cover 1, p = 8, prefilled at 100): e_t = D_t − μ̂_{t−1}.</summary>
    /// <remarks>
    /// μ̂ is 100 before t = 20, 110 for t = 20..27 (the shock 180 in the window) and 100 from t = 28, so e = 80 at t = 20,
    /// −10 at t = 21..28 and 0 elsewhere (errors exist from t = 1).
    /// </remarks>
    internal static double ShockError(int t) => t == 20 ? 80 : t >= 21 && t <= 28 ? -10 : 0;

    /// <summary>σ̂ at order time t: the RMS of the last w errors e_1 … e_t (fewer at the start), the hand rule of ErrorWindow.</summary>
    internal static double ShockSigma(int t, int w)
    {
        int first = Math.Max(1, t - w + 1);
        if (t < 1)
        {
            return 0;
        }

        double squares = 0;
        for (int s = first; s <= t; s++)
        {
            squares += ShockError(s) * ShockError(s);
        }

        return Math.Sqrt(squares / (t - first + 1));
    }

    /// <remarks>
    /// MeanSigmaHat is the mean of σ̂ over t = 0..39 (<see cref="ShockSigma"/>). w = 1: σ̂ = |e|, (80 + 8·10)/40 = 4. w = 8 and
    /// w = 16: the RMS over the last 8 or 16 errors; the literals were recomputed by a separate script from the same rule.
    /// </remarks>
    [Theory]
    [InlineData(1, 4.0)]
    [InlineData(8, 7.2498981753749066)]
    [InlineData(16, 8.984951326717487)]
    public void SigmaWindow_SetsTheRmsSampleCount(int window, double expected)
    {
        var res = SerialChain.Run(ShockChain(window), ShockDemand());
        var reference = SerialChain.Run(ShockChain(null), ShockDemand());
        _out.WriteLine($"w = {window}: MeanSigmaHat[0] = {res.MeanSigmaHat[0]:R}");
        Assert.InRange(res.MeanSigmaHat[0], expected - 1e-9, expected + 1e-9);
        Assert.Equal(reference.Orders[0], res.Orders[0]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(16)]
    public void SigmaWindow_MovesOrdersAtPositiveZ(int window)
    {
        // z = 1, unclipped: q_t = D_t + (S_t − S_{t−1}) with S_t = μ̂_t + σ̂_t, so the window enters the orders through the
        // change in σ̂. Spot values w = 1: q(20) = 180 + 10 + 80 = 270, q(21) = 100 + (10 − 80) = 30, q(28) = 100 − 10 + 0 = 90,
        // q(29) = 100 − 10 = 90. w = 8: q(20) = 190 + √(6400/8) = 218.2843.
        var res = SerialChain.Run(ShockChain(window) with { Z = new double[] { 1 } }, ShockDemand());
        double MuHat(int t) => t >= 20 && t <= 27 ? 110 : 100;
        for (int t = 1; t < 40; t++)
        {
            double expected = ShockDemand()[t] + MuHat(t) - MuHat(t - 1) + ShockSigma(t, window) - ShockSigma(t - 1, window);
            Assert.InRange(res.Orders[0][t], expected - 1e-9, expected + 1e-9);
        }

        if (window == 1)
        {
            Assert.Equal(new double[] { 100, 270, 30, 100 }, res.Orders[0][19..23]);
            Assert.Equal(new double[] { 90, 90, 100 }, res.Orders[0][28..31]);
        }

        if (window == 8)
        {
            Assert.InRange(res.Orders[0][20], 218.2843 - 5e-5, 218.2843 + 5e-5);
        }
    }

    public static IEnumerable<object[]> OtherSafetyModes()
    {
        var r1 = CountermeasureTestChains.BaseChain(0, 1);
        var r4 = CountermeasureTestChains.BaseChain();
        yield return new object[] { "chen-R1", r1 with { Safety = SafetySource.ChenOnePeriodError } };
        yield return new object[] { "ewma-R1", r1 with { Safety = SafetySource.EwmaAbsError, AbsErrorSmoothing = 2.0 / 9 } };
        yield return new object[] { "known-R4", r4 with { Safety = SafetySource.KnownSigma, KnownSigma = 10 } };
        yield return new object[] { "constmean-R4", r4 with { Safety = SafetySource.EstimatedError, ConstantMean = 100 } };
    }

    [Theory]
    [MemberData(nameof(OtherSafetyModes))]
    public void SigmaWindow_UnderOtherSafetyModes_Throws(string name, ChainConfig plain)
    {
        RunRep(plain, 0); // the same configuration without the switch runs
        var ex = Assert.Throws<ArgumentException>(() => RunRep(plain with { SigmaWindow = 8 }, 0));
        Assert.Contains("SigmaWindow", ex.Message);
        _out.WriteLine($"{name}: {ex.Message}");
    }

    [Fact]
    public void SigmaWindow_DoesNotMoveOrdersOrErrorsAtZeroZ()
    {
        var off = CountermeasureTestChains.BaseChain();
        var on = off with { SigmaWindow = 16 };
        for (int rep = 0; rep < 5; rep++)
        {
            var a = RunRep(off, rep);
            var b = RunRep(on, rep);
            for (int k = 0; k < 4; k++)
            {
                Assert.Equal(a.Orders[k], b.Orders[k]);
                Assert.Equal(a.ProtectionErrorRms[k], b.ProtectionErrorRms[k]);
                Assert.NotEqual(a.MeanSigmaHat[k], b.MeanSigmaHat[k]);
            }
        }
    }

    [Theory]
    [InlineData(16, 1.9091, 3.4281, 6.5053, 12.8599)]
    [InlineData(24, 1.5549, 2.2866, 3.4787, 5.4451)]
    [InlineData(32, 1.3970, 1.8574, 2.5254, 3.5029)]
    public void LongerForecastWindow_ClosedFormBr4(int p, double b1, double b2, double b3, double b4)
    {
        var cfg = CountermeasureTestChains.BaseChain() with { Window = p, SigmaWindow = 8 };
        var sim = CountermeasureTestChains.Br4PerStage(cfg, CountermeasureTestChains.Reps);
        var refs = CountermeasureTestChains.DecentralisedBr4(CountermeasureTestChains.Covers(0, 4), p);
        for (int k = 0; k < 4; k++)
        {
            Assert.InRange(refs[k], new[] { b1, b2, b3, b4 }[k] - 5e-4, new[] { b1, b2, b3, b4 }[k] + 5e-4);
        }

        CountermeasureTestChains.AssertClosedForm($"MA p={p}", refs, sim, _out);
    }

    private static ChainResult RunRep(ChainConfig cfg, int rep)
    {
        var demand = GateA.Demand(rep, 2200, CountermeasureTestChains.Mu, 0, CountermeasureTestChains.Sigma);
        return SerialChain.Run(cfg with { LeadTimeSeed = GateA.LeadTimeSeed(rep) }, demand);
    }
}
