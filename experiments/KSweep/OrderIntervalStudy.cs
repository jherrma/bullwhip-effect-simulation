using Bullwhip.Core.Costs;

namespace KSweep;

/// <summary>Grid of the order-interval study (countermeasure 03): factors f that cut the fixed order cost K, and coordinated intervals R.</summary>
public sealed record OrderCostCutSettings
{
    /// <summary>Cut factors f applied to <see cref="CostParameters.OrderCostScale"/>. Must contain 1 (the base K).</summary>
    public double[] Factors { get; init; } = { 1, 0.5, 0.37, 0.25, 0.1 };

    /// <summary>Order intervals R, the same at every stage, phases 0. Must contain 4 (the base interval).</summary>
    public int[] Intervals { get; init; } = { 1, 2, 4 };
}

/// <summary>One cell of the grid: cut factor <paramref name="F"/>, interval <paramref name="R"/>, its own z and the held-out KPIs.</summary>
public sealed record OrderIntervalCell(double F, int R, double[] Z, KpiSample Kpi);

/// <summary>
/// Countermeasure 03, coordinated order intervals (RI-C): for every cut factor f and interval R the safety factors z are
/// searched on the training replications (C44) and the KPIs are measured on the held-out replications. K is cut through
/// <see cref="CostParameters.OrderCostScale"/>. The cell (f = 1, R = 4) is the base case.
/// </summary>
/// <remarks>
/// R*(f) is chosen on the same held-out replications that report the saving, as in the K sweep (C28, C49); this biases the
/// saving slightly in favour of the chosen R. The break-even f* follows from the affine cost law cost(f) = A + f·B at fixed
/// (R, z), which holds because K only prices orders and never feeds back into the dynamics.
/// </remarks>
public sealed class OrderIntervalRunner
{
    private const double InvariantTolerance = 1e-9;
    private readonly SweepSettings _s;
    private readonly OrderCostCutSettings _k;
    private readonly ChainCostEvaluator _evaluator;
    private readonly SafetyFactorSearch _search;
    private readonly double _scale;
    private readonly List<OrderIntervalCell> _cells = new();
    private Action<string> _say = _ => { };

    public OrderIntervalRunner(SweepSettings s, OrderCostCutSettings k)
    {
        if (!k.Factors.Contains(1.0))
        {
            throw new ArgumentException("Factors must contain 1 (the cell f = 1 is the base and the fixed-z view).", nameof(k));
        }

        if (!k.Intervals.Contains(4))
        {
            throw new ArgumentException("Intervals must contain 4 (the base interval).", nameof(k));
        }

        _s = s with { Variant = new Variant() };
        _k = k;
        _scale = s.Costs.OrderCostScale;
        _evaluator = new ChainCostEvaluator(_s);
        _search = new SafetyFactorSearch(_evaluator, _s);
    }

    /// <summary>Evaluates every cell (factors in the outer loop, intervals in the inner loop) and returns them.</summary>
    public IReadOnlyList<OrderIntervalCell> Grid()
    {
        Evaluate(null);
        return _cells.ToArray();
    }

    /// <summary>Evaluates every cell at the fixed safety factors <paramref name="z"/>, without a search (structural companion run, C59), and returns them.</summary>
    public IReadOnlyList<OrderIntervalCell> Grid(double[] z)
    {
        Evaluate(null, z);
        return _cells.ToArray();
    }

    /// <summary>Runs the study and reports each line through <paramref name="say"/>.</summary>
    public void Run(Action<string> say)
    {
        _say = say;
        say("Order-interval study, coordinated R (countermeasure 03, RI-C)");
        say($"K scale s0 = {_scale:F4}, beta = {_s.Costs.UpstreamShortageMultiplier}, shortage/holding ratio {_s.Costs.ShortageToHoldingRatio}");
        say($"Factors f = [{string.Join(", ", _k.Factors.Select(f => f.ToString("F2")))}], intervals R = [{string.Join(", ", _k.Intervals)}]");
        say($"Training replications {_s.TrainFirst}..{_s.TrainFirst + _s.TrainReps - 1}, held-out {_s.TestFirst}..{_s.TestFirst + _s.TestReps - 1}, 95 % CI with t = {_s.T975}");
        say("R*(f) is chosen on the held-out replications, which also report the saving; this favours the chosen R slightly (C28, C49).");
        say("");
        Evaluate(PrintCell);
        foreach (double f in _k.Factors)
        {
            PrintDecomposition(f);
        }

        PrintBreakEven();
        PrintFixedZBreakEven();
        PrintEoqAndSanity();
    }

    /// <summary>
    /// The interval R* in <see cref="OrderCostCutSettings.Intervals"/> with the lowest mean held-out total cost at factor
    /// <paramref name="f"/>; the first one on ties (the rule of <c>KSweepRunner.ComparePairs</c>).
    /// </summary>
    public int ChooseR(double f)
    {
        int best = _k.Intervals[0];
        double bestMean = double.MaxValue;
        foreach (int r in _k.Intervals)
        {
            double mean = CellOf(f, r).Kpi["total"].Average();
            if (mean < bestMean)
            {
                bestMean = mean;
                best = r;
            }
        }

        return best;
    }

    /// <summary>Splits the saving of (f, R*(f)) against the base into the K-price effect and the R effect, with paired CIs and the BR4 change.</summary>
    public void PrintDecomposition(double f)
    {
        int rStar = ChooseR(f);
        var baseCell = CellOf(1.0, 4);
        var atFour = CellOf(f, 4);
        var chosen = CellOf(f, rStar);
        var kPrice = Diff(atFour, baseCell, "total");
        var rEffect = Diff(chosen, atFour, "total");
        var total = Diff(chosen, baseCell, "total");
        CheckDecomposition(f, kPrice, rEffect, total);
        _say($"== f = {f:F2}: R*(f) = {rStar} (lowest mean held-out total cost)");
        _say($"  K-price effect (R = 4 at f vs base):  {Ci(kPrice)}");
        _say($"  R effect (R* vs R = 4 at f):          {Ci(rEffect)}");
        _say($"  total saving (R* at f vs base):       {Ci(total)}");
        _say($"  BR4 change per stage (R* at f minus base): [{string.Join(", ", Enumerable.Range(0, _s.Costs.Stages).Select(k => Ci(Diff(chosen, baseCell, $"br4_{k}")).Trim()))}]");
        _say("");
    }

    /// <summary>Break-even f of R against R = 4 for cost_R(f) = A_R + f·B_R, or null when B_R does not exceed B_4 (R has no extra order cost to save).</summary>
    public static double? BreakEven(double a4, double b4, double aR, double bR) =>
        bR > b4 ? (a4 - aR) / (bR - b4) : null;

    private void Evaluate(Action<OrderIntervalCell>? onCell, double[]? fixedZ = null)
    {
        _cells.Clear();
        foreach (double f in _k.Factors)
        {
            foreach (int r in _k.Intervals)
            {
                var cell = Cell(f, r, fixedZ);
                _cells.Add(cell);
                onCell?.Invoke(cell);
            }
        }
    }

    private CostParameters CostsFor(double f) => _s.Costs with { OrderCostScale = f * _scale };

    // fixedZ = null: z from the coordinate search on the training replications; otherwise fixedZ in every cell.
    private OrderIntervalCell Cell(double f, int r, double[]? fixedZ)
    {
        var costs = CostsFor(f);
        var z = fixedZ != null
            ? (double[])fixedZ.Clone()
            : _search.Optimize(costs, r, SafetyFactorSearch.Uniform(costs.Stages, 1.0), _s.Passes);
        var kpi = KpiSample.Collect(_evaluator, costs, r, z, _s.TestFirst, _s.TestReps);
        return new OrderIntervalCell(f, r, z, kpi);
    }

    private OrderIntervalCell CellOf(double f, int r) => _cells.Single(c => c.F == f && c.R == r);

    private double[] Diff(OrderIntervalCell a, OrderIntervalCell b, string key) => Report.Differences(a.Kpi[key], b.Kpi[key]);

    private string Ci(double[] x)
    {
        var (mean, half) = Report.Interval(x, _s.T975);
        return $"{mean,10:F4} ± {half:F4}";
    }

    private void PrintCell(OrderIntervalCell c)
    {
        int n = _s.Costs.Stages;
        var k = BaseCostModel.OrderCost(CostsFor(c.F));
        string edge = Report.AtGridEdge(c.Z, _s.Grid) ? "  [z at grid edge]" : "";
        var perWeek = Enumerable.Range(0, n).Select(i => Ci(c.Kpi[$"order_{i}"].Select(v => v / k[i]).ToArray()).Trim());
        _say($"== f = {c.F:F2}, K1(f) = {Report.F(k[0])} EUR, R = {c.R}");
        _say($"  z per stage (1 = retailer): {Report.Vector(c.Z)}{edge}");
        _say($"  TOTAL cost/week: {Ci(c.Kpi["total"])} | order cost/week (sum): {Ci(c.Kpi.SumOver("order", n))}");
        _say($"  orders/week per stage: [{string.Join(", ", perWeek)}]");
        _say($"  BR4 per stage: [{string.Join(", ", Enumerable.Range(0, n).Select(i => Ci(c.Kpi[$"br4_{i}"]).Trim()))}]");
        _say($"  ready rate: {Ci(c.Kpi["ready"])} | immediate fill rate: {Ci(c.Kpi["immfill"])}");
        _say("");
    }

    private static void CheckDecomposition(double f, double[] kPrice, double[] rEffect, double[] total)
    {
        for (int i = 0; i < total.Length; i++)
        {
            if (Math.Abs(kPrice[i] + rEffect[i] - total[i]) > InvariantTolerance)
            {
                throw new InvalidOperationException($"Decomposition invariant failed at f = {f}, replication {i}: K-price + R effect differs from the total saving.");
            }
        }
    }

    // A_i = total_i − O_i does not depend on K, B_i = O_i / f is the order cost per unit of f.
    private (double[] A, double[] B) Split(OrderIntervalCell c)
    {
        var total = c.Kpi["total"];
        var orders = c.Kpi.SumOver("order", _s.Costs.Stages);
        var a = new double[total.Length];
        var b = new double[total.Length];
        for (int i = 0; i < total.Length; i++)
        {
            a[i] = total[i] - orders[i];
            b[i] = orders[i] / c.F;
            if (Math.Abs(a[i] + c.F * b[i] - total[i]) > InvariantTolerance * total[i])
            {
                throw new InvalidOperationException($"Affine invariant A + f·B = total failed at f = {c.F}, R = {c.R}, replication {i}.");
            }
        }

        return (a, b);
    }

    private (double A, double B) Means(OrderIntervalCell c)
    {
        var (a, b) = Split(c);
        return (a.Average(), b.Average());
    }

    private double? FStar(double f, int r)
    {
        var (a4, b4) = Means(CellOf(f, 4));
        var (aR, bR) = Means(CellOf(f, r));
        return BreakEven(a4, b4, aR, bR);
    }

    private IEnumerable<int> ShorterIntervals() => _k.Intervals.Where(r => r < 4);

    private static string Describe(double? fStar, int r) => fStar switch
    {
        null => "n/a (R has no more orders than R = 4)",
        <= 0 => $"{fStar.Value:F4} (R = 4 cheaper at every f <= 1)",
        > 1 => $"{fStar.Value:F4} (R = {r} cheaper already at f = 1)",
        _ => $"{fStar.Value:F4}",
    };

    private void PrintBreakEven()
    {
        _say("Break-even cut f* (z re-optimised for each grid f; cost_R(f) = A_R + f·B_R per cell, EUR/week)");
        _say("  f | R | z | A_R | B_R | orders/week per stage | f*_R(f)");
        foreach (var c in _cells)
        {
            var (a, b) = Means(c);
            var k = BaseCostModel.OrderCost(CostsFor(c.F));
            var perWeek = Enumerable.Range(0, k.Length).Select(i => (c.Kpi[$"order_{i}"].Average() / k[i]).ToString("F3"));
            string fStar = c.R < 4 ? Describe(FStar(c.F, c.R), c.R) : "-";
            _say($"  {c.F:F2} | {c.R} | {Report.Vector(c.Z)} | {a:F4} | {b:F4} | [{string.Join(", ", perWeek)}] | {fStar}");
        }

        foreach (int r in ShorterIntervals())
        {
            PrintHeadline(r);
        }

        _say("");
    }

    // Headline: f*_R(f̂) at the grid point f̂ whose own break-even is closest to f̂ itself (D-8).
    private void PrintHeadline(int r)
    {
        var candidates = _k.Factors.Select(f => (F: f, Star: FStar(f, r))).Where(x => x.Star != null).ToArray();
        if (candidates.Length == 0)
        {
            _say($"headline f*_{r}: n/a (no grid factor with B_{r} > B_4)");
            return;
        }

        var hat = candidates.MinBy(x => Math.Abs(x.Star!.Value - x.F));
        double star = hat.Star!.Value;
        _say($"headline f*_{r} = f*_{r}(f̂ = {hat.F:F2}) = {Describe(star, r)}");
        PrintBracket(r, star);
    }

    // Uncertainty of f*: the paired total-cost difference (R minus R = 4, each at its own z for that f) at the two grid points around f*.
    private void PrintBracket(int r, double star)
    {
        var sorted = _k.Factors.OrderBy(f => f).ToArray();
        var around = new[] { sorted.Where(f => f <= star).DefaultIfEmpty(double.NaN).Max(), sorted.Where(f => f >= star).DefaultIfEmpty(double.NaN).Min() };
        foreach (double f in around.Where(f => !double.IsNaN(f)).Distinct())
        {
            _say($"  bracketing grid f = {f:F2}: R = {r} minus R = 4, paired total cost/week {Ci(Diff(CellOf(f, r), CellOf(f, 4), "total"))}");
        }

        if (star < sorted[0] || star > sorted[^1])
        {
            _say($"  f*_{r} lies outside the grid [{sorted[0]:F2}, {sorted[^1]:F2}]; only one bracketing grid point exists.");
        }
    }

    private void PrintFixedZBreakEven()
    {
        _say("Fixed-z view (secondary): z = z*(R, f = 1) for every f, from the cells (1, R)");
        var (a4, b4) = Means(CellOf(1.0, 4));
        foreach (int r in ShorterIntervals())
        {
            var (aR, bR) = Means(CellOf(1.0, r));
            var fStar = BreakEven(a4, b4, aR, bR);
            _say($"  fixed z = z*({r}, 1): f*_{r} = {Describe(fStar, r)}, A_{r} = {aR:F4}, B_{r} = {bR:F4}, A_4 = {a4:F4}, B_4 = {b4:F4}");
        }

        _say("");
    }

    private void PrintEoqAndSanity()
    {
        double rCal = _s.Costs.CalibrationInterval;
        var eoq = ShorterIntervals().OrderByDescending(r => r).Select(r => $"R = {r}: {r / rCal * (r / rCal) / _scale:F4}");
        _say($"EOQ comparison f_EOQ(R) = (R / R_cal)^2 / s0: {string.Join(", ", eoq)}");
        if (!_k.Intervals.Contains(2))
        {
            return;
        }

        var (_, b4) = Means(CellOf(1.0, 4));
        var (_, b2) = Means(CellOf(1.0, 2));
        double expected = (1.0 / 2 - 1.0 / 4) * BaseCostModel.OrderCost(CostsFor(1.0)).Sum();
        _say($"sanity: B_2 - B_4 = {b2 - b4:F2} EUR/week; if every order time orders: (1/2 - 1/4) · sum_k K_k(f = 1) = {expected:F2}");
    }
}
