using Bullwhip.Core.Costs;
using KSweep;
using Xunit;

namespace Bullwhip.Tests;

// The KSweep option --negative-orders allow: parsing, default, refusal on modes that would ignore it, and that the choice
// reaches the chain through the evaluator (one held-out replication, 200 + 2000 weeks, R = 1, z = 1 at every stage).
public class NegativeOrderOptionTests
{
    private static readonly CostParameters Costs = new();

    private static double[][] OrdersFor(params string[] args)
    {
        var (demand, rest) = DemandOption.Extract(args);
        var (negative, _) = NegativeOrderOption.Extract(rest);
        var settings = negative.ApplyTo(demand.ApplyTo(new SweepSettings { Costs = Costs }));
        return new ChainCostEvaluator(settings).Run(Costs, 1, new[] { 1.0, 1.0, 1.0, 1.0 }, 1000).Orders;
    }

    // Without the option nothing is removed from the arguments and negative orders stay clipped (base case).
    [Fact]
    public void NoOption_LeavesArgumentsAndClippingUnchanged()
    {
        string[] args = { "countermeasure", "lt", "--pattern", "jump", "out.txt" };
        var (option, rest) = NegativeOrderOption.Extract(args);
        Assert.True(option.IsDefault);
        Assert.Equal(args, rest);
        Assert.False(option.ApplyTo(new SweepSettings()).AllowNegativeOrders);
        Assert.False(new SweepSettings().AllowNegativeOrders);
    }

    // The option reaches the chain: with it, the seasonal demand (amplitude 0.3, amplified upstream) produces at least one
    // negative order; without it, no order of any stage is negative. Same replication, so the demand path is identical.
    [Fact]
    public void AllowOption_ReachesChain()
    {
        var (option, rest) = NegativeOrderOption.Extract(new[] { "base-kpi", "--negative-orders", "allow", "out.txt" });
        Assert.True(option.Allow);
        Assert.Equal(new[] { "base-kpi", "out.txt" }, rest);
        Assert.True(option.ApplyTo(new SweepSettings()).AllowNegativeOrders);

        var allowed = OrdersFor("--pattern", "seasonal", "--negative-orders", "allow");
        var clipped = OrdersFor("--pattern", "seasonal");
        Assert.Contains(allowed.SelectMany(o => o), v => v < 0);
        Assert.DoesNotContain(clipped.SelectMany(o => o), v => v < 0);
    }

    [Theory]
    [InlineData("--negative-orders")]
    [InlineData("--negative-orders", "clip")]
    [InlineData("--negative-orders", "Allow")]
    [InlineData("--negative-orders", "true")]
    [InlineData("--negative-orders", "allow", "--negative-orders", "allow")]
    public void InvalidOptions_Throw(params string[] args)
    {
        Assert.ThrowsAny<ArgumentException>(() => NegativeOrderOption.Extract(args));
    }

    // A mode that does not build on the base sweep would ignore the option; it must refuse it. The default passes everywhere.
    [Fact]
    public void AllowOption_IsRefusedByModesThatIgnoreIt()
    {
        var (allow, _) = NegativeOrderOption.Extract(new[] { "--negative-orders", "allow" });
        foreach (string mode in new[] { "upstream", "larger-beta", "single-stage", "gate-b", "k-sweep", "k-factor", "k-bme-neg" })
        {
            Assert.Throws<ArgumentException>(() => allow.EnsureSupported(mode));
            new NegativeOrderOption().EnsureSupported(mode);
        }

        foreach (string mode in NegativeOrderOption.SupportedModes)
        {
            allow.EnsureSupported(mode);
        }
    }
}
