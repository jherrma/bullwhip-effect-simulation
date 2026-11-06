using Bullwhip.Core.Chain;
using Bullwhip.Core.Costs;
using Bullwhip.Core.Validation;

namespace KSweep;

/// <summary>
/// Base-case KPI report (DECISIONS C44): R = 4, z per stage from the coordinate search on the training replications,
/// measured on the held-out replications. The 4-week bullwhip ratio is computed per replication (variance of the
/// non-overlapping 4-week order sums over the variance of the 4-week demand sums of the same replication) and then averaged.
/// </summary>
public sealed class BaseKpiReportRunner
{
    private const int Interval = 4;
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
        var m = Measure(z);
        say("Base-case KPI report (DECISIONS C44)");
        say($"R = {R}, K scale {_costs.OrderCostScale:F4}, beta = {_costs.UpstreamShortageMultiplier}, shortage/holding ratio {_costs.ShortageToHoldingRatio}");
        say($"Training replications {_s.TrainFirst}..{_s.TrainFirst + _s.TrainReps - 1}, held-out {_s.TestFirst}..{_s.TestFirst + _s.TestReps - 1}, 95 % CI with t = {_s.T975}");
        say($"z per stage (1 = retailer): {Report.Vector(z)}{(Report.AtGridEdge(z, _s.Grid) ? "  [z at grid edge]" : "")}");
        say("");
        Print(m, n, say);
        Verdict(m, say);
    }

    private Dictionary<string, double[]> Measure(double[] z)
    {
        var m = new Dictionary<string, double[]>();
        for (int i = 0; i < _s.TestReps; i++)
        {
            int rep = _s.TestFirst + i;
            var res = _evaluator.Run(_costs, R, z, rep);
            var demand = _evaluator.Demand(_costs, rep);
            Add(m, i, res, Window(demand, res.Warmup));
        }

        return m;
    }

    private void Add(Dictionary<string, double[]> m, int i, ChainResult res, double[] demand)
    {
        double[] dBuckets = Buckets(demand);
        double dVar = Variance(dBuckets);
        double pVar = Variance(demand);
        double per = res.MeasuredPeriods;
        double total = 0;
        for (int k = 0; k < _costs.Stages; k++)
        {
            Put(m, $"br4_{k}", i, Variance(Buckets(Window(res.Orders[k], res.Warmup))) / dVar);
            Put(m, $"brp_{k}", i, res.Bullwhip(k, pVar));
            Put(m, $"hold_{k}", i, res.HoldingCost[k] / per);
            Put(m, $"cap_{k}", i, res.CapitalCost[k] / per);
            Put(m, $"short_{k}", i, res.ShortageCost[k] / per);
            Put(m, $"order_{k}", i, res.OrderCost[k] / per);
            Put(m, $"onhand_{k}", i, res.MeanOnHand[k]);
            Put(m, $"backlog_{k}", i, res.MeanBacklog[k]);
            Put(m, $"pipe_{k}", i, res.MeanPipeline[k]);
            Put(m, $"sigma_{k}", i, res.MeanSigmaHat[k]);
            total += res.HoldingCost[k] + res.CapitalCost[k] + res.ShortageCost[k] + res.OrderCost[k];
        }

        Put(m, "total", i, res.TotalCostPerPeriod());
        Put(m, "ready", i, res.ReadyRate);
        Put(m, "fill", i, res.FillRate);
        Put(m, "immfill", i, res.ImmediateFillRate);
    }

    private void Put(Dictionary<string, double[]> m, string key, int i, double value)
    {
        if (!m.TryGetValue(key, out var arr))
        {
            arr = new double[_s.TestReps];
            m[key] = arr;
        }

        arr[i] = value;
    }

    private static double[] Window(double[] x, int warmup) => x[warmup..];

    private static double[] Buckets(double[] x)
    {
        int count = x.Length / Interval;
        return Enumerable.Range(0, count).Select(b => x.Skip(b * Interval).Take(Interval).Sum()).ToArray();
    }

    private static double Variance(double[] x)
    {
        double mean = x.Average();
        return x.Sum(v => (v - mean) * (v - mean)) / (x.Length - 1);
    }

    private string Ci(double[] x)
    {
        var (mean, half) = Report.Interval(x, _s.T975);
        return $"{mean,10:F4} ± {half:F4}";
    }

    private double[] Sum(Dictionary<string, double[]> m, string prefix, int n) =>
        Enumerable.Range(0, _s.TestReps).Select(i => Enumerable.Range(0, n).Sum(k => m[$"{prefix}_{k}"][i])).ToArray();

    private void Print(Dictionary<string, double[]> m, int n, Action<string> say)
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

        say($"    sum:     holding {Ci(Sum(m, "hold", n))} | capital {Ci(Sum(m, "cap", n))} | shortage {Ci(Sum(m, "short", n))} | order {Ci(Sum(m, "order", n))}");
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

        say($"    sum:     on-hand {Ci(Sum(m, "onhand", n))} | backlog {Ci(Sum(m, "backlog", n))} | pipeline {Ci(Sum(m, "pipe", n))}");
        say("");
        say("(e) Mean sigma-hat per stage (mean ± 95 % CI)");
        for (int k = 0; k < n; k++)
        {
            say($"    stage {k + 1}: sigma-hat {Ci(m[$"sigma_{k}"])}");
        }

        say("");
    }

    private void Verdict(Dictionary<string, double[]> m, Action<string> say)
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
