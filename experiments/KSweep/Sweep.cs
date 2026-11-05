using Bullwhip.Core.Costs;

namespace KSweep;

/// <summary>Settings of the held-out K sweep (DECISIONS C28). The defaults are the values used for the thesis.</summary>
public sealed record SweepSettings
{
    /// <summary>Warm-up periods, not measured.</summary>
    public int Warmup { get; init; } = 200;

    /// <summary>Measured periods per replication.</summary>
    public int Periods { get; init; } = 2000;

    /// <summary>Standard deviation of the weekly demand (mean from the cost parameters).</summary>
    public double Sigma { get; init; } = 10;

    /// <summary>Forecast window p of the moving average.</summary>
    public int Window { get; init; } = 8;

    /// <summary>First training replication; the safety factors are optimised on TrainReps replications from here.</summary>
    public int TrainFirst { get; init; } = 0;

    /// <summary>Number of training replications.</summary>
    public int TrainReps { get; init; } = 50;

    /// <summary>First held-out replication; costs and intervals use TestReps fresh replications from here.</summary>
    public int TestFirst { get; init; } = 1000;

    /// <summary>Number of held-out replications.</summary>
    public int TestReps { get; init; } = 200;

    /// <summary>Two-sided 95 % Student t quantile for TestReps − 1 degrees of freedom (t(0.975, 199) = 1.972).</summary>
    public double T975 { get; init; } = 1.972;

    /// <summary>Coordinate-search passes over the stages.</summary>
    public int Passes { get; init; } = 2;

    /// <summary>Candidate safety factors: 0 to 8 in steps of 0.25.</summary>
    public double[] Grid { get; init; } = Enumerable.Range(0, 33).Select(i => 0.25 * i).ToArray();

    /// <summary>Scales of the fixed order cost K relative to the calibrated base values.</summary>
    public double[] Scales { get; init; } = { 0.25, 0.5, 1.0, 2.0, 4.0 };

    /// <summary>Order intervals R compared.</summary>
    public int[] Intervals { get; init; } = { 1, 2, 4, 8 };

    /// <summary>Lead-time lag distribution: lag 0 with probability 0.57, lag 1 with 0.43 (lead time 1 or 2 weeks).</summary>
    public double[] LagPmf { get; init; } = { 0.57, 0.43 };

    /// <summary>Base cost parameters (mean demand, rates, item values). <see cref="CostParameters.OrderCostScale"/> is set per run.</summary>
    public CostParameters Costs { get; init; } = new();
}

/// <summary>
/// The sweep: for each K scale and each order interval R, optimise the safety factor z per stage on the training replications,
/// then measure the cost per week on fresh replications. Costs are paired by replication (common random numbers).
/// </summary>
public sealed class KSweepRunner
{
    private readonly SweepSettings _settings;
    private readonly ChainCostEvaluator _evaluator;
    private readonly SafetyFactorSearch _search;

    public KSweepRunner(SweepSettings settings)
    {
        _settings = settings;
        _evaluator = new ChainCostEvaluator(settings);
        _search = new SafetyFactorSearch(_evaluator, settings);
    }

    /// <summary>Runs the full sweep and reports each line through <paramref name="say"/>.</summary>
    public void Run(Action<string> say)
    {
        var costs = _settings.Costs;
        var lags = _evaluator.Lags;
        say($"Value per stage: {string.Join(", ", BaseCostModel.StageValues(costs).Select(F))}");
        say($"Shortage cost b: {BaseCostModel.ShortageCost(costs):F4}; E[L] = {lags.MeanLeadTime:F2}; " +
            $"lag CV(L) = {Math.Sqrt(lags.LagVariance) / lags.MeanLeadTime:F3}");
        say("");
        foreach (double scale in _settings.Scales)
        {
            SweepScale(costs with { OrderCostScale = scale }, say);
        }
    }

    private void SweepScale(CostParameters costs, Action<string> say)
    {
        var k = BaseCostModel.OrderCost(costs);
        say($"K scale {costs.OrderCostScale:F2} (K = {string.Join(", ", k.Select(F))})");
        var means = new double[_settings.Intervals.Length];
        var tests = new double[_settings.Intervals.Length][];
        for (int i = 0; i < _settings.Intervals.Length; i++)
        {
            int r = _settings.Intervals[i];
            var z = _search.Optimize(costs, r, SafetyFactorSearch.Uniform(costs.Stages, 1.0), _settings.Passes);
            tests[i] = _evaluator.CostsPerRep(costs, r, z, _settings.TestFirst, _settings.TestReps);
            var (mean, half) = Report.Interval(tests[i], _settings.T975);
            means[i] = mean;
            string edge = Report.AtGridEdge(z, _settings.Grid) ? "  [z at grid edge]" : "";
            say($"  R = {r}: z = {Report.Vector(z)}, held-out cost/week = {F(mean)} ± {F(half)} (95 % CI){edge}");
        }

        ComparePairs(means, tests, say);
        say("");
    }

    private void ComparePairs(double[] means, double[][] tests, Action<string> say)
    {
        int best = Array.IndexOf(means, means.Min());
        say($"  lowest held-out cost: R = {_settings.Intervals[best]}");
        for (int i = 0; i < means.Length; i++)
        {
            if (i == best)
            {
                continue;
            }

            var (diff, half) = Report.Interval(Report.Differences(tests[i], tests[best]), _settings.T975);
            say($"    R = {_settings.Intervals[i]} minus R = {_settings.Intervals[best]}: {F(diff)} ± {F(half)} (paired 95 % CI)");
        }
    }

    private static string F(double x) => Report.F(x);
}
