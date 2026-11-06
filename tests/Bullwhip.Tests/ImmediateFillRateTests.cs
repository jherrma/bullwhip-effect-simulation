using Bullwhip.Core.Chain;
using Xunit;

namespace Bullwhip.Tests;

// Immediate fill rate (DECISIONS C46): sum of min(D_t, V_t) / sum of D_t, with V_t = max(0, on-hand after the arrivals of t
// minus the backlog carried into t).
public class ImmediateFillRateTests
{
    // One stage, L = 1, R = 1, constant forecast 10, prefilled at 10, no safety stock, order-up-to level S = 10.
    // Demand 10, 15, 5, 10, 10 (sum 50). Hand computation:
    //   t0: arrival 10 (prefill), V = 10, D = 10 -> min = 10, on-hand 0.
    //   t1: arrival 10, V = 10 (no backlog carried in), D = 15 -> min = 10; 5 units go to backlog.
    //   t2: arrival 15 (the order placed at t1 is 15), on-hand 15, backlog carried 5 -> V = 10, D = 5 -> min = 5.
    //   t3, t4: the order placed after each period restores the position to 10, so V = 10 = D -> min = 10 each.
    // Immediate fill rate = (10 + 10 + 5 + 10 + 10) / 50 = 45 / 50 = 0.9. Only t1 falls short (5 units).
    // The fill rate in Result (shipped / demanded, later shipments included) counts the backlog shipped at t2: 1.0.
    [Fact]
    public void ImmediateFillRate_CountsOnlySameWeekUnits_WhileFillRateCountsLateShipments()
    {
        var config = new ChainConfig
        {
            LeadTimes = new[] { 1.0 }, Window = 1, R = 1, ConstantMean = 10, InitialMean = 10, Warmup = 0, AllowNegativeOrders = false,
        };
        var res = SerialChain.Run(config, new double[] { 10, 15, 5, 10, 10 });
        Assert.Equal(0.9, res.ImmediateFillRate, 10);
        Assert.Equal(1.0, res.FillRate, 10);
    }

    [Fact]
    public void ImmediateFillRate_StockOwedToEarlierBacklogIsNotCountedForTodaysDemand()
    {
        // Same chain, demand 10, 15, 12, 10, 10 (sum 57). Hand computation with V_t = on-hand after arrivals less the backlog of
        // earlier periods (C46):
        //   t0: V = 10, D = 10 -> 10.
        //   t1: V = 10, D = 15 -> 10; backlog 5.
        //   t2: arrival 15, on-hand 15, backlog carried 5 -> V = 10, D = 12 -> 10. Shipped 15 (5 old + 10 new); backlog 2.
        //   t3: arrival 12, on-hand 12, backlog carried 2 -> V = 10, D = 10 -> 10.
        //   t4: V = 10, D = 10 -> 10.
        // Immediate fill rate = 50 / 57 = 0.877193. Without subtracting the earlier backlog the measure would be 52 / 57.
        var config = new ChainConfig
        {
            LeadTimes = new[] { 1.0 }, Window = 1, R = 1, ConstantMean = 10, InitialMean = 10, Warmup = 0, AllowNegativeOrders = false,
        };
        var res = SerialChain.Run(config, new double[] { 10, 15, 12, 10, 10 });
        Assert.Equal(50.0 / 57.0, res.ImmediateFillRate, 10);
    }
}
