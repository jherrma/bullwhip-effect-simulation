using Bullwhip.Core.Chain;
using Bullwhip.Core.Costs;
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
    /// <param name="settings">Model and replication settings.</param>
    public ChainCostEvaluator(SweepSettings settings)
    {
        _settings = settings;
        Lags = new KaplanLeadTime(settings.LagPmf);
    }

    /// <summary>Lead-time distribution of every stage.</summary>
    public KaplanLeadTime Lags { get; }

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
        var demand = GateA.Demand(rep, _settings.Warmup + _settings.Periods, costs.Mu, 0, _settings.Sigma);
        return SerialChain.Run(ChainConfigFor(costs, r, z, rep), demand);
    }

    private ChainConfig ChainConfigFor(CostParameters costs, int r, double[] z, int rep)
    {
        int n = costs.Stages;
        return new ChainConfig
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
