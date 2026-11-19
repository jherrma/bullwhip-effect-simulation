using Bullwhip.Core.Chain;
using Bullwhip.Core.Rng;
using Xunit;

namespace Bullwhip.Tests;

/// <summary>
/// Hand trace of one stage with Kaplan arrivals at R = 2: lag pmf P(T = 0..3) = (0, 0.25, 0.5, 0.25), so L in 2..4 and
/// E[L] = 3, p = 2, z = 0, no prefill, eight periods. Lead-time seed 1: the stream SplitMix64.Derive(1, 0) delivers the
/// order of t = 0 after L = 3 (at t = 3) and that of t = 2 after L = 2 (at t = 4); these two values come from a
/// separate SplitMix64 and Kaplan-cut implementation and are checked as a precondition.
/// </summary>
public class StochasticArrivalTraceTests
{
    private static readonly double[] LagPmf = { 0, 0.25, 0.5, 0.25 };

    /// <remarks>
    /// Cover c = L + R − 1 = 4 (integer planning L, retailer). μ̂ at an order time is the mean of the closed bucket: t = 0
    /// holds one period (10), later buckets two periods: μ̂(2) = 15, μ̂(4) = 10, μ̂(6) = 20. IP after the demand of t:
    /// IP(0) = −10, IP(2) = −10 + 50 − 30 = 10, IP(4) = 10 + 50 − 20 = 40, IP(6) = 40 − 40 = 0. Orders q = 4·μ̂ − IP:
    /// 50, 50, 0, 80. Stock: backlog 10, 20, 40 until the 50 of t = 0 arrives at t = 3 and clears it; the 50 of t = 2
    /// arrives at t = 4 and serves 10, 10, 30 from on-hand 50; t = 7 is short by 10. The order of t = 6 is due after the
    /// horizon.
    /// </remarks>
    [Fact]
    public void KaplanArrivals_AtROfTwo_FollowTheHandTrace()
    {
        var lag = new KaplanLeadTime(LagPmf);
        var due = lag.DrawArrivals(new SplitMix64(SplitMix64.Derive(1, 0)), 7);
        Assert.Equal(3, due[0 + lag.MaxLag + 1]);
        Assert.Equal(4, due[2 + lag.MaxLag + 1]);

        var cfg = new ChainConfig
        {
            LeadTimes = new[] { 3.0 },
            StochasticLeadTimes = new[] { lag },
            LeadTimeSeed = 1,
            R = 2,
            Window = 2,
            Z = new[] { 0.0 },
            InitialMean = 0,
            Warmup = 0,
        };
        var res = SerialChain.Run(cfg, new double[] { 10, 10, 20, 10, 10, 10, 30, 10 });

        Assert.Equal(4.0, res.CoverPeriods[0]);
        Assert.Equal(new double[] { 50, 0, 50, 0, 0, 0, 80, 0 }, res.Orders[0]);
        Assert.Equal(new double[] { 0, 0, 0, 50, 10, 10, 30, 0 }, res.Shipped[0]);
        Assert.Equal(new double[] { 0, 0, 0, 0, 40, 30, 0, 0 }, res.OnHandPeriod[0]);
        Assert.Equal(new double[] { 10, 20, 40, 0, 0, 0, 0, 10 }, res.BacklogPeriod[0]);
    }
}
