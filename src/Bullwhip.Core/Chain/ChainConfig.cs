namespace Bullwhip.Core.Chain;

public enum SafetySource
{
    /// <summary>Rolling RMS of realised protection-period forecast errors on order-time buckets (plan §2, R1).</summary>
    EstimatedError,

    /// <summary>Known per-period σ: S = Lp·μ̂ + z·σ·√Lp (used by V8 and V13).</summary>
    KnownSigma,
}

/// <summary>
/// Configuration of a serial chain. Index 0 is the retailer (downstream); the last index is the upstream-most stage,
/// which is supplied by an external source (plan S5). LeadTimes[k] is the model lead time L_k of stage k's replenishment.
/// </summary>
public sealed record ChainConfig
{
    public required int[] LeadTimes { get; init; }

    /// <summary>Moving-average window p (must be a multiple of R).</summary>
    public int Window { get; init; } = 8;

    /// <summary>Order interval R.</summary>
    public int R { get; init; } = 1;

    /// <summary>Order phase per stage (0..R−1). Default 0 for all stages.</summary>
    public int[]? Phases { get; init; }

    /// <summary>Safety factor z per stage. Default 0.</summary>
    public double[]? Z { get; init; }

    /// <summary>If true, every stage forecasts from POS demand (Chen's centralised case).</summary>
    public bool InfoSharing { get; init; }

    /// <summary>If true, upstream stages always fill shipments in full and arrivals come after exactly L_k (plan §3).</summary>
    public bool PerfectSupplier { get; init; }

    /// <summary>If false, negative orders are clipped at 0 (experiment mode).</summary>
    public bool AllowNegativeOrders { get; init; } = true;

    public SafetySource Safety { get; init; } = SafetySource.EstimatedError;

    /// <summary>Per-period σ for <see cref="SafetySource.KnownSigma"/>.</summary>
    public double KnownSigma { get; init; }

    /// <summary>If set, every stage forecasts this mean and uses no safety stock (V9).</summary>
    public double? ConstantMean { get; init; }

    /// <summary>If positive: pre-fill forecasts and pipeline at this mean (plan S11). 0 starts empty.</summary>
    public double InitialMean { get; init; }

    /// <summary>First period included in the measurement window.</summary>
    public int Warmup { get; init; }

    /// <summary>Holding cost per unit-period, per stage.</summary>
    public double[]? Holding { get; init; }

    /// <summary>Item value per stage, for capital cost.</summary>
    public double[]? Value { get; init; }

    /// <summary>Capital cost rate per period (i/52 for a weekly model).</summary>
    public double CapitalRate { get; init; }

    /// <summary>Backlog cost per unit-period, at the retailer only (plan Q4 a).</summary>
    public double ShortageCost { get; init; }

    /// <summary>Fixed cost K per order placed.</summary>
    public double OrderCost { get; init; }
}
