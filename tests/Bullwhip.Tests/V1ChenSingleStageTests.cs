using Bullwhip.Core.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Bullwhip.Tests;

/// <summary>V1: stage-1 bullwhip against Chen et al. (2000), Eq. (5). Full grid, plan §5 settings.</summary>
public class V1ChenSingleStageTests
{
    private readonly ITestOutputHelper _output;

    public V1ChenSingleStageTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void V1_SingleStageMatchesChenOnFullGrid()
    {
        var results = ChenSingleStageCheck.RunGrid();

        foreach (var r in results)
        {
            _output.WriteLine(
                $"L={r.LeadTime} p={r.Window} rho={r.Rho,5:0.0}  analytic={r.Analytic,8:0.0000}  sim={r.Simulated,8:0.0000}  " +
                $"rel={r.Simulated / r.Analytic - 1,8:+0.00%;-0.00%}  se={r.StandardError,7:0.0000}  z={r.BonferroniZ:0.00}  {(r.Pass ? "PASS" : "FAIL")}");
        }

        var failures = results.Where(r => !r.Pass).ToList();
        Assert.True(failures.Count == 0, $"{failures.Count} of {results.Count} grid points failed V1.");
    }

    [Fact]
    public void V11_SameSeedGivesIdenticalResult()
    {
        var first = ChenSingleStageCheck.RunCase(2, 8, 0.5, replications: 10, periods: 500);
        var second = ChenSingleStageCheck.RunCase(2, 8, 0.5, replications: 10, periods: 500);
        Assert.Equal(first, second);
    }
}
