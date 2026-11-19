using Bullwhip.Core.Chain;
using Xunit;

namespace Bullwhip.Tests;

/// <summary>
/// Rejection paths of SerialChain.Validate (every failure is an ArgumentException), of the KaplanLeadTime constructor
/// (ArgumentException) and of the CoverRules and ProtectionWindow guards (ArgumentOutOfRangeException, or ArgumentException
/// for the Integer rule). Each case changes one field of a configuration that runs.
/// </summary>
public class ValidationGuardTests
{
    private static readonly double[] Demand = Enumerable.Repeat(100.0, 40).ToArray();

    private static KaplanLeadTime BaseLag() => new(BaseLeadTime.LagPmf());

    private static ChainConfig Valid() => new()
    {
        LeadTimes = new double[] { 2, 2 },
        Window = 8,
        R = 1,
        InitialMean = 100,
    };

    private static ChainConfig ValidKaplan() => Valid() with
    {
        LeadTimes = new[] { BaseLeadTime.MeanLeadTime, BaseLeadTime.MeanLeadTime },
        StochasticLeadTimes = new[] { BaseLag(), BaseLag() },
    };

    private static void Rejects(ChainConfig cfg) => Assert.Throws<ArgumentException>(() => SerialChain.Run(cfg, Demand));

    [Fact]
    public void ValidConfigurations_Run()
    {
        SerialChain.Run(Valid(), Demand);
        SerialChain.Run(ValidKaplan(), Demand);
        SerialChain.Run(ValidKaplan() with { R = 4 }, Demand);
    }

    [Fact]
    public void ZeroStages_AreRejected() => Rejects(Valid() with { LeadTimes = Array.Empty<double>() });

    [Fact]
    public void CostArraysShorterThanTheChain_AreRejected()
    {
        Rejects(Valid() with { OrderCost = new[] { 1.0 } });
        Rejects(Valid() with { ShortageCost = new[] { 1.0 } });
        SerialChain.Run(Valid() with { OrderCost = new[] { 1.0, 1.0 }, ShortageCost = new[] { 1.0, 1.0 } }, Demand);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(-1, 8)]
    [InlineData(3, 8)]
    [InlineData(16, 8)]
    [InlineData(4, 6)]
    public void OrderIntervalBelowOneOrNotDividingTheWindow_IsRejected(int r, int window) =>
        Rejects(Valid() with { R = r, Window = window });

    [Fact]
    public void ChenAndEwmaSigma_AtROtherThanOne_AreRejected()
    {
        Rejects(Valid() with { R = 2, Safety = SafetySource.ChenOnePeriodError });
        Rejects(Valid() with { R = 2, Safety = SafetySource.EwmaAbsError, AbsErrorSmoothing = 0.2 });
        Rejects(Valid() with { Safety = SafetySource.EwmaAbsError, AbsErrorSmoothing = 0 });
        Rejects(Valid() with { Safety = SafetySource.EwmaAbsError, AbsErrorSmoothing = 1.5 });
        SerialChain.Run(Valid() with { Safety = SafetySource.ChenOnePeriodError }, Demand);
        SerialChain.Run(Valid() with { Safety = SafetySource.EwmaAbsError, AbsErrorSmoothing = 1.0 }, Demand);
    }

    [Fact]
    public void KaplanEntryCountMismatch_IsRejected()
    {
        Rejects(ValidKaplan() with { StochasticLeadTimes = new[] { BaseLag() } });
        Rejects(ValidKaplan() with { StochasticLeadTimes = new[] { BaseLag(), BaseLag(), BaseLag() } });
    }

    [Fact]
    public void PlanningLeadTimeOtherThanTheKaplanMean_IsRejected()
    {
        Rejects(ValidKaplan() with { LeadTimes = new[] { 2.43, 2.5 } });
        Rejects(ValidKaplan() with { LeadTimes = new[] { 2.0, 2.43 } });
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteLeadTime_IsRejected(double l)
    {
        Rejects(Valid() with { LeadTimes = new[] { 2.0, l } });
        Rejects(ValidKaplan() with { LeadTimes = new[] { 2.43, l } });
    }

    [Fact]
    public void NonIntegerLeadTimeWithoutKaplan_IsRejected()
    {
        Rejects(Valid() with { LeadTimes = new[] { 2.0, 2.43 } });
        Rejects(Valid() with { LeadTimes = new[] { 2.0, 2.43 }, R = 4, Cover = CoverRule.CycleAverage });
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    public void NegativeOrNaNEscalation_IsRejected(double kappa) => Rejects(Valid() with { ShortageEscalation = kappa });

    [Fact]
    public void KaplanConstructor_RejectsAnInvalidPmf()
    {
        Assert.Throws<ArgumentException>(() => new KaplanLeadTime(Array.Empty<double>()));
        Assert.Throws<ArgumentException>(() => new KaplanLeadTime(new[] { 0.5, 0.6, -0.1 }));
        Assert.Throws<ArgumentException>(() => new KaplanLeadTime(new[] { 0.5, double.NaN, 0.5 }));
        Assert.Throws<ArgumentException>(() => new KaplanLeadTime(new[] { 0.5, 0.4 }));
        Assert.Throws<ArgumentException>(() => new KaplanLeadTime(new[] { 0.5, 0.5, 0.0 }));
    }

    [Fact]
    public void KaplanConstructor_RejectsAFallingHazard()
    {
        // (0.5, 0.1, 0.4): h_0 = 0.5, h_1 = 0.1/0.5 = 0.2 < h_0.
        var ex = Assert.Throws<ArgumentException>(() => new KaplanLeadTime(new[] { 0.5, 0.1, 0.4 }));
        Assert.Contains("Hazard falls", ex.Message);

        // (0.5, 0.25, 0.25): h_0 = 0.5, h_1 = 0.25/0.5 = 0.5, h_2 = 1 (non-decreasing) is accepted.
        _ = new KaplanLeadTime(new[] { 0.5, 0.25, 0.25 });
    }

    [Fact]
    public void BasePmfMixedWithAPointMassAtTransitOne_IsNotKaplanRepresentable()
    {
        // ½·base + ½·δ(T = 1): h_1 = ½ + ½·0.6897 = 0.8449, then h_2 = ½·0.2140 / (½·(1 − 0.6897)) = 0.6897 < h_1.
        var mixed = BaseLeadTime.LagPmf().Select(p => 0.5 * p).ToArray();
        mixed[1] += 0.5;
        var ex = Assert.Throws<ArgumentException>(() => new KaplanLeadTime(mixed));
        Assert.Contains("Hazard falls at lag 2", ex.Message);
    }

    [Fact]
    public void CoverRulesAndProtectionWindow_RejectInvalidArguments()
    {
        var pmf = CoverRules.LeadPmf(BaseLag(), 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => CoverRules.LeadPmf(null, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CoverRules.Cover(CoverRule.CycleAverage, pmf, 0, null));
        Assert.Throws<ArgumentException>(() => CoverRules.Cover(CoverRule.Integer, pmf, 4, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProtectionWindow.Weights(0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProtectionWindow.Weights(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => ProtectionWindow.Weights(double.PositiveInfinity));
    }
}
