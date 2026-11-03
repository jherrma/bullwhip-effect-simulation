using Bullwhip.Core.Analytic;
using Bullwhip.Core.Chain;
using Bullwhip.Core.Demand;
using Bullwhip.Core.Rng;
using Bullwhip.Core.Stats;

namespace Bullwhip.Core.Validation;

public sealed record ChenCaseResult(
    int LeadTime,
    int Window,
    double Rho,
    double Analytic,
    double Simulated,
    double StandardError,
    double BonferroniZ,
    bool RelativeOk,
    bool InsideInterval)
{
    public bool Pass => RelativeOk && InsideInterval;
}

/// <summary>
/// V1 (plan §5): stage-1 bullwhip ratio under OUT + MA(p), z = 0, iid or AR(1) demand, against Chen's Eq. (5).
/// Pass: |BR_sim/BR_ana − 1| ≤ 2 % and BR_ana inside the Bonferroni-adjusted interval of the replication mean.
/// </summary>
public static class ChenSingleStageCheck
{
    public const int Replications = 100;
    public const int Periods = 2000;
    public const int Warmup = 200;
    public const double Mu = 100.0;
    public const double Sigma = 10.0;
    public const double Alpha = 0.01;
    public const double RelativeTolerance = 0.02;
    public const ulong BaseSeed = 20261103UL;

    public static IReadOnlyList<(int LeadTime, int Window, double Rho)> Grid { get; } = BuildGrid();

    public static IReadOnlyList<ChenCaseResult> RunGrid()
    {
        var results = new List<ChenCaseResult>(Grid.Count);
        foreach (var (leadTime, window, rho) in Grid)
        {
            results.Add(RunCase(leadTime, window, rho));
        }

        return results;
    }

    public static ChenCaseResult RunCase(
        int leadTime,
        int window,
        double rho,
        int replications = Replications,
        int periods = Periods,
        int warmup = Warmup)
    {
        double varianceDemand = Ar1Demand.StationaryVariance(Sigma, rho);
        double analytic = ChenFormulas.SingleStageBullwhipRatio(leadTime, window, rho);
        int length = warmup + periods;

        var ratios = new double[replications];
        for (int r = 0; r < replications; r++)
        {
            // Common random numbers: the same seed gives the same demand for every (L, p) at this ρ.
            var rng = new SplitMix64(SplitMix64.Derive(BaseSeed, (ulong)r));
            double[] demand = Ar1Demand.Generate(length, Mu, rho, Sigma, rng);
            double[] orders = SingleStageOrders.Compute(demand, leadTime, window);
            ratios[r] = SampleVariance(orders, warmup, length) / varianceDemand;
        }

        double mean = ratios.Average();
        double sd = Math.Sqrt(ratios.Sum(x => (x - mean) * (x - mean)) / (replications - 1));
        double standardError = sd / Math.Sqrt(replications);
        double bonferroniZ = Normal.Quantile(1.0 - Alpha / (2.0 * Grid.Count));

        bool relativeOk = Math.Abs(mean / analytic - 1.0) <= RelativeTolerance;
        bool insideInterval = Math.Abs(analytic - mean) <= bonferroniZ * standardError;

        return new ChenCaseResult(leadTime, window, rho, analytic, mean, standardError, bonferroniZ, relativeOk, insideInterval);
    }

    private static double SampleVariance(double[] values, int from, int to)
    {
        double sum = 0.0;
        for (int i = from; i < to; i++)
        {
            sum += values[i];
        }

        double mean = sum / (to - from);
        double ss = 0.0;
        for (int i = from; i < to; i++)
        {
            double d = values[i] - mean;
            ss += d * d;
        }

        return ss / (to - from);
    }

    private static List<(int, int, double)> BuildGrid()
    {
        var grid = new List<(int, int, double)>();
        foreach (int leadTime in new[] { 1, 2, 4 })
        {
            foreach (int window in new[] { 2, 4, 8, 16 })
            {
                foreach (double rho in new[] { 0.0, 0.5, -0.5 })
                {
                    grid.Add((leadTime, window, rho));
                }
            }
        }

        return grid;
    }
}
