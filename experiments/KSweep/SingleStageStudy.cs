using Bullwhip.Core.Chain;
using Bullwhip.Core.Costs;
using Bullwhip.Core.Stats;

namespace KSweep;

/// <summary>One lead-time arm of the single-stage diagnostic: a label and Kaplan's lag pmf P(T = i), T = L − 1.</summary>
public sealed record LeadTimeArm(string Name, double[] LagPmf);

/// <summary>Settings of the single-stage diagnostic (DECISIONS C33). The defaults are the pre-registered values.</summary>
public sealed record SingleStageStudySettings
{
    /// <summary>
    /// R = 1 only, trained on the replications 200–249 and measured on 4000–4199, z grid −4 to 8 in steps of 0.25. With one
    /// stage, one pass of the coordinate search is a full grid search. The lag pmf is replaced per arm.
    /// </summary>
    public SweepSettings Sweep { get; init; } = new()
    {
        TrainFirst = 200,
        TestFirst = 4000,
        Passes = 1,
        Intervals = new[] { 1 },
        Grid = Enumerable.Range(0, 49).Select(i => -4.0 + 0.25 * i).ToArray(),
    };

    /// <summary>One stage (the retailer, v = 30) without fixed order cost; the shortage ratio b/h₁ is set per run.</summary>
    public CostParameters Costs { get; init; } = new() { Stages = 1, OrderCostScale = 0 };

    /// <summary>Shortage-to-holding ratios b/h₁ (critical ratios 0.5, 0.667, 0.9).</summary>
    public double[] Ratios { get; init; } = { 1, 2, 9 };

    /// <summary>
    /// The pre-registered arms of C33: B1 a Kaplan lead time with L in {1, 2} (E[L] = 1.43, not the base-case distribution
    /// of <see cref="Bullwhip.Core.Chain.BaseLeadTime"/>); B2 and B3 deterministic controls (one-point lag pmf).
    /// </summary>
    public LeadTimeArm[] Arms { get; init; } =
    {
        new("B1 Kaplan, L in {1, 2} with P = {0.57, 0.43}", new[] { 0.57, 0.43 }),
        new("B2 deterministic L = 1", new[] { 1.0 }),
        new("B3 deterministic L = 2", new[] { 0.0, 1.0 }),
    };

    /// <summary>Start value of the search.</summary>
    public double Start { get; init; } = 1;

    /// <summary>Distance of the neighbours z* ± Step whose ready rates must bracket the critical ratio.</summary>
    public double Step { get; init; } = 0.25;

    /// <summary>Pre-registered tolerance of z* around P1 (deterministic arms).</summary>
    public double ToleranceP1 { get; init; } = 0.25;

    /// <summary>Pre-registered tolerance of z* around P2 (Kaplan arm).</summary>
    public double ToleranceP2 { get; init; } = 0.5;
}

/// <summary>Held-out averages of one single-stage configuration.</summary>
/// <param name="Cost">Mean cost per week.</param>
/// <param name="Half">Half width of its 95 % t interval.</param>
/// <param name="ReadyRate">Mean share of periods without backlog at the period end.</param>
/// <param name="SigmaHat">Mean σ̂ at the order times.</param>
/// <param name="ErrorRms">Mean RMS of the realised protection-period errors.</param>
/// <param name="NetStockSd">Mean per-replication standard deviation of the period-end net stock (on-hand − backlog).</param>
public sealed record SingleStageStats(double Cost, double Half, double ReadyRate, double SigmaHat, double ErrorRms, double NetStockSd);

/// <summary>
/// Single-stage diagnostic (DECISIONS C33): optimises z of one stage at R = 1 for each shortage ratio and lead-time arm and
/// compares it with the normal newsvendor quantile (P1) and with the quantile of the lead-time mixture (P2).
/// </summary>
/// <remarks>
/// Newsvendor: per period the stage pays h on the period-end on-hand stock and b on the backlog, so the cost-minimal target has
/// P(no backlog) = b/(b + h), the critical ratio CR. P1 = Φ⁻¹(CR) assumes the net-stock error has standard deviation σ̂.
/// P2: at R = 1 the period-end net stock is S_τ − D(τ, t] with t − τ distributed as L (SerialChain remarks), while σ̂
/// estimates only the demand error over the planning cover c = E[L], σ_e = σ·√(c + c²/p) (lead-time variance is not in the
/// error, ProtectionWindow). P2 solves Σ_l P(L = l)·Φ((Q − l·μ)/(σ·√(l + c²/p))) = CR and sets z = (Q − c·μ)/σ_e; the c²/p
/// term is the variance of c·μ̂. For a one-point L, P2 = P1.
/// </remarks>
public sealed class SingleStageStudyRunner
{
    private readonly SingleStageStudySettings _settings;

    public SingleStageStudyRunner(SingleStageStudySettings settings)
    {
        _settings = settings;
    }

    /// <summary>Runs the diagnostic and reports each line through <paramref name="say"/>.</summary>
    public void Run(Action<string> say)
    {
        var sweep = _settings.Sweep;
        var costs = _settings.Costs;
        say("Single-stage diagnostic (DECISIONS C33): one stage, R = 1, no fixed order cost, b = ratio * h1");
        say($"h1 = (s + i) * v1 / 52 = {BaseCostModel.HoldingRatePerUnitWeek(costs)[0]:F4} (storage {BaseCostModel.StorageRatePerUnitWeek(costs)[0]:F4} " +
            $"on on-hand, capital {BaseCostModel.CapitalRatePerWeek(costs) * BaseCostModel.StageValues(costs)[0]:F4} on on-hand and pipeline)");
        say($"Grid {Report.F(sweep.Grid[0])}..{Report.F(sweep.Grid[^1])} ({sweep.Grid.Length} values), full grid search; " +
            $"training replications {sweep.TrainFirst}..{sweep.TrainFirst + sweep.TrainReps - 1}, " +
            $"held-out replications {sweep.TestFirst}..{sweep.TestFirst + sweep.TestReps - 1}, t = {sweep.T975:F3}");
        say("P1 = Phi^-1(CR) (normal, sigma-hat = sigma_e); P2 = quantile of the lead-time mixture of the net stock, (Q - c*mu) / sigma_e(c)");
        say("");
        foreach (var arm in _settings.Arms)
        {
            RunArm(arm, say);
        }
    }

    private void RunArm(LeadTimeArm arm, Action<string> say)
    {
        var sweep = _settings.Sweep with { LagPmf = arm.LagPmf };
        var evaluator = new ChainCostEvaluator(sweep);
        var search = new SafetyFactorSearch(evaluator, sweep);
        double cover = evaluator.Lags.MeanLeadTime;
        say($"{arm.Name}: E[L] = {cover:F2}, sigma_e(c) = {SigmaE(cover):F2}");
        foreach (double ratio in _settings.Ratios)
        {
            var costs = _settings.Costs with { ShortageToHoldingRatio = ratio };
            double cr = ratio / (ratio + 1);
            double best = search.Optimize(costs, 1, new[] { _settings.Start }, sweep.Passes)[0];
            double p1 = Normal.Quantile(cr);
            double p2 = MixturePrediction(arm.LagPmf, cover, cr);
            var below = Holdout(evaluator, costs, best - _settings.Step);
            var at = Holdout(evaluator, costs, best);
            var above = Holdout(evaluator, costs, best + _settings.Step);
            bool bracket = below.ReadyRate <= cr && cr <= above.ReadyRate;
            string edge = Report.AtGridEdge(new[] { best }, sweep.Grid) ? "  [z at grid edge]" : "";
            say($"  ratio b/h = {ratio:F0} (CR = {cr:F4}): z* = {Report.F(best)}{edge}; P1 = {Report.F(p1)} (within {Report.F(_settings.ToleranceP1)}: " +
                $"{YesNo(Math.Abs(best - p1) <= _settings.ToleranceP1)}), P2 = {Report.F(p2)} (within {Report.F(_settings.ToleranceP2)}: " +
                $"{YesNo(Math.Abs(best - p2) <= _settings.ToleranceP2)})");
            say($"    held-out cost/week at z* = {at.Cost:F4} ± {at.Half:F4}; ready rate at z* - {Report.F(_settings.Step)} / z* / z* + {Report.F(_settings.Step)} = " +
                $"{below.ReadyRate:F4} / {at.ReadyRate:F4} / {above.ReadyRate:F4}, CR bracketed: {YesNo(bracket)}");
            say($"    at z*: mean sigma-hat = {at.SigmaHat:F2}, protection-error RMS = {at.ErrorRms:F2}, net-stock SD = {at.NetStockSd:F2} " +
                $"(net-stock SD / sigma-hat = {at.NetStockSd / at.SigmaHat:F2})");
        }

        say("");
    }

    /// <summary>Held-out averages at safety factor <paramref name="z"/>.</summary>
    private SingleStageStats Holdout(ChainCostEvaluator evaluator, CostParameters costs, double z)
    {
        var sweep = _settings.Sweep;
        var cost = new double[sweep.TestReps];
        double ready = 0, sigma = 0, rms = 0, sd = 0;
        for (int i = 0; i < sweep.TestReps; i++)
        {
            var result = evaluator.Run(costs, 1, new[] { z }, sweep.TestFirst + i);
            cost[i] = result.TotalCostPerPeriod();
            ready += result.ReadyRate;
            sigma += result.MeanSigmaHat[0];
            rms += result.ProtectionErrorRms[0];
            sd += NetStockSd(result);
        }

        int n = sweep.TestReps;
        var (mean, half) = Report.Interval(cost, sweep.T975);
        return new SingleStageStats(mean, half, ready / n, sigma / n, rms / n, sd / n);
    }

    /// <summary>Standard deviation of the period-end net stock of stage 0 over the measurement window.</summary>
    private static double NetStockSd(ChainResult result)
    {
        var onHand = result.OnHandPeriod[0];
        var netStock = onHand.Select((x, t) => x - result.BacklogPeriod[0][t]).ToArray();
        return Math.Sqrt(Descriptive.Variance(netStock, result.Warmup, netStock.Length));
    }

    /// <summary>σ_e(c) = σ·√(c + c²/p): RMS of the protection-period demand error over the planning cover c.</summary>
    private double SigmaE(double cover)
    {
        var sweep = _settings.Sweep;
        return sweep.Sigma * Math.Sqrt(cover + cover * cover / sweep.Window);
    }

    /// <summary>P2 (class remarks): z at the CR quantile of the normal mixture of the net-stock error over L.</summary>
    private double MixturePrediction(double[] lagPmf, double cover, double cr)
    {
        var sweep = _settings.Sweep;
        double mu = _settings.Costs.Mu;
        double sigma = sweep.Sigma;
        double Cdf(double q) => lagPmf.Select((w, lag) => w * Normal.Cdf((q - (lag + 1) * mu) / (sigma * Math.Sqrt(lag + 1 + cover * cover / sweep.Window)))).Sum();
        double lo = 0.0;
        double hi = 2.0 * lagPmf.Length * mu + 10.0 * sigma;
        for (int i = 0; i < 200; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Cdf(mid) < cr)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return (0.5 * (lo + hi) - cover * mu) / SigmaE(cover);
    }

    private static string YesNo(bool value) => value ? "yes" : "no";
}
