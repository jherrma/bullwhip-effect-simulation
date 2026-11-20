using Bullwhip.Core.Chain;
using Bullwhip.Core.Costs;

namespace KSweep;

/// <summary>Registry of the countermeasure variants that the <c>countermeasure</c> mode can run.</summary>
public static class Countermeasures
{
    private static readonly string[] KnownIds = { "base", "lt", "lt-dose", "is-f", "is-m", "is", "is-lt", "is-lt-parts", "maw", "bundle" };

    /// <summary>The variants compared with the base for the countermeasure <paramref name="id"/>.</summary>
    public static IReadOnlyList<Variant> For(string id)
    {
        return id switch
        {
            "base" => new[] { new Variant() },
            "lt" => new[] { LeadTime(1) },
            "lt-dose" => new[] { 0.10, 0.20, 0.30, 1.00 }.Select(LeadTime).ToArray(),
            "is-f" => new[] { IsF },
            "is-m" => new[] { IsM },
            "is" => new[] { IsF, IsM },
            "is-lt" => new[] { IsLt },
            "is-lt-parts" => new[] { IsF, LeadTime(1), IsLt },
            "maw" => new[] { 12, 16, 24, 32 }.Select(MovingAverage).ToArray(),
            "bundle" => new[] { Bundle(32) },
            _ => throw new ArgumentException($"Unknown countermeasure id '{id}'. Known ids: {string.Join(", ", KnownIds)}.", nameof(id)),
        };
    }

    /// <summary>
    /// Lag pmf of one link at the dose w of countermeasure 02 (<see cref="BaseLeadTime.LagPmf"/>): every hazard of the
    /// transit time T = L − 1 from week 1 on rises to h + w·(1 − h). w = 0 is the base case (L in 2..5, E[L] = 2.43),
    /// w = 1 the deterministic lead time L = 2.
    /// </summary>
    public static double[] LagPmfForW(double w)
    {
        if (!(w >= 0 && w <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(w), w, "w must be in [0, 1].");
        }

        return BaseLeadTime.LagPmf(w);
    }

    private static readonly Variant IsF = new() { Name = "is-f", InfoSharing = true };

    private static readonly Variant IsM = new() { Name = "is-m", InfoSharing = true, InfoSharingSigmaFromIncomingOrders = true };

    private static readonly Variant IsLt = new()
    {
        Name = "is-f + lt w=1",
        InfoSharing = true,
        LagPmfPerStage = Enumerable.Repeat(0, 4).Select(_ => LagPmfForW(1)).ToArray(),
    };

    /// <summary>
    /// The recommended bundle: information sharing (01, IS-F) with the lead-time dose w = 1 (02) and the forecast window p
    /// (04b). The sigma-hat window stays at 8.
    /// </summary>
    private static Variant Bundle(int p) => new()
    {
        Name = $"is-f + lt w=1 + ma p={p}",
        InfoSharing = true,
        LagPmfPerStage = Enumerable.Repeat(0, 4).Select(_ => LagPmfForW(1)).ToArray(),
        ForecastWindow = p,
        SigmaWindow = 8,
    };

    /// <summary>
    /// Countermeasure 04b: the forecast window grows to <paramref name="p"/>. The sigma-hat window is held at 8, the base
    /// value, so the variant differs from the base case in the forecast window only.
    /// </summary>
    private static Variant MovingAverage(int p) => new() { Name = $"ma p={p}", ForecastWindow = p, SigmaWindow = 8 };

    /// <summary>
    /// Countermeasure 02: every link gets the dose <paramref name="w"/> (<see cref="LagPmfForW"/>). Above w = 0 the lead
    /// time is shorter on average and less variable; at w = 1 it is deterministic (L = 2).
    /// </summary>
    private static Variant LeadTime(double w) => new()
    {
        Name = $"lt w={w.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}",
        LagPmfPerStage = Enumerable.Repeat(0, 4).Select(_ => LagPmfForW(w)).ToArray(),
    };
}

/// <summary>
/// Compares the base case with countermeasure variants under the C44 protocol: R = 4, z from the coordinate search on the
/// training replications, measurement on the held-out replications. Base and variant run on the same replications, so the
/// differences are paired.
/// </summary>
public sealed class CountermeasureRunner
{
    private const int R = 4;
    private readonly SweepSettings _base;
    private readonly IReadOnlyList<Variant> _variants;
    private readonly CostParameters _costs;

    public CountermeasureRunner(SweepSettings baseSettings, IReadOnlyList<Variant> variants)
    {
        _base = baseSettings;
        _variants = variants;
        _costs = baseSettings.Costs;
    }

    /// <summary>Runs the base and every variant and reports each line through <paramref name="say"/>.</summary>
    public void Run(Action<string> say)
    {
        var baseSettings = _base with { Variant = new Variant() };
        var settingsPerVariant = _variants.Select(v => _base with { Variant = v }).ToArray();
        var baseEvaluator = new ChainCostEvaluator(baseSettings);
        var evaluators = settingsPerVariant.Select(s => new ChainCostEvaluator(s)).ToArray();
        say("Countermeasure comparison (C44 protocol)");
        say($"R = {R}, K scale {_costs.OrderCostScale:F4}, beta = {_costs.UpstreamShortageMultiplier}, shortage/holding ratio {_costs.ShortageToHoldingRatio}");
        say($"Training replications {_base.TrainFirst}..{_base.TrainFirst + _base.TrainReps - 1}, held-out {_base.TestFirst}..{_base.TestFirst + _base.TestReps - 1}, 95 % CI with t = {_base.T975}");
        say("");
        var zBase = Optimize(baseEvaluator, baseSettings);
        var mBase = Collect(baseEvaluator, zBase);
        Block("base", baseEvaluator, zBase, mBase, say);
        var samples = new List<KpiSample>();
        for (int i = 0; i < _variants.Count; i++)
        {
            samples.Add(RunVariant(_variants[i], settingsPerVariant[i], evaluators[i], zBase, mBase, say));
        }

        if (IsInteractionSet(_variants))
        {
            Interaction(samples[0], samples[1], samples[2], mBase, say);
        }
    }

    /// <summary>True for exactly [is-f, lt (lag pmf, no IS), is-f + lt]: the entry "is-lt-parts".</summary>
    private static bool IsInteractionSet(IReadOnlyList<Variant> v)
    {
        return v.Count == 3
            && v[0].InfoSharing && v[0].LagPmfPerStage is null
            && !v[1].InfoSharing && v[1].LagPmfPerStage is not null
            && v[2].InfoSharing && v[2].LagPmfPerStage is not null;
    }

    private KpiSample RunVariant(Variant v, SweepSettings s, ChainCostEvaluator ev, double[] zBase, KpiSample mBase, Action<string> say)
    {
        var z = Optimize(ev, s);
        var m = Collect(ev, z);
        Block(v.Name, ev, z, m, say);
        Diff(m, mBase, say);
        var fixedZ = Collect(ev, zBase);
        Block(v.Name + " at base z", ev, zBase, fixedZ, say);
        Diff(fixedZ, mBase, say);
        return m;
    }

    /// <summary>Interaction per replication: (comb − base) − (is − base) − (lt − base), each at its own z; paired CI.</summary>
    private void Interaction(KpiSample isF, KpiSample lt, KpiSample comb, KpiSample baseSample, Action<string> say)
    {
        say("Interaction: (is-f + lt w=1) - is-f - lt w=1, relative to base (paired, same replication, each variant at its own z)");
        say($"    TOTAL cost/week: {InteractionCi(isF, lt, comb, baseSample, "total")}");
        for (int k = 0; k < _costs.Stages; k++)
        {
            say($"    stage {k + 1}: BR4 {InteractionCi(isF, lt, comb, baseSample, $"br4_{k}")}");
        }

        say("");
    }

    private string InteractionCi(KpiSample isF, KpiSample lt, KpiSample comb, KpiSample baseSample, string key)
    {
        var x = comb[key].Select((c, i) => (c - baseSample[key][i]) - (isF[key][i] - baseSample[key][i]) - (lt[key][i] - baseSample[key][i])).ToArray();
        return Ci(x);
    }

    private double[] Optimize(ChainCostEvaluator ev, SweepSettings s)
    {
        return new SafetyFactorSearch(ev, s).Optimize(_costs, R, SafetyFactorSearch.Uniform(_costs.Stages, 1.0), s.Passes);
    }

    private KpiSample Collect(ChainCostEvaluator ev, double[] z)
    {
        return KpiSample.Collect(ev, _costs, R, z, _base.TestFirst, _base.TestReps);
    }

    private string Ci(double[] x)
    {
        var (mean, half) = Report.Interval(x, _base.T975);
        return $"{mean,10:F4} ± {half:F4}";
    }

    private void Block(string name, ChainCostEvaluator ev, double[] z, KpiSample m, Action<string> say)
    {
        int n = _costs.Stages;
        string edge = Report.AtGridEdge(z, _base.Grid) ? "  [z at grid edge]" : "";
        say($"== {name}");
        say($"z per stage (1 = retailer): {Report.Vector(z)}{edge}");
        say($"cover periods (replication {_base.TestFirst}): [{string.Join(", ", Enumerable.Range(0, n).Select(k => m[$"cover_{k}"][0].ToString("F2")))}]");
        var lags = ev.StageLags(n);
        for (int k = 0; k < n; k++)
        {
            say($"  stage {k + 1}: E[L] = {Report.F(lags[k].MeanLeadTime)}, CV(L) = {(Math.Sqrt(lags[k].LagVariance) / lags[k].MeanLeadTime).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}, {Report.TransitCv(lags[k])}");
        }

        for (int k = 0; k < n; k++)
        {
            say($"  stage {k + 1}: BR4 {Ci(m[$"br4_{k}"])} | sigma-hat {Ci(m[$"sigma_{k}"])} | error rms {Ci(m[$"errrms_{k}"])} | backlog {Ci(m[$"backlog_{k}"])} | pipeline {Ci(m[$"pipe_{k}"])}");
        }

        say($"  sum:     holding {Ci(m.SumOver("hold", n))} | capital {Ci(m.SumOver("cap", n))} | shortage {Ci(m.SumOver("short", n))} | order {Ci(m.SumOver("order", n))}");
        say($"  pipeline capital per stage: [{string.Join(", ", Enumerable.Range(0, n).Select(k => Ci(m[$"pipecap_{k}"]).Trim()))}] | sum {Ci(m.SumOver("pipecap", n))}");
        say($"  TOTAL cost/week: {Ci(m["total"])}");
        say($"  ready rate: {Ci(m["ready"])}");
        say($"  immediate fill rate: {Ci(m["immfill"])}");
        say("");
    }

    private void Diff(KpiSample a, KpiSample b, Action<string> say)
    {
        int n = _costs.Stages;
        say("  difference to base (paired, same replication)");
        for (int k = 0; k < n; k++)
        {
            say($"    stage {k + 1}: BR4 {DiffCi(a, b, $"br4_{k}")} | sigma-hat {DiffCi(a, b, $"sigma_{k}")} | error rms {DiffCi(a, b, $"errrms_{k}")} | backlog {DiffCi(a, b, $"backlog_{k}")} | pipeline {DiffCi(a, b, $"pipe_{k}")}");
        }

        say($"    sum:     holding {Ci(Report.Differences(a.SumOver("hold", n), b.SumOver("hold", n)))} | capital {Ci(Report.Differences(a.SumOver("cap", n), b.SumOver("cap", n)))} | shortage {Ci(Report.Differences(a.SumOver("short", n), b.SumOver("short", n)))} | order {Ci(Report.Differences(a.SumOver("order", n), b.SumOver("order", n)))}");
        say($"    TOTAL cost/week: {DiffCi(a, b, "total")}");
        say($"    ready rate: {DiffCi(a, b, "ready")}");
        say($"    immediate fill rate: {DiffCi(a, b, "immfill")}");
        say("");
    }

    private string DiffCi(KpiSample a, KpiSample b, string key) => Ci(Report.Differences(a[key], b[key]));
}
