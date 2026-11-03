namespace Bullwhip.Core.Chain;

public enum SafetySource
{
    /// <summary>Rolling RMS of realised protection-period forecast errors on order-time buckets (plan §2, R1).</summary>
    EstimatedError,

    /// <summary>Known per-period σ: S = Lp·μ̂ + z·σ·√Lp (used by V8 and V13).</summary>
    KnownSigma,

    /// <summary>
    /// Chen et al. (2000) Eq. (3), p. 437: σ̂ = C·√(Σ_{i=1..p} e²_{t−i} / p), e_t = D_t − D̂¹_t the one-period error of
    /// the MA forecast. R = 1 only. The paper names C_{L,ρ} but gives no value (it refers to Ryan 1997, not in hand);
    /// C is <see cref="Analytic.ChenFormulas.SigmaConstantIid"/> (our derivation). Used for the V4 comparison.
    /// </summary>
    ChenOnePeriodError,
}

/// <summary>
/// Configuration of a serial chain. Index 0 is the retailer (downstream); the last index is the upstream-most stage,
/// which is supplied by an external source (plan S5). LeadTimes[k] is the model lead time L_k of stage k's replenishment.
/// </summary>
public sealed record ChainConfig
{
    /// <summary>
    /// Planning lead time L_k per stage; S covers Lp = L_k + R − 1 periods of mean demand. Deterministic arrivals
    /// (<see cref="StochasticLeadTimes"/> null): an integer, the physical lead time. Kaplan mode: E[L_k] of the lag
    /// distribution, which may be non-integer at R = 1 (the base case E[L] = 6.4); R &gt; 1 needs an integer (SerialChain).
    /// </summary>
    public required double[] LeadTimes { get; init; }

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

    /// <summary>
    /// If set (one entry per stage): Kaplan (1970) no-crossing stochastic lead time of each stage's replenishment
    /// (plan S10). Arrivals of shipments (or of the orders themselves for a perfect supplier and the top stage) follow
    /// <see cref="KaplanLeadTime"/>. LeadTimes[k] stays the planning lead time and must equal E[L_k] (planning on the
    /// known mean); at R = 1 it may be non-integer. Null (default): deterministic L_k, unchanged behaviour.
    /// </summary>
    public KaplanLeadTime[]? StochasticLeadTimes { get; init; }

    /// <summary>Seed of the lead-time streams: stage k draws from SplitMix64.Derive(LeadTimeSeed, k).</summary>
    public ulong LeadTimeSeed { get; init; }
}
