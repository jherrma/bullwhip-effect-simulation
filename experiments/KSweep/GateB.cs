using Bullwhip.Core.Costs;

namespace KSweep;

/// <summary>Settings of Gate B (DECISIONS A4, C30). The defaults are the pre-registered values.</summary>
public sealed record GateBSettings
{
    /// <summary>
    /// Model, cost parameters (base K), grid, t quantile and procedure (a): the C28 settings, trained on the fresh replications
    /// 200–249 and measured on the fresh replications 2000–2199.
    /// </summary>
    public SweepSettings Sweep { get; init; } = new() { TrainFirst = 200, TestFirst = 2000 };

    /// <summary>Start value of every stage in procedure (a), the C28 start.</summary>
    public double SingleStart { get; init; } = 1;

    /// <summary>Start values of procedure (b): one coordinate search per value, every stage starting there.</summary>
    public double[] MultiStarts { get; init; } = { 0, 1, 3 };

    /// <summary>Coordinate-search passes of procedure (b).</summary>
    public int MultiStartPasses { get; init; } = 3;

    /// <summary>Order interval that must be cheapest for the gate to pass (the C28 minimum at base K).</summary>
    public int ReferenceInterval { get; init; } = 4;
}

/// <summary>
/// Gate B: re-optimises z per order interval R with two search procedures on fresh training replications and tests on fresh
/// held-out replications whether R = 4 stays the cheapest. Pass rule (pre-registered in C30): under both procedures R = 4 has
/// the lowest mean held-out cost and every paired 95 % interval "R minus R = 4" lies entirely above 0.
/// </summary>
public sealed class GateBRunner
{
    private readonly GateBSettings _settings;
    private readonly SweepSettings _sweep;
    private readonly ChainCostEvaluator _evaluator;
    private readonly SafetyFactorSearch _search;

    public GateBRunner(GateBSettings settings)
    {
        _settings = settings;
        _sweep = settings.Sweep;
        _evaluator = new ChainCostEvaluator(_sweep);
        _search = new SafetyFactorSearch(_evaluator, _sweep);
    }

    /// <summary>Runs Gate B and reports each line through <paramref name="say"/>.</summary>
    public void Run(Action<string> say)
    {
        var costs = _sweep.Costs;
        SayHeader(costs, say);
        var intervals = _sweep.Intervals;
        var single = new double[intervals.Length][];
        var multi = new double[intervals.Length][];
        for (int i = 0; i < intervals.Length; i++)
        {
            single[i] = SingleStart(costs, intervals[i], say);
            multi[i] = MultiStart(costs, intervals[i], say);
        }

        var fixedZ = single[Array.IndexOf(intervals, _settings.ReferenceInterval)];
        say("");
        bool passSingle = Compare("Procedure (a), C28 search", costs, single, say);
        bool passMulti = Compare("Procedure (b), multi-start search", costs, multi, say);
        string context = $"Context only (not part of the rule): fixed z = {Report.Vector(fixedZ)} for every R";
        Compare(context, costs, intervals.Select(_ => fixedZ).ToArray(), say);
        say($"Rule under (a): {Verdict(passSingle)}");
        say($"Rule under (b): {Verdict(passMulti)}");
        say($"Gate B: {Verdict(passSingle && passMulti)}");
    }

    private void SayHeader(CostParameters costs, Action<string> say)
    {
        say("Gate B (DECISIONS A4, C30)");
        say($"K scale {costs.OrderCostScale:F2} (K = {string.Join(", ", BaseCostModel.OrderCost(costs).Select(Report.F))})");
        say($"Training replications {_sweep.TrainFirst}..{_sweep.TrainFirst + _sweep.TrainReps - 1}, " +
            $"held-out replications {_sweep.TestFirst}..{_sweep.TestFirst + _sweep.TestReps - 1}, t = {_sweep.T975:F3}");
        say($"(a) start z = {Report.F(_settings.SingleStart)}, {_sweep.Passes} passes; " +
            $"(b) starts z = {string.Join(" / ", _settings.MultiStarts.Select(Report.F))}, {_settings.MultiStartPasses} passes, " +
            $"keep the lowest training cost; grid {Report.F(_sweep.Grid[0])}..{Report.F(_sweep.Grid[^1])}");
        say("");
    }

    private double[] SingleStart(CostParameters costs, int r, Action<string> say)
    {
        var start = SafetyFactorSearch.Uniform(costs.Stages, _settings.SingleStart);
        var z = _search.Optimize(costs, r, start, _sweep.Passes);
        double training = _search.TrainingCost(costs, r, z);
        say($"R = {r} (a): z = {Report.Vector(z)}, training cost/week = {training:F4}{Edge(z)}");
        return z;
    }

    /// <summary>Procedure (b): one search per start value; keeps the lowest training cost (the earlier start on ties).</summary>
    private double[] MultiStart(CostParameters costs, int r, Action<string> say)
    {
        double[] best = Array.Empty<double>();
        double bestCost = double.MaxValue;
        double bestStart = double.NaN;
        foreach (double level in _settings.MultiStarts)
        {
            var z = _search.Optimize(costs, r, SafetyFactorSearch.Uniform(costs.Stages, level), _settings.MultiStartPasses);
            double training = _search.TrainingCost(costs, r, z);
            say($"R = {r} (b) start {Report.F(level)}: z = {Report.Vector(z)}, training cost/week = {training:F4}");
            if (training < bestCost)
            {
                best = z;
                bestCost = training;
                bestStart = level;
            }
        }

        say($"R = {r} (b) kept start {Report.F(bestStart)}: z = {Report.Vector(best)}{Edge(best)}");
        return best;
    }

    /// <summary>Held-out costs per R and the paired differences to the reference R; true if the pre-registered rule holds.</summary>
    private bool Compare(string title, CostParameters costs, double[][] z, Action<string> say)
    {
        var intervals = _sweep.Intervals;
        say(title);
        var tests = new double[intervals.Length][];
        var means = new double[intervals.Length];
        for (int i = 0; i < intervals.Length; i++)
        {
            tests[i] = _evaluator.CostsPerRep(costs, intervals[i], z[i], _sweep.TestFirst, _sweep.TestReps);
            var (mean, half) = Report.Interval(tests[i], _sweep.T975);
            means[i] = mean;
            say($"  R = {intervals[i]}: z = {Report.Vector(z[i])}, held-out cost/week = {Report.F(mean)} ± {Report.F(half)} (95 % CI)");
        }

        int reference = Array.IndexOf(intervals, _settings.ReferenceInterval);
        int best = Array.IndexOf(means, means.Min());
        say($"  lowest held-out cost: R = {intervals[best]}");
        bool allAbove = true;
        for (int i = 0; i < intervals.Length; i++)
        {
            if (i == reference)
            {
                continue;
            }

            var (diff, half) = Report.Interval(Report.Differences(tests[i], tests[reference]), _sweep.T975);
            bool above = diff - half > 0;
            allAbove &= above;
            say($"    R = {intervals[i]} minus R = {intervals[reference]}: {Report.F(diff)} ± {Report.F(half)} " +
                $"(paired 95 % CI [{Report.F(diff - half)}, {Report.F(diff + half)}], entirely above 0: {(above ? "yes" : "no")})");
        }

        say("");
        return best == reference && allAbove;
    }

    private string Edge(double[] z) => Report.AtGridEdge(z, _sweep.Grid) ? "  [z at grid edge]" : "";

    private static string Verdict(bool pass) => pass ? "PASS" : "FAIL";
}
