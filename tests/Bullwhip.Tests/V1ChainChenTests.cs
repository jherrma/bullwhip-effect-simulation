using Bullwhip.Core.Analytic;
using Bullwhip.Core.Chain;
using Bullwhip.Core.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Bullwhip.Tests;

/// <summary>
/// V1b: the V1 grid run through <see cref="SerialChain"/> itself (one stage, R = 1, z = 0, unclipped orders) against Chen et
/// al. (2000), Eq. (5), Thm 2.2, p. 438. V1 (<see cref="ChenSingleStageCheck"/>) checks the order arithmetic of Eq. (4) in
/// <see cref="SingleStageOrders"/>; this check covers the period loop of the chain for L = 1, 2, 4 and ρ = 0, ±0.5.
/// </summary>
/// <remarks>
/// Reference side: the closed form <see cref="ChenFormulas.SingleStageBullwhipRatio"/> only, which uses no forecaster. At
/// z = 0 the bound of Eq. (5) holds with equality for every grid point (iid and AR(1)), so no case needs a simulated
/// reference. Demand, replications, denominator (stationary variance) and the pass rule are those of V1: 100 replications of
/// 2000 periods after 200 warm-up periods, |BR_sim/BR_ana − 1| ≤ 2 % and BR_ana inside the Bonferroni interval over the 36
/// grid points.
/// </remarks>
public class V1ChainChenTests
{
    private readonly ITestOutputHelper _out;

    public V1ChainChenTests(ITestOutputHelper output)
    {
        _out = output;
    }

    [Fact]
    public void V1b_SerialChainStageOneMatchesChenOnFullGrid()
    {
        var grid = ChenSingleStageCheck.Grid;
        Assert.Contains(grid, g => g.LeadTime == 1);
        Assert.Contains(grid, g => g.LeadTime == 4);
        Assert.Contains(grid, g => g.Rho == 0.5);
        Assert.Contains(grid, g => g.Rho == -0.5);
        double z = GateA.BonferroniZ(grid.Count);
        int failures = 0;
        foreach (var (leadTime, window, rho) in grid)
        {
            var cfg = new ChainConfig { LeadTimes = new double[] { leadTime }, Window = window, InitialMean = GateA.Mu };
            var (mean, se) = GateA.BullwhipPerStage(cfg, GateA.Sigma, rho, GateA.Replications, GateA.Periods, GateA.Warmup);
            double reference = ChenFormulas.SingleStageBullwhipRatio(leadTime, window, rho);
            bool ok = GateA.Passes(reference, mean[0], se[0], z);
            failures += ok ? 0 : 1;
            _out.WriteLine(
                $"V1b L={leadTime} p={window} rho={rho,5:0.0}  analytic={reference,8:0.0000}  chain={mean[0],8:0.0000}  " +
                $"rel={mean[0] / reference - 1,8:+0.00%;-0.00%}  se={se[0],7:0.0000}  z={z:0.00}  {(ok ? "PASS" : "FAIL")}");
        }

        Assert.True(failures == 0, $"{failures} of {grid.Count} grid points failed V1b.");
    }
}
