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

    /// <summary>
    /// Chen, Ryan &amp; Simchi-Levi (2000), NRL 47(4), p. 273, Eq. (5): σ̂ = K_L·Δ̂, Δ̂ the exponentially smoothed |e_t| of the
    /// one-period errors e_t = D_t − μ̂_{t−1} of the stage's own MA forecast (<see cref="Forecast.AbsErrorEwma"/>, smoothing
    /// constant <see cref="ChainConfig.AbsErrorSmoothing"/>), K_L = c·A, c = √(π/2) (paper), A = σ_eL/σ_e our derivation
    /// for iid demand (<see cref="Analytic.NrlFormulas"/>). R = 1 only. Used for the V4 comparison.
    /// </summary>
    EwmaAbsError,
}

/// <summary>
/// Cover c of the order-up-to level S = c·μ̂ + z·σ̂ at R &gt; 1 (periods of mean demand). Derivations and the upstream rule of
/// each option in <see cref="CoverRules"/>. At R = 1 every option equals the default (c = E[L]) and is ignored. The default
/// (<see cref="ChainConfig.Cover"/> null) is Integer for integer planning L and CycleAverage (B) for a non-integer one.
/// </summary>
public enum CoverRule
{
    /// <summary>
    /// Integer rule: c = L + R − 1 at the retailer and n·R upstream (<see cref="PhaseTarget"/>), from the planning L; at R &gt; 1
    /// the planning L must be an integer (set explicitly with a non-integer one, it throws). The default for integer L.
    /// </summary>
    Integer,

    /// <summary>(A) Worst phase: c = max over the phase of the mean exposure E[D(τ, t]]/μ. Point-mass L: L + R − 1 and n·R.</summary>
    WorstPhase,

    /// <summary>
    /// (B) Cycle average: c = E_L[integer cover(L)] (time-average net stock = z·σ̂ + L-averaged cycle stock). Point-mass L: as
    /// Integer. The default at R &gt; 1 when a planning L is non-integer.
    /// </summary>
    CycleAverage,

    /// <summary>(C) Lots in transit × R: c = R·Σ_j P(L &gt; jR) at every stage. Point-mass L: R·⌈L/R⌉ (differs from Integer).</summary>
    LotsInTransit,
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
    /// distribution, which may be non-integer (the base case E[L] = 2.43, <see cref="BaseLeadTime"/>); at R &gt; 1 a non-integer one uses a cover rule,
    /// by default CycleAverage (<see cref="Cover"/>).
    /// </summary>
    public required double[] LeadTimes { get; init; }

    /// <summary>Moving-average window p (must be a multiple of R).</summary>
    public int Window { get; init; } = 8;

    /// <summary>
    /// Number of order-time errors in the RMS σ̂. Null: the forecast window p. Only with <c>Safety = EstimatedError</c> and
    /// no <c>ConstantMean</c>; <c>Validate</c> rejects other combinations.
    /// The Chen and NRL constants keep p, because they depend on the forecast.
    /// </summary>
    public int? SigmaWindow { get; init; }

    /// <summary>Order interval R.</summary>
    public int R { get; init; } = 1;

    /// <summary>Order phase per stage (0..R−1). Default 0 for all stages.</summary>
    public int[]? Phases { get; init; }

    /// <summary>Safety factor z per stage. Default 0.</summary>
    public double[]? Z { get; init; }

    /// <summary>If true, every stage forecasts from POS demand (Chen's centralised case).</summary>
    public bool InfoSharing { get; init; }

    /// <summary>
    /// Only with <see cref="InfoSharing"/>: μ̂ stays from POS demand, but σ̂ comes from the realised errors of the stage's
    /// incoming demand over the decentralised window and cover (IS-M). False = σ̂ from POS errors (IS-F).
    /// Also requires <c>Safety = EstimatedError</c> and no <c>ConstantMean</c>; <c>Validate</c> rejects other combinations.
    /// </summary>
    public bool InfoSharingSigmaFromIncomingOrders { get; init; }

    /// <summary>If true, upstream stages always fill shipments in full and arrivals come after exactly L_k (plan §3).</summary>
    public bool PerfectSupplier { get; init; }

    /// <summary>If false, negative orders are clipped at 0 (experiment mode).</summary>
    public bool AllowNegativeOrders { get; init; } = true;

    public SafetySource Safety { get; init; } = SafetySource.EstimatedError;

    /// <summary>
    /// Smoothing constant α_s ∈ (0, 1] of Δ̂ for <see cref="SafetySource.EwmaAbsError"/> (required there, ignored otherwise).
    /// α_s = 2/(p + 1) gives the level Δ̂ the variance of a p-sample mean for iid inputs (α/(2 − α) = 1/p, our choice for V4).
    /// </summary>
    public double AbsErrorSmoothing { get; init; }

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

    /// <summary>
    /// Backlog cost b_k per unit-period at stage k, charged on the stage's backlog in the measurement window (one entry per
    /// stage). Null: zero at every stage. The base values are derived in <see cref="Costs.BaseCostModel.ShortageCostPerStage"/>
    /// (retailer b = 26·h₁, upstream stages b_k = β·26·h_k; β = 1 is the base case).
    /// </summary>
    public double[]? ShortageCost { get; init; }

    /// <summary>
    /// Counter escalation κ ≥ 0 of the backlog cost (sensitivity, DECISIONS C34). With n_{k,t} the number of consecutive
    /// periods up to t with a backlog at stage k, the shortage cost of stage k in period t is
    /// <see cref="ShortageCost"/>_k · backlog_{k,t} · (1 + κ · n_{k,t}). Zero (default) is the plain per-unit-period cost.
    /// </summary>
    public double ShortageEscalation { get; init; }

    /// <summary>
    /// Fixed cost K_k per order placed by stage k, charged in the measurement window (one entry per stage). Null: zero.
    /// The base values are derived in <see cref="Costs.BaseCostModel.OrderCost"/>.
    /// </summary>
    public double[]? OrderCost { get; init; }

    /// <summary>
    /// If set (one entry per stage): Kaplan (1970) no-crossing stochastic lead time of each stage's replenishment
    /// (plan S10). Arrivals of shipments (or of the orders themselves for a perfect supplier and the top stage) follow
    /// <see cref="KaplanLeadTime"/>. LeadTimes[k] stays the planning lead time and must equal E[L_k] (planning on the
    /// known mean); it may be non-integer (<see cref="Cover"/> at R &gt; 1). Null (default): deterministic L_k.
    /// </summary>
    public KaplanLeadTime[]? StochasticLeadTimes { get; init; }

    /// <summary>Seed of the lead-time streams: stage k draws from SplitMix64.Derive(LeadTimeSeed, k).</summary>
    public ulong LeadTimeSeed { get; init; }

    /// <summary>
    /// Cover rule at R &gt; 1 (<see cref="CoverRules"/>). Default null: <see cref="CoverRule.Integer"/> when every planning L is
    /// an integer, <see cref="CoverRule.CycleAverage"/> (B) for the whole chain when one is non-integer
    /// (Kaplan mean, e.g. E[L] = 2.43). The options A, B, C compute c from the lag pmf (Kaplan mode) or a point mass at the
    /// integer L (deterministic arrivals) and allow a non-integer planning L at R &gt; 1; Integer set explicitly does not.
    /// Ignored at R = 1.
    /// </summary>
    public CoverRule? Cover { get; init; }
}
