using Bullwhip.Core.Chain;
using Bullwhip.Core.Costs;
using Bullwhip.Core.Demand;
using Bullwhip.Core.Rng;
using Bullwhip.Core.Validation;

namespace KSweep;

/// <summary>
/// Cost per week of the serial chain in the setup of DECISIONS C28, measured on a block of replications. The number of stages
/// comes from <see cref="CostParameters.Stages"/> (one stage for the single-stage diagnostic C33). Each replication draws
/// its demand and lead times from its own fixed streams, so two calls with the same replication range see the same random
/// numbers (common random numbers across R and z, which makes differences paired).
/// </summary>
public sealed class ChainCostEvaluator
{
    private readonly SweepSettings _settings;
    private readonly KaplanLeadTime[]? _stageLags;

    /// <param name="settings">Model and replication settings.</param>
    public ChainCostEvaluator(SweepSettings settings)
    {
        _ = Ar1Demand.InnovationSigma(settings.Sigma, settings.Rho); // C50/C52 range rule lives in Core

        if (settings.Pattern != DemandPattern.Stable && settings.Rho != 0)
        {
            throw new ArgumentException("Rho (C50) and a non-stable demand pattern (C51) cannot be combined; use only one.", nameof(settings));
        }

        _settings = settings;
        Lags = new KaplanLeadTime(settings.LagPmf);
        RequireWired(settings.Variant);
        if (settings.Variant.LagPmfPerStage is { } pmfs)
        {
            if (pmfs.Length != settings.Costs.Stages)
            {
                throw new ArgumentException($"LagPmfPerStage has {pmfs.Length} entries but the chain has {settings.Costs.Stages} stages.", nameof(settings));
            }

            _stageLags = pmfs.Select(p => new KaplanLeadTime(p)).ToArray();
        }
    }

    /// <summary>Lead-time distribution of every stage.</summary>
    public KaplanLeadTime Lags { get; }

    /// <summary>Lead-time distribution of each of <paramref name="stages"/> stages (the variant's lag pmf per stage, else <see cref="Lags"/> at every stage).</summary>
    public IReadOnlyList<KaplanLeadTime> StageLags(int stages)
    {
        return _stageLags ?? Enumerable.Repeat(Lags, stages).ToArray();
    }

    /// <summary>Cost per week of each replication in [first, first + count).</summary>
    public double[] CostsPerRep(CostParameters costs, int r, double[] z, int first, int count)
    {
        var totals = new double[count];
        for (int i = 0; i < count; i++)
        {
            totals[i] = Run(costs, r, z, first + i).TotalCostPerPeriod();
        }

        return totals;
    }

    /// <summary>Full result of replication <paramref name="rep"/> (its own demand and lead-time streams).</summary>
    public ChainResult Run(CostParameters costs, int r, double[] z, int rep)
    {
        return SerialChain.Run(ChainConfigFor(costs, r, z, rep), Demand(costs, rep));
    }

    /// <summary>Demand path of replication <paramref name="rep"/> (pattern and rho of the settings); shared by the evaluator and reports.</summary>
    public double[] Demand(CostParameters costs, int rep)
    {
        int length = _settings.Warmup + _settings.Periods;
        return _settings.Pattern == DemandPattern.Stable
            ? GateA.Demand(rep, length, costs.Mu, _settings.Rho, Ar1Demand.InnovationSigma(_settings.Sigma, _settings.Rho))
            : PatternDemand(costs, rep, length);
    }

    // C51: same replication seed as GateA.Demand (common random numbers across patterns).
    private double[] PatternDemand(CostParameters costs, int rep, int length)
    {
        var rng = new SplitMix64(SplitMix64.Derive(ChenSingleStageCheck.BaseSeed, (ulong)rep));
        var p = _settings.PatternParameters with { Mu = costs.Mu, Sigma = _settings.Sigma };
        return DemandPatterns.Generate(_settings.Pattern, p, length, rng);
    }

    private static void RequireWired(Variant v)
    {
        if (v.InfoSharing)
        {
            throw NotWired(nameof(Variant.InfoSharing), "IS-1");
        }

        if (v.InfoSharingSigmaFromIncomingOrders)
        {
            throw NotWired(nameof(Variant.InfoSharingSigmaFromIncomingOrders), "IS-3");
        }

        if (v.SigmaWindow != null)
        {
            throw NotWired(nameof(Variant.SigmaWindow), "MAW-1");
        }

        if (v.ForecastWindow != null)
        {
            throw NotWired(nameof(Variant.ForecastWindow), "MAW-1");
        }
    }

    private static NotSupportedException NotWired(string field, string story) =>
        new($"Variant.{field} is not wired into the chain yet (story {story}).");

    private ChainConfig ChainConfigFor(CostParameters costs, int r, double[] z, int rep)
    {
        int n = costs.Stages;
        if (_stageLags != null && _stageLags.Length != n)
        {
            throw new ArgumentException($"LagPmfPerStage has {_stageLags.Length} entries but the chain has {n} stages.", nameof(costs));
        }

        var config = new ChainConfig
        {
            LeadTimes = Enumerable.Repeat(Lags.MeanLeadTime, n).ToArray(),
            StochasticLeadTimes = Enumerable.Repeat(Lags, n).ToArray(),
            LeadTimeSeed = GateA.LeadTimeSeed(rep),
            R = r,
            Z = z,
            Holding = BaseCostModel.StorageRatePerUnitWeek(costs),
            Value = BaseCostModel.StageValues(costs),
            CapitalRate = BaseCostModel.CapitalRatePerWeek(costs),
            ShortageCost = BaseCostModel.ShortageCostPerStage(costs, costs.UpstreamShortageMultiplier),
            OrderCost = BaseCostModel.OrderCost(costs),
            AllowNegativeOrders = false,
            InitialMean = costs.Mu,
            Warmup = _settings.Warmup,
            Window = _settings.Window,
        };

        // LT-1: per-stage lag distributions replace the shared one (own mean lead time per stage).
        if (_stageLags != null)
        {
            config = config with
            {
                LeadTimes = _stageLags.Select(l => l.MeanLeadTime).ToArray(),
                StochasticLeadTimes = _stageLags.ToArray(),
            };
        }

        return ApplyVariant(config);
    }

    private ChainConfig ApplyVariant(ChainConfig config)
    {
        return config;
    }
}

/// <summary>
/// Coordinate search for the safety factor z of each stage on the training replications of <see cref="SweepSettings"/>: one
/// stage at a time, from the retailer upstream, every grid value is tried with the other stages held fixed, and the value with
/// the lowest mean training cost is kept (the first in grid order on ties).
/// </summary>
public sealed class SafetyFactorSearch
{
    private readonly ChainCostEvaluator _evaluator;
    private readonly SweepSettings _settings;

    public SafetyFactorSearch(ChainCostEvaluator evaluator, SweepSettings settings)
    {
        _evaluator = evaluator;
        _settings = settings;
    }

    /// <summary>Mean cost per week over the training replications.</summary>
    public double TrainingCost(CostParameters costs, int r, double[] z)
    {
        return _evaluator.CostsPerRep(costs, r, z, _settings.TrainFirst, _settings.TrainReps).Average();
    }

    /// <summary>Runs <paramref name="passes"/> passes over the stages, starting at <paramref name="start"/>.</summary>
    public double[] Optimize(CostParameters costs, int r, double[] start, int passes)
    {
        var z = (double[])start.Clone();
        for (int pass = 0; pass < passes; pass++)
        {
            for (int stage = 0; stage < costs.Stages; stage++)
            {
                z[stage] = BestFactor(costs, r, z, stage);
            }
        }

        return z;
    }

    /// <summary>The same start value for every stage.</summary>
    public static double[] Uniform(int stages, double level)
    {
        return Enumerable.Repeat(level, stages).ToArray();
    }

    private double BestFactor(CostParameters costs, int r, double[] z, int stage)
    {
        double best = z[stage];
        double bestCost = double.MaxValue;
        foreach (double candidate in _settings.Grid)
        {
            var trial = (double[])z.Clone();
            trial[stage] = candidate;
            double cost = TrainingCost(costs, r, trial);
            if (cost < bestCost)
            {
                bestCost = cost;
                best = candidate;
            }
        }

        return best;
    }
}

/// <summary>Confidence intervals and number formatting shared by the experiments.</summary>
public static class Report
{
    /// <summary>Mean and half width of the two-sided t confidence interval with quantile <paramref name="t"/>.</summary>
    public static (double Mean, double Half) Interval(double[] x, double t)
    {
        double mean = x.Average();
        double sd = Math.Sqrt(x.Sum(v => (v - mean) * (v - mean)) / (x.Length - 1));
        return (mean, t * sd / Math.Sqrt(x.Length));
    }

    /// <summary>Paired differences a_i − b_i (same replication i).</summary>
    public static double[] Differences(double[] a, double[] b)
    {
        return a.Zip(b, (x, y) => x - y).ToArray();
    }

    /// <summary>True if some stage sits on the lowest or highest grid value.</summary>
    public static bool AtGridEdge(double[] z, double[] grid)
    {
        return z.Any(x => x == grid[0] || x == grid[^1]);
    }

    public static string F(double x) => x.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

    public static string Vector(double[] z) => "[" + string.Join(", ", z.Select(F)) + "]";
}
