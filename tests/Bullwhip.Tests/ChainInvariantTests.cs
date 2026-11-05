using Bullwhip.Core.Chain;
using Xunit;

namespace Bullwhip.Tests;

// Property tests of the chain bookkeeping over several configurations and 10 fixed seeds, whole run (warm-up included).
public class ChainInvariantTests
{
    private static void ForEachRun(Action<string, ChainConfig, ChainResult, double[]> check)
    {
        foreach (var (name, config) in PropertyTestChains.Configs())
        {
            for (int rep = 0; rep < PropertyTestChains.Seeds; rep++)
            {
                check($"{name} rep {rep}", config, PropertyTestChains.Run(config, rep), PropertyTestChains.Demand(rep));
            }
        }
    }

    private static void Near(double expected, double actual, string label)
    {
        Assert.True(Math.Abs(expected - actual) <= PropertyTestChains.Tolerance * Math.Max(1.0, Math.Abs(expected)),
            $"{label}: expected {expected}, got {actual}");
    }

    // Invariant: backlog_t = backlog_{t-1} + incoming_t - shipped_t (backlog_{-1} = 0), per stage and period.
    [Fact]
    public void BacklogBalance_HoldsInEveryPeriod()
    {
        ForEachRun((label, _, res, _) =>
        {
            for (int k = 0; k < res.Orders.Length; k++)
            {
                double previous = 0.0;
                for (int t = 0; t < res.Orders[k].Length; t++)
                {
                    Near(previous + res.Incoming[k][t] - res.Shipped[k][t], res.BacklogPeriod[k][t], $"{label} stage {k} t {t}");
                    previous = res.BacklogPeriod[k][t];
                }
            }
        });
    }

    // Invariant: the receipt implied by the on-hand balance, onHand_t - onHand_{t-1} + shipped_t, is >= 0 each period
    // (orders are >= 0) and sums to ArrivedTotal; the last period equals the end-state arrays.
    [Fact]
    public void OnHandBalance_ImpliesNonNegativeReceiptsSummingToArrivedTotal()
    {
        ForEachRun((label, _, res, _) =>
        {
            for (int k = 0; k < res.Orders.Length; k++)
            {
                double previous = 0.0, received = 0.0;
                for (int t = 0; t < res.Orders[k].Length; t++)
                {
                    double arrival = res.OnHandPeriod[k][t] - previous + res.Shipped[k][t];
                    Assert.True(arrival >= -PropertyTestChains.Tolerance, $"{label} stage {k} t {t}: receipt {arrival}");
                    received += arrival;
                    previous = res.OnHandPeriod[k][t];
                }

                int last = res.Orders[k].Length - 1;
                Near(res.ArrivedTotal[k], received, $"{label} stage {k} arrivals");
                Near(res.OnHandEnd[k], res.OnHandPeriod[k][last], $"{label} stage {k} on-hand end");
                Near(res.BacklogEnd[k], res.BacklogPeriod[k][last], $"{label} stage {k} backlog end");
            }
        });
    }

    // Invariant: totals are the sums of the per-period arrays; stage k > 0 sees exactly the orders of stage k-1;
    // the retailer sees the demand series unchanged.
    [Fact]
    public void Totals_AreSumsOfPeriodArrays_AndIncomingIsTheDownstreamOrder()
    {
        ForEachRun((label, _, res, demand) =>
        {
            for (int k = 0; k < res.Orders.Length; k++)
            {
                Near(res.ShippedTotal[k], res.Shipped[k].Sum(), $"{label} stage {k} shipped total");
                Near(res.IncomingTotal[k], res.Incoming[k].Sum(), $"{label} stage {k} incoming total");
                double[] expected = k == 0 ? demand : res.Orders[k - 1];
                Assert.Equal(expected, res.Incoming[k]);
            }
        });
    }

    // Invariant (pipeline, per stage): in transit at the end = initial pipeline + dispatched - arrived.
    [Fact]
    public void Pipeline_EndEqualsInitialPlusDispatchedMinusArrived_PerStage()
    {
        ForEachRun((label, _, res, _) =>
        {
            for (int k = 0; k < res.Orders.Length; k++)
            {
                Near(res.ScheduledInitial[k] + res.ScheduledAdded[k] - res.ArrivedTotal[k], res.ScheduledEnd[k], $"{label} stage {k}");
            }
        });
    }

    // Invariant: orders are >= 0 (AllowNegativeOrders = false); orders are placed only at the stage's order times
    // (t - phase) mod R = 0; OrderCount is the number of positive orders.
    [Fact]
    public void Orders_AreNonNegative_OnlyAtOrderTimes_AndCounted()
    {
        ForEachRun((label, config, res, _) =>
        {
            for (int k = 0; k < res.Orders.Length; k++)
            {
                int phase = config.Phases?[k] ?? 0;
                for (int t = 0; t < res.Orders[k].Length; t++)
                {
                    Assert.True(res.Orders[k][t] >= 0.0, $"{label} stage {k} t {t}: order {res.Orders[k][t]}");
                    Assert.True(res.Orders[k][t] == 0.0 || (t - phase) % config.R == 0, $"{label} stage {k} t {t}: off-time order");
                }

                Assert.Equal(res.Orders[k].Count(q => q > 0.0), res.OrderCount[k]);
            }
        });
    }

    // Invariant: on-hand >= 0, backlog >= 0, shipped >= 0, shipped_t <= incoming_t + backlog_{t-1}, and a stage never
    // holds stock and a backlog at once. A perfect supplier ships upstream demand regardless of stock, so for it only
    // the retailer is held to the stock rules.
    [Fact]
    public void Stocks_AndShipments_AreNonNegativeAndBounded()
    {
        ForEachRun((label, config, res, _) =>
        {
            for (int k = 0; k < res.Orders.Length; k++)
            {
                double carried = 0.0;
                bool limitedByStock = !(config.PerfectSupplier && k > 0);
                for (int t = 0; t < res.Orders[k].Length; t++)
                {
                    double shipped = res.Shipped[k][t];
                    string where = $"{label} stage {k} t {t}";
                    Assert.True(shipped >= 0.0 && res.BacklogPeriod[k][t] >= -PropertyTestChains.Tolerance, where);
                    Assert.True(shipped <= res.Incoming[k][t] + carried + PropertyTestChains.Tolerance, $"{where}: shipped {shipped}");
                    Assert.True(!limitedByStock || res.OnHandPeriod[k][t] >= -PropertyTestChains.Tolerance, $"{where}: on-hand");
                    Assert.True(!limitedByStock || Math.Min(res.OnHandPeriod[k][t], res.BacklogPeriod[k][t]) <= PropertyTestChains.Tolerance,
                        $"{where}: stock and backlog together");
                    carried = res.BacklogPeriod[k][t];
                }
            }
        });
    }

    // Invariant: window means are the means of the period arrays; fill rate = window shipped / window demand (may exceed 1 when the window ships earlier backlog); ready rate lies in [0, 1]; nothing ships beyond the demand.
    [Fact]
    public void WindowMeans_AndServiceLevels_MatchPeriodArrays()
    {
        ForEachRun((label, _, res, _) =>
        {
            int from = res.Warmup, to = res.Orders[0].Length;
            for (int k = 0; k < res.Orders.Length; k++)
            {
                Near(res.OnHandPeriod[k].Skip(from).Select(v => Math.Max(0.0, v)).Sum() / (to - from), res.MeanOnHand[k], $"{label} on-hand {k}");
                Near(res.BacklogPeriod[k].Skip(from).Sum() / (to - from), res.MeanBacklog[k], $"{label} backlog {k}");
            }

            double demanded = res.Incoming[0].Skip(from).Sum();
            Near(res.Shipped[0].Skip(from).Sum() / demanded, res.FillRate, $"{label} fill rate");
            Assert.True(res.ShippedTotal[0] <= res.IncomingTotal[0] + PropertyTestChains.Tolerance, label);
            Assert.InRange(res.ReadyRate, 0.0, 1.0);
        });
    }

    // Invariant: the simulation is a pure function of (configuration, demand, seed) and does not modify its inputs.
    [Fact]
    public void Run_IsDeterministic_AndLeavesTheDemandArrayUntouched()
    {
        var config = PropertyTestChains.Configs().First(c => c.Name == "kaplan-R4-z1").Config;
        var demand = PropertyTestChains.Demand(3);
        var copy = (double[])demand.Clone();
        var cfg = config with { LeadTimeSeed = Bullwhip.Core.Validation.GateA.LeadTimeSeed(3) };
        var a = SerialChain.Run(cfg, demand);
        var b = SerialChain.Run(cfg, demand);

        Assert.Equal(copy, demand);
        for (int k = 0; k < 4; k++)
        {
            Assert.Equal(a.Orders[k], b.Orders[k]);
            Assert.Equal(a.OnHandPeriod[k], b.OnHandPeriod[k]);
        }
    }
}
