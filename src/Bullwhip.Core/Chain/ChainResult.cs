using Bullwhip.Core.Stats;

namespace Bullwhip.Core.Chain;

public sealed class ChainResult
{
    /// <summary>Orders q[k][t] placed by stage k (0 at non-order periods).</summary>
    public required double[][] Orders { get; init; }

    /// <summary>Incoming demand d[k][t] seen by stage k (customer demand for k = 0, else the order of stage k−1).</summary>
    public required double[][] Incoming { get; init; }

    /// <summary>Per-period shipment s[k][t] of stage k, and its on-hand / backlog at period end.</summary>
    public required double[][] Shipped { get; init; }
    public required double[][] OnHandPeriod { get; init; }
    public required double[][] BacklogPeriod { get; init; }

    /// <summary>Retailer shipments to customers per period.</summary>
    public double[] RetailerShipped => Shipped[0];

    /// <summary>Measurement window start.</summary>
    public required int Warmup { get; init; }

    /// <summary>Time-average over the measurement window.</summary>
    public required double[] MeanOnHand { get; init; }
    public required double[] MeanBacklog { get; init; }
    public required double[] MeanPipeline { get; init; }

    /// <summary>Retailer: share of periods with no backlog at period end (ready rate, α).</summary>
    public required double ReadyRate { get; init; }

    /// <summary>Retailer: shipped / demanded within the window (fill rate, β).</summary>
    public required double FillRate { get; init; }

    /// <summary>Window costs per stage (holding, capital, shortage, order fixed cost).</summary>
    public required double[] HoldingCost { get; init; }
    public required double[] CapitalCost { get; init; }
    public required double[] ShortageCost { get; init; }
    public required double[] OrderCost { get; init; }

    /// <summary>Number of periods in the measurement window (the run length minus the warm-up).</summary>
    public int MeasuredPeriods => Orders[0].Length - Warmup;

    /// <summary>
    /// Total cost per period over the measurement window: shortage cost plus holding, capital and fixed order cost of every
    /// stage, divided by <see cref="MeasuredPeriods"/>.
    /// </summary>
    public double TotalCostPerPeriod()
    {
        double total = HoldingCost.Sum() + CapitalCost.Sum() + ShortageCost.Sum() + OrderCost.Sum();
        return total / MeasuredPeriods;
    }

    /// <summary>Number of orders with positive quantity per stage (whole run).</summary>
    public required int[] OrderCount { get; init; }

    /// <summary>
    /// RMS of the realised protection-period errors settled in the window (0 without an estimated σ̂). With
    /// <see cref="SafetySource.ChenOnePeriodError"/>: RMS of the one-period errors e_t = D_t − D̂¹_t.
    /// </summary>
    public required double[] ProtectionErrorRms { get; init; }

    /// <summary>Mean of the same errors (0 without an estimated σ̂): the bias of the realised window against c·μ̂.</summary>
    public required double[] ProtectionErrorMean { get; init; }

    /// <summary>Cover c per stage: S = c·μ̂ + z·σ̂ (periods of mean demand; CoverRule).</summary>
    public required double[] CoverPeriods { get; init; }

    /// <summary>Mean σ̂ over the order times in the window (0 without an estimated σ̂); z·MeanSigmaHat is the mean safety term of S.</summary>
    public required double[] MeanSigmaHat { get; init; }

    /// <summary>Conservation bookkeeping over the whole run (V6).</summary>
    public required double[] OnHandEnd { get; init; }
    public required double[] BacklogEnd { get; init; }
    public required double[] ScheduledEnd { get; init; }
    public required double[] ArrivedTotal { get; init; }
    public required double[] ShippedTotal { get; init; }
    public required double[] IncomingTotal { get; init; }
    public required double[] ScheduledAdded { get; init; }
    public required double[] ScheduledInitial { get; init; }

    /// <summary>Bullwhip ratio of stage k: Var(q) over the window divided by the given demand variance.</summary>
    public double Bullwhip(int stage, double demandVariance)
    {
        return Descriptive.Variance(Orders[stage], Warmup, Orders[stage].Length) / demandVariance;
    }
}
