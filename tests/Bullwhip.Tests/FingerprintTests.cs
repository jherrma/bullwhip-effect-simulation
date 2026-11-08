using System.Security.Cryptography;
using Bullwhip.Core.Chain;
using Bullwhip.Core.Validation;
using Xunit;

namespace Bullwhip.Tests;

// Regression fingerprint (DECISIONS C29). The fixed runs below hash every output that a change to the simulation could move:
// orders, stocks, shipments, cost components and error statistics. Any change to these values changes the hash and fails
// this test. Such a change is only acceptable together with a DECISIONS entry that explains it; then update Expected.
// The configurations use literal constants (not BaseCostModel) so that the hash does not depend on the cost-model code.
public class FingerprintTests
{
    internal const string Expected = "1A4BA4A4206B34B8EED2E61723A5D85F8CDACC5CF3DF3CB3D3C801723ADAA171";
    internal const string ExpectedInfoSharing = "C8551D8708B19559C0430E258BC543121538C315DF0D7FB76A4D070FC79DEDCD";
    private const int Replications = 5;
    private const int Length = 2200;

    [Fact]
    public void FixedRuns_MatchStoredFingerprint()
    {
        Assert.Equal(Expected, Fingerprint(c => c));
    }

    // Information sharing (IS-F, flag InfoSharing = true) on the same fixed configurations. Baseline D-5: computed once on the
    // unchanged Core of step 3a, before IS-3 touches the same code. The base fingerprint above cannot see this path.
    [Fact]
    public void FixedRunsWithInfoSharing_MatchStoredFingerprint()
    {
        Assert.Equal(ExpectedInfoSharing, Fingerprint(c => c with { InfoSharing = true }));
    }

    internal static string Fingerprint(Func<ChainConfig, ChainConfig> map)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var (_, baseCfg, stochastic) in FixedConfigs())
        {
            var cfg = map(baseCfg);
            for (int rep = 0; rep < Replications; rep++)
            {
                var run = stochastic ? cfg with { LeadTimeSeed = GateA.LeadTimeSeed(rep) } : cfg;
                var res = SerialChain.Run(run, GateA.Demand(rep, Length, 100, 0, 10));
                FeedAll(sha, res.Orders);
                FeedAll(sha, res.OnHandPeriod);
                FeedAll(sha, res.BacklogPeriod);
                FeedAll(sha, res.Shipped);
                Feed(sha, res.HoldingCost);
                Feed(sha, res.CapitalCost);
                Feed(sha, res.ShortageCost);
                Feed(sha, res.ProtectionErrorRms);
                Feed(sha, res.MeanPipeline);
            }
        }

        return Convert.ToHexString(sha.GetHashAndReset());
    }

    private static List<(string Name, ChainConfig Config, bool Stochastic)> FixedConfigs()
    {
        var kaplan = new KaplanLeadTime(new[] { 0.57, 0.43 });
        double[] value = Enumerable.Range(0, 4).Select(k => 30.0 * Math.Pow(10.0 / 30.0, k / 3.0)).ToArray();
        double[] holding = value.Select(v => 0.02 * v / 52).ToArray();
        double shortage = 9 * 0.10 * value[0] / 52;
        double[] lags = Enumerable.Repeat(kaplan.MeanLeadTime, 4).ToArray();
        var kaplanStages = Enumerable.Repeat(kaplan, 4).ToArray();

        ChainConfig Stochastic(int r, double[] z, bool allowNegative) => new()
        {
            LeadTimes = lags, StochasticLeadTimes = kaplanStages, R = r, Z = z, Holding = holding, Value = value,
            CapitalRate = 0.08 / 52, ShortageCost = new[] { shortage, 0.0, 0.0, 0.0 }, AllowNegativeOrders = allowNegative, InitialMean = 100,
            Warmup = 200, Window = 8,
        };

        return new List<(string, ChainConfig, bool)>
        {
            ("kaplan R4", Stochastic(4, new[] { 1.0, 1.0, 1.0, 1.0 }, false), true),
            ("kaplan R1", Stochastic(1, new[] { 2.0, 1.0, 0.5, 0.0 }, false), true),
            ("kaplan R2", Stochastic(2, new[] { 1.5, 0.5, 0.0, 1.0 }, true), true),
            ("deterministic L2", new ChainConfig
            {
                LeadTimes = new double[] { 2, 2, 2, 2 }, R = 1, Z = new[] { 1.0, 1.0, 1.0, 1.0 }, Holding = holding,
                Value = value, CapitalRate = 0.08 / 52, ShortageCost = new[] { shortage, 0.0, 0.0, 0.0 }, AllowNegativeOrders = false,
                InitialMean = 100, Warmup = 200, Window = 8,
            }, false),
        };
    }

    private static void Feed(IncrementalHash sha, double[] values)
    {
        foreach (double x in values)
        {
            sha.AppendData(BitConverter.GetBytes(x));
        }
    }

    private static void FeedAll(IncrementalHash sha, double[][] series)
    {
        foreach (var values in series)
        {
            Feed(sha, values);
        }
    }
}
