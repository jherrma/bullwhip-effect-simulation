using Bullwhip.Core.Costs;

namespace KSweep;

/// <summary>
/// Base-case KPI report (DECISIONS C44): R = 4, z per stage from the coordinate search on the training replications,
/// measured on the held-out replications. The 4-week bullwhip ratio is computed per replication (variance of the
/// non-overlapping 4-week order sums over the variance of the 4-week demand sums of the same replication) and then averaged.
/// </summary>
public sealed class BaseKpiReportRunner
{
    private const int R = 4;
    private readonly SweepSettings _s;
    private readonly ChainCostEvaluator _evaluator;
    private readonly CostParameters _costs;

    public BaseKpiReportRunner(SweepSettings settings)
    {
        _s = settings;
        _costs = settings.Costs;
        _evaluator = new ChainCostEvaluator(settings);
    }

    /// <summary>Runs search and measurement and reports each line through <paramref name="say"/>.</summary>
    public void Run(Action<string> say)
    {
        int n = _costs.Stages;
        var z = new SafetyFactorSearch(_evaluator, _s).Optimize(_costs, R, SafetyFactorSearch.Uniform(n, 1.0), _s.Passes);
        var m = KpiSample.Collect(_evaluator, _costs, R, z, _s.TestFirst, _s.TestReps);
        say("Base-case KPI report (DECISIONS C44)");
        say($"R = {R}, K scale {_costs.OrderCostScale:F4}, beta = {_costs.UpstreamShortageMultiplier}, shortage/holding ratio {_costs.ShortageToHoldingRatio}");
        say($"Training replications {_s.TrainFirst}..{_s.TrainFirst + _s.TrainReps - 1}, held-out {_s.TestFirst}..{_s.TestFirst + _s.TestReps - 1}, 95 % CI with t = {_s.T975}");
        say($"z per stage (1 = retailer): {Report.Vector(z)}{(Report.AtGridEdge(z, _s.Grid) ? "  [z at grid edge]" : "")}");
        say("");
        Print(m, n, say);
        Verdict(m, say);
    }

    private string Ci(double[] x)
    {
        var (mean, half) = Report.Interval(x, _s.T975);
        return $"{mean,10:F4} ± {half:F4}";
    }

    private void Print(KpiSample m, int n, Action<string> say)
    {
        say("(a) Bullwhip ratio, 4-week buckets: per replication Var(non-overlapping 4-week order sums) / Var(4-week customer-demand sums), then mean over replications");
        say("    (per-period ratio = ChainResult.Bullwhip with the empirical per-period demand variance; ARTEFACT-PRONE at R = 4, extra column only)");
        for (int k = 0; k < n; k++)
        {
            say($"    stage {k + 1}: BR(4-week) = {Ci(m[$"br4_{k}"])}    [artefact-prone per-period BR = {Ci(m[$"brp_{k}"])}]");
        }

        say("");
        say("(b) Costs per week (mean ± 95 % CI)");
        for (int k = 0; k < n; k++)
        {
            say($"    stage {k + 1}: holding {Ci(m[$"hold_{k}"])} | capital {Ci(m[$"cap_{k}"])} | shortage {Ci(m[$"short_{k}"])} | order {Ci(m[$"order_{k}"])}");
        }

        say($"    sum:     holding {Ci(m.SumOver("hold", n))} | capital {Ci(m.SumOver("cap", n))} | shortage {Ci(m.SumOver("short", n))} | order {Ci(m.SumOver("order", n))}");
        say($"    TOTAL cost/week: {Ci(m["total"])}");
        say("");
        say($"(c) Retailer ready rate: {Ci(m["ready"])}");
        say($"    immediate fill rate (sum min(D, V) / sum D, DECISIONS C46): {Ci(m["immfill"])}");
        say($"    fill rate (shipped/demanded incl. later shipments, not informative): {Ci(m["fill"])}");
        say("");
        say("(d) Mean on-hand / backlog / pipeline per stage (mean ± 95 % CI)");
        for (int k = 0; k < n; k++)
        {
            say($"    stage {k + 1}: on-hand {Ci(m[$"onhand_{k}"])} | backlog {Ci(m[$"backlog_{k}"])} | pipeline {Ci(m[$"pipe_{k}"])}");
        }

        say($"    sum:     on-hand {Ci(m.SumOver("onhand", n))} | backlog {Ci(m.SumOver("backlog", n))} | pipeline {Ci(m.SumOver("pipe", n))}");
        say("");
        say("(e) Mean sigma-hat per stage (mean ± 95 % CI)");
        for (int k = 0; k < n; k++)
        {
            say($"    stage {k + 1}: sigma-hat {Ci(m[$"sigma_{k}"])}");
        }

        say("");
    }

    private void Verdict(KpiSample m, Action<string> say)
    {
        int n = _costs.Stages;
        double ready = m["ready"].Average();
        double fill = m["fill"].Average();
        double imm = m["immfill"].Average();
        var br = Enumerable.Range(0, n).Select(k => m[$"br4_{k}"].Average()).ToArray();
        double total = m["total"].Average();
        bool p3 = Enumerable.Range(0, n - 1).All(k => br[k] < br[k + 1]);
        say("Verdict of the pre-registered predictions (DECISIONS C44)");
        say($"  P1: ready rate mean {ready:F4} in [0.93, 0.97]  -> {PassFail(ready >= 0.93 && ready <= 0.97)}");
        say($"  P2: fill rate mean {fill:F4} > ready rate mean {ready:F4} and > 0.98  -> {PassFail(fill > ready && fill > 0.98)}");
        say($"  P2' (DECISIONS C46): immediate fill rate mean {imm:F4} < 1.0 and > 0.98  -> {PassFail(imm < 1.0 && imm > 0.98)}");
        say($"  P3: BR(4-week) = {string.Join(" < ", br.Select(b => b.ToString("F4")))}  strictly increasing  -> {PassFail(p3)}");
        say($"  P4: total cost/week {total:F4} > 123.31 (difference {total - 123.31:F4})  -> {PassFail(total > 123.31)}");
    }

    private static string PassFail(bool ok) => ok ? "PASS" : "FAIL";
}
