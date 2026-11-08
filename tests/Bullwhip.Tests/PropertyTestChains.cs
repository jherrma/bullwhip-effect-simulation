using Bullwhip.Core.Chain;
using Bullwhip.Core.Validation;

namespace Bullwhip.Tests;

/// <summary>Shared configurations and runner for the property tests (ChainInvariantTests, ChainCostPropertyTests, DemandGeneratorTests).</summary>
internal static class PropertyTestChains
{
    public const int Length = 600;
    public const int Warmup = 100;
    public const double Mu = 100;
    public const double Sigma = 25;
    public const int Seeds = 10;
    public const double Tolerance = 1e-7;

    public static readonly double[] Holding = { 0.30, 0.21, 0.15, 0.10 };
    public static readonly double[] Value = { 30, 20.8, 14.4, 10 };
    public static readonly double[] OrderCost = { 46.2, 32.0, 22.2, 15.4 };
    public static readonly double[] ShortageBase = { 2.7, 0.4, 0.3, 0.2 };

    private static KaplanLeadTime[] Kaplan() => Enumerable.Repeat(new KaplanLeadTime(new[] { 0.57, 0.43 }), 4).ToArray();

    /// <summary>Named configurations: Kaplan and fixed lead times, R = 1 and R = 4, safety stock, a perfect supplier, shared POS.</summary>
    public static IEnumerable<(string Name, ChainConfig Config)> Configs()
    {
        double[] z = { 1.0, 1.0, 1.0, 1.0 };
        yield return ("kaplan-R4", Base(1.43, 4, Kaplan()) with { Z = new double[4] });
        yield return ("kaplan-R4-z1", Base(1.43, 4, Kaplan()) with { Z = z });
        yield return ("kaplan-R1-z1", Base(1.43, 1, Kaplan()) with { Z = z });
        yield return ("fixedL2-R1", Base(2, 1, null) with { Z = z });
        yield return ("fixedL3-R2-z1", Base(3, 2, null) with { Z = z });
        yield return ("kaplan-R4-perfect", Base(1.43, 4, Kaplan()) with { Z = z, PerfectSupplier = true });
        yield return ("kaplan-R1-infosharing", Base(1.43, 1, Kaplan()) with { Z = z, InfoSharing = true });
        yield return ("kaplan-R4-infosharing", Base(1.43, 4, Kaplan()) with { Z = z, InfoSharing = true });
        yield return ("kaplan-R4-infosharing-sigma-incoming", Base(1.43, 4, Kaplan()) with { Z = z, InfoSharing = true, InfoSharingSigmaFromIncomingOrders = true });
    }

    private static ChainConfig Base(double leadTime, int r, KaplanLeadTime[]? lag) => new()
    {
        LeadTimes = Enumerable.Repeat(leadTime, 4).ToArray(), StochasticLeadTimes = lag, R = r, Window = 8,
        AllowNegativeOrders = false, InitialMean = Mu, Warmup = Warmup,
        Holding = Holding, Value = Value, CapitalRate = 0.05 / 52, OrderCost = OrderCost, ShortageCost = ShortageBase,
    };

    public static double[] Demand(int rep) => GateA.Demand(rep, Length, Mu, 0.0, Sigma);

    public static ChainResult Run(ChainConfig config, int rep) =>
        SerialChain.Run(config with { LeadTimeSeed = GateA.LeadTimeSeed(rep) }, Demand(rep));
}
