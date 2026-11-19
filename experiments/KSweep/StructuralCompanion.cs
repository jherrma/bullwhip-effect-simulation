using System.Globalization;

namespace KSweep;

/// <summary>
/// Structural companion run of countermeasures 02 and 03 (DECISIONS C59, C67, C68): demand without noise (sigma = 0, so
/// every week is exactly mu) and z = 0 at every stage, so the safety terms vanish and no search runs. What remains is the
/// structural cost (cycle stock, pipeline, fixed order cost K, shortages from lead-time variation). Base and variants run on
/// the same held-out replications (same lead-time streams), so the differences are paired.
/// </summary>
/// <remarks>The bullwhip ratio is not reported: at sigma = 0 the demand variance is 0, so the ratio is not defined.</remarks>
public sealed class StructuralCompanionRunner
{
    private const int R = 4;
    private static readonly string[] KnownIds = { "lt", "lt-dose", "ri" };
    private readonly SweepSettings _s;
    private readonly double[] _zero;

    /// <param name="baseSettings">Base-case settings; Sigma is set to 0 here, everything else is kept.</param>
    public StructuralCompanionRunner(SweepSettings baseSettings)
    {
        _s = baseSettings with { Sigma = 0 };
        _zero = SafetyFactorSearch.Uniform(_s.Costs.Stages, 0.0);
    }

    /// <summary>Runs the companion of countermeasure <paramref name="id"/> ("lt", "lt-dose" or "ri") and reports each line through <paramref name="say"/>.</summary>
    public void Run(string id, Action<string> say)
    {
        switch (id)
        {
            case "ri":
                RunOrderIntervals(say);
                break;
            case "lt" or "lt-dose":
                RunVariants(Countermeasures.For(id), say);
                break;
            default:
                throw new ArgumentException($"Unknown structural companion id '{id}'. Known ids: {string.Join(", ", KnownIds)}.", nameof(id));
        }
    }

    // Countermeasure 02: the base and each variant of the registry at R = 4, each with the paired difference to the base.
    private void RunVariants(IReadOnlyList<Variant> variants, Action<string> say)
    {
        Header("countermeasure variants against the base", $"R = {R}", say);
        var baseEvaluator = new ChainCostEvaluator(_s with { Variant = new Variant() });
        var mBase = Collect(baseEvaluator);
        VariantBlock("base", baseEvaluator, mBase, say);
        foreach (var v in variants)
        {
            var ev = new ChainCostEvaluator(_s with { Variant = v });
            var m = Collect(ev);
            VariantBlock(v.Name, ev, m, say);
            say("  difference to base (paired, same replication)");
            CostLines("    ", key => Report.Differences(Values(m, key), Values(mBase, key)), say);
            say("");
        }
    }

    // Countermeasure 03: the cells (f, R) of the order-interval study at z = 0, the paired difference to R = 4 per f and the cheapest R per f.
    private void RunOrderIntervals(Action<string> say)
    {
        var grid = new OrderCostCutSettings();
        var runner = new OrderIntervalRunner(_s, grid);
        string factors = string.Join(", ", grid.Factors.Select(f => f.ToString("F2", CultureInfo.InvariantCulture)));
        Header("coordinated order intervals with K cut (countermeasure 03)", $"R = [{string.Join(", ", grid.Intervals)}], K scale f · s0 with f = [{factors}]", say);
        var cells = runner.Grid(_zero);
        foreach (var c in cells)
        {
            say($"== f = {c.F:F2}, R = {c.R}");
            CostLines("  ", key => Values(c.Kpi, key), say);
            say("");
        }

        foreach (double f in grid.Factors)
        {
            PairedToBaseInterval(cells, f, say);
        }

        var cheapest = grid.Factors.Select(f => $"f = {f:F2}: R = {runner.ChooseR(f)}");
        say($"cheapest R per f (lowest mean held-out total cost/week; first in R = [{string.Join(", ", grid.Intervals)}] on ties): {string.Join(", ", cheapest)}");
    }

    private void PairedToBaseInterval(IReadOnlyList<OrderIntervalCell> cells, double f, Action<string> say)
    {
        var atFour = cells.Single(c => c.F == f && c.R == R);
        say($"== f = {f:F2}: paired difference to R = {R} at the same f (total cost/week, same replication)");
        foreach (var c in cells.Where(c => c.F == f && c.R != R))
        {
            say($"  R = {c.R} minus R = {R}: {Ci(Report.Differences(c.Kpi["total"], atFour.Kpi["total"]))}");
        }

        say("");
    }

    private void Header(string title, string intervals, Action<string> say)
    {
        var c = _s.Costs;
        say($"Structural companion run (C59): {title}");
        say($"sigma = {_s.Sigma}: demand deterministic at mu = {c.Mu}; {intervals}; K scale s0 = {c.OrderCostScale:F4}, beta = {c.UpstreamShortageMultiplier}, shortage/holding ratio {c.ShortageToHoldingRatio}");
        say("z fixed at 0 (sigma = 0): no search");
        say("bullwhip ratio not defined at sigma = 0");
        say($"Held-out replications {_s.TestFirst}..{_s.TestFirst + _s.TestReps - 1}, the same for every configuration (paired), 95 % CI with t = {_s.T975}");
        say("");
    }

    private void VariantBlock(string name, ChainCostEvaluator ev, KpiSample m, Action<string> say)
    {
        int n = _s.Costs.Stages;
        say($"== {name}");
        say($"cover periods (replication {_s.TestFirst}): [{string.Join(", ", Enumerable.Range(0, n).Select(k => m[$"cover_{k}"][0].ToString("F2", CultureInfo.InvariantCulture)))}]");
        var lags = ev.StageLags(n);
        for (int k = 0; k < n; k++)
        {
            say($"  stage {k + 1}: E[L] = {Report.F(lags[k].MeanLeadTime)}, CV(L) = {(Math.Sqrt(lags[k].LagVariance) / lags[k].MeanLeadTime).ToString("F3", CultureInfo.InvariantCulture)}, {Report.TransitCv(lags[k])}");
        }

        CostLines("  ", key => Values(m, key), say);
        say("");
    }

    // One line with the stage sums of the four cost components, one with the total; values(key) gives one value per replication.
    private void CostLines(string indent, Func<string, double[]> values, Action<string> say)
    {
        say($"{indent}sum:     holding {Ci(values("hold"))} | capital {Ci(values("cap"))} | shortage {Ci(values("short"))} | order {Ci(values("order"))}");
        say($"{indent}TOTAL cost/week: {Ci(values("total"))}");
    }

    private double[] Values(KpiSample m, string key) => key == "total" ? m["total"] : m.SumOver(key, _s.Costs.Stages);

    private KpiSample Collect(ChainCostEvaluator ev) => KpiSample.Collect(ev, _s.Costs, R, _zero, _s.TestFirst, _s.TestReps);

    private string Ci(double[] x)
    {
        var (mean, half) = Report.Interval(x, _s.T975);
        return $"{mean,10:F4} ± {half:F4}";
    }
}
