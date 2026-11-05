using Bullwhip.Core.Costs;

namespace KSweep;

/// <summary>Settings of the upstream shortage threshold study (DECISIONS C31). The defaults are the pre-registered values.</summary>
public sealed record UpstreamStudySettings
{
    /// <summary>
    /// Base K (scale 1), trained on the fresh replications 200–249 and measured on the fresh replications 3000–3199. The z grid
    /// contains negative values: −4 to 8 in steps of 0.25 (49 values); this grid belongs to this study only.
    /// </summary>
    public SweepSettings Sweep { get; init; } = new()
    {
        TrainFirst = 200,
        TestFirst = 3000,
        Grid = Enumerable.Range(0, 49).Select(i => -4.0 + 0.25 * i).ToArray(),
    };

    /// <summary>Upstream shortage multipliers β in b_k = β·9·h_k (k ≥ 2).</summary>
    public double[] Multipliers { get; init; } = { 0, 0.25, 0.5, 1, 2 };

    /// <summary>Start value of every stage of the coordinate search.</summary>
    public double Start { get; init; } = 1;

    /// <summary>Order interval the others are compared with.</summary>
    public int ReferenceInterval { get; init; } = 4;

    /// <summary>First line of the report.</summary>
    public string Title { get; init; } = "Upstream shortage threshold study (DECISIONS C31) - z grid includes negative values";

    /// <summary>If true, each β block also compares every R with the cheapest R of that β (DECISIONS C32, question b).</summary>
    public bool CompareWithCheapest { get; init; }

    /// <summary>
    /// The pre-registered larger-β study (DECISIONS C32): β ∈ {1, 2, 5, 10, 20}, the C31 training replications, grid and search,
    /// and the fresh held-out replications 4000–4199.
    /// </summary>
    public static UpstreamStudySettings LargerBeta()
    {
        var c31 = new UpstreamStudySettings();
        return c31 with
        {
            Sweep = c31.Sweep with { TestFirst = 4000 },
            Multipliers = new double[] { 1, 2, 5, 10, 20 },
            Title = "Larger upstream shortage multiplier study (DECISIONS C32) - z grid includes negative values",
            CompareWithCheapest = true,
        };
    }
}

/// <summary>
/// Threshold study: for each upstream shortage multiplier β and order interval R, optimise z per stage on a grid that includes
/// negative values and report the held-out cost per week, with paired 95 % intervals of "R minus R = 4" (DECISIONS C31) and,
/// with <see cref="UpstreamStudySettings.CompareWithCheapest"/>, of "R minus the cheapest R" (DECISIONS C32).
/// </summary>
public sealed class UpstreamStudyRunner
{
    private readonly UpstreamStudySettings _settings;
    private readonly SweepSettings _sweep;

    public UpstreamStudyRunner(UpstreamStudySettings settings)
    {
        _settings = settings;
        _sweep = settings.Sweep;
    }

    /// <summary>Runs the study and reports each line through <paramref name="say"/>.</summary>
    public void Run(Action<string> say)
    {
        var costs = _sweep.Costs;
        say(_settings.Title);
        say($"K scale {costs.OrderCostScale:F2}; grid {Report.F(_sweep.Grid[0])}..{Report.F(_sweep.Grid[^1])} ({_sweep.Grid.Length} values); " +
            $"start z = {Report.F(_settings.Start)}, {_sweep.Passes} passes");
        say($"Training replications {_sweep.TrainFirst}..{_sweep.TrainFirst + _sweep.TrainReps - 1}, " +
            $"held-out replications {_sweep.TestFirst}..{_sweep.TestFirst + _sweep.TestReps - 1}, t = {_sweep.T975:F3}");
        say("");
        foreach (double beta in _settings.Multipliers)
        {
            RunMultiplier(costs, beta, say);
        }
    }

    private void RunMultiplier(CostParameters costs, double beta, Action<string> say)
    {
        var b = BaseCostModel.ShortageCostPerStage(costs, beta);
        say($"beta = {beta:F2}: b = [{string.Join(", ", b.Select(x => x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)))}]");
        var evaluator = new ChainCostEvaluator(_sweep, beta);
        var search = new SafetyFactorSearch(evaluator, _sweep);
        var intervals = _sweep.Intervals;
        var z = new double[intervals.Length][];
        var tests = new double[intervals.Length][];
        for (int i = 0; i < intervals.Length; i++)
        {
            var start = SafetyFactorSearch.Uniform(costs.Stages, _settings.Start);
            z[i] = search.Optimize(costs, intervals[i], start, _sweep.Passes);
            tests[i] = evaluator.CostsPerRep(costs, intervals[i], z[i], _sweep.TestFirst, _sweep.TestReps);
        }

        SayRows(beta, z, tests, say);
    }

    private void SayRows(double beta, double[][] z, double[][] tests, Action<string> say)
    {
        var intervals = _sweep.Intervals;
        int reference = Array.IndexOf(intervals, _settings.ReferenceInterval);
        var means = tests.Select(t => t.Average()).ToArray();
        for (int i = 0; i < intervals.Length; i++)
        {
            var (mean, half) = Report.Interval(tests[i], _sweep.T975);
            string diff = "reference";
            if (i != reference)
            {
                var (d, dh) = Report.Interval(Report.Differences(tests[i], tests[reference]), _sweep.T975);
                diff = $"R minus R={intervals[reference]}: {Report.F(d)} ± {Report.F(dh)} (paired 95 % CI [{Report.F(d - dh)}, {Report.F(d + dh)}])";
            }

            string edge = Report.AtGridEdge(z[i], _sweep.Grid) ? "  [z at grid edge]" : "";
            say($"  beta = {beta:F2}, R = {intervals[i]}: z = {Report.Vector(z[i])}, held-out cost/week = {Report.F(mean)} ± {Report.F(half)}; {diff}{edge}");
        }

        say($"  beta = {beta:F2}: lowest held-out cost R = {intervals[Array.IndexOf(means, means.Min())]}; " +
            $"R = {_settings.ReferenceInterval}: every z_k (k = 2..4) >= 0: {z[reference].Skip(1).All(x => x >= 0)}");
        if (_settings.CompareWithCheapest)
        {
            SayCheapestComparison(beta, tests, means, say);
        }

        say("");
    }

    /// <summary>Paired 95 % intervals of every other R minus the cheapest R, and whether all lie entirely above 0.</summary>
    private void SayCheapestComparison(double beta, double[][] tests, double[] means, Action<string> say)
    {
        var intervals = _sweep.Intervals;
        int cheapest = Array.IndexOf(means, means.Min());
        bool allAbove = true;
        for (int i = 0; i < intervals.Length; i++)
        {
            if (i == cheapest)
            {
                continue;
            }

            var (d, dh) = Report.Interval(Report.Differences(tests[i], tests[cheapest]), _sweep.T975);
            bool above = d - dh > 0;
            allAbove &= above;
            say($"    beta = {beta:F2}: R = {intervals[i]} minus cheapest R = {intervals[cheapest]}: {Report.F(d)} ± {Report.F(dh)} " +
                $"(paired 95 % CI [{Report.F(d - dh)}, {Report.F(d + dh)}], entirely above 0: {(above ? "yes" : "no")})");
        }

        say($"  beta = {beta:F2}: cheapest R = {intervals[cheapest]}; every other R minus it entirely above 0: {(allAbove ? "yes" : "no")}");
    }
}
