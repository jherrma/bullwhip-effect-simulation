using Bullwhip.Core.Chain;
using Bullwhip.Core.Costs;

namespace KSweep;

/// <summary>
/// Per-replication KPIs of one configuration (R, z) on a block of replications. Each key maps to one value per replication,
/// so two samples taken on the same replication range can be compared pairwise (common random numbers).
/// </summary>
public sealed class KpiSample
{
    private const int Interval = 4;
    private readonly Dictionary<string, double[]> _values = new();
    private readonly int _count;

    private KpiSample(int count)
    {
        _count = count;
    }

    /// <summary>Values of <paramref name="key"/>, one per replication.</summary>
    public double[] this[string key] => _values[key];

    /// <summary>Runs replications [first, first + count) and collects the KPIs of each.</summary>
    public static KpiSample Collect(ChainCostEvaluator ev, CostParameters costs, int r, double[] z, int first, int count)
    {
        var sample = new KpiSample(count);
        for (int i = 0; i < count; i++)
        {
            int rep = first + i;
            var res = ev.Run(costs, r, z, rep);
            var demand = ev.Demand(costs, rep);
            sample.Add(costs, i, res, Window(demand, res.Warmup));
        }

        return sample;
    }

    /// <summary>Per replication, the sum over stages 0..<paramref name="stages"/> − 1 of the keys "prefix_k".</summary>
    public double[] SumOver(string prefix, int stages) =>
        Enumerable.Range(0, _count).Select(i => Enumerable.Range(0, stages).Sum(k => _values[$"{prefix}_{k}"][i])).ToArray();

    private void Add(CostParameters costs, int i, ChainResult res, double[] demand)
    {
        int stages = costs.Stages;
        double[] dBuckets = Buckets(demand);
        double dVar = Variance(dBuckets);
        double pVar = Variance(demand);
        double per = res.MeasuredPeriods;
        for (int k = 0; k < stages; k++)
        {
            Put($"br4_{k}", i, Variance(Buckets(Window(res.Orders[k], res.Warmup))) / dVar);
            Put($"brp_{k}", i, res.Bullwhip(k, pVar));
            Put($"hold_{k}", i, res.HoldingCost[k] / per);
            Put($"cap_{k}", i, res.CapitalCost[k] / per);
            Put($"short_{k}", i, res.ShortageCost[k] / per);
            Put($"order_{k}", i, res.OrderCost[k] / per);
            Put($"onhand_{k}", i, res.MeanOnHand[k]);
            Put($"backlog_{k}", i, res.MeanBacklog[k]);
            Put($"pipe_{k}", i, res.MeanPipeline[k]);
            Put($"pipecap_{k}", i, BaseCostModel.CapitalRatePerWeek(costs) * BaseCostModel.StageValues(costs)[k] * res.MeanPipeline[k]);
            Put($"sigma_{k}", i, res.MeanSigmaHat[k]);
            Put($"errrms_{k}", i, res.ProtectionErrorRms[k]);
            Put($"cover_{k}", i, res.CoverPeriods[k]);
        }

        Put("total", i, res.TotalCostPerPeriod());
        Put("ready", i, res.ReadyRate);
        Put("fill", i, res.FillRate);
        Put("immfill", i, res.ImmediateFillRate);
    }

    private void Put(string key, int i, double value)
    {
        if (!_values.TryGetValue(key, out var arr))
        {
            arr = new double[_count];
            _values[key] = arr;
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
}
