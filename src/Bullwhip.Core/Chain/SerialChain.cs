using Bullwhip.Core.Analytic;
using Bullwhip.Core.Forecast;
using Bullwhip.Core.Rng;

namespace Bullwhip.Core.Chain;

/// <summary>
/// Period loop of the serial chain (plan §3). Per period t, for stages k = 0..N−1 (downstream first):
/// 1 receive arrivals; 2 read incoming demand; 3 ship (backlog, min(on-hand, backlog));
/// 4 observe demand for the forecast; 5 at an order time place q = S − IP.
/// </summary>
/// <remarks>
/// <para>
/// Cover. S = c·μ̂ + z·σ̂ covers c periods of mean demand: the protection period Lp = L + R − 1, or n·R for the lumpy
/// incoming demand of an upstream stage at R &gt; 1 (<see cref="PhaseTarget"/>). Deterministic arrivals need an integer L; a
/// non-integer planning L is a Kaplan mean E[L] (base case 6.4). At R = 1, c = E[L] is exact: the net stock at the end of t
/// is S_τ − D(τ, t] with τ the newest order already received, and t − τ has the lag distribution of L (Kaplan cuts are iid),
/// so E[NS] = (c − E[L])·μ + z·σ̂ in every period. At R &gt; 1 the random arrivals smooth the per-phase mean of t − τ (and of
/// the lumps in (τ, t]), so the worst-phase rule behind PhaseTarget, the cycle-average rule and the order-time rule differ
/// and the integer rule defines no cover for a real L. <see cref="ChainConfig.Cover"/> selects the rule; the options A, B, C
/// (<see cref="CoverRules"/>) take c from the lead-time pmf (Kaplan) or a point mass at the integer L.
/// </para>
/// <para>
/// Protection-period error (plan §2, R1). The order at order time t0 sees IP after d[t0]; it arrives at t0 + L and the next
/// one (placed at t0 + R) at t0 + R + L, so the net stock at the end of t0 + Lp is S − D(t0, t0 + Lp]. S must cover
/// e = D(t0, t0 + Lp] − c·μ̂(t0), so the realised side is the exact per-period sum over (t0, t0 + Lp]. Smooth iid demand
/// (σ², window p): D and μ̂ use disjoint periods, Var(e) = Lp·σ² + Lp²·σ²/p, which the RMS reproduces. A sum over R-buckets
/// with the partial bucket weighted w = min(R, Lp − jR)/R would keep the mean but put w²·R·σ² instead of w·R·σ² on that
/// bucket. With lumps the window holds n lumps, and c = n·R removes the offset (Lp − nR)·μ that would otherwise enter σ̂.
/// The window closes by the order time t0 + mR, m = ⌈Lp/R⌉, where the error is settled before σ̂ is read: one sample per
/// order time. A non-integer Lp (Kaplan mean) uses (t0, t0 + ⌈Lp⌉] with the boundary weights of
/// <see cref="ProtectionWindow"/>; a cover-rule option uses <see cref="CoverRules.Window"/>: the smooth cover of the option
/// for POS input (retailer, IS-F), the lumpy one otherwise. The error is taken on the forecast input, which is POS
/// demand under info sharing, or on the incoming demand when <see cref="ChainConfig.InfoSharingSigmaFromIncomingOrders"/> is set.
/// </para>
/// <para>
/// σ̂ (<see cref="SafetySource.EstimatedError"/>) is the RMS of the last p order-time errors (<see cref="ErrorWindow"/>; p
/// samples at every R, spanning p·R periods). Every sample is the error of an actual order, so E[σ̂²] = σ_e²; per-period
/// errors would not be, because upstream their window holds a phase-dependent lump count and consecutive ones share Lp − 1
/// periods and μ̂. The RMS itself has the Jensen bias of GateA.RmsMeanFactor. Chen et al. (2000) Eq. (3) and the NRL
/// estimator (Eq. 5, p. 273) use the one-period error e_t = D_t − μ̂ of order time t − 1 instead (Chen's order at the start
/// of t + 1 is the model's order at the end of t): σ̂ = C·RMS (<see cref="ChenFormulas.SigmaConstantIid"/>) or σ̂ = K_L·Δ̂
/// with Δ̂ the smoothed |e| (<see cref="AbsErrorEwma"/>, <see cref="NrlFormulas.KFactorIid"/>). KnownSigma uses σ·√Lp, with
/// the smooth cover of the option in place of Lp under a cover rule. Every error enters ProtectionErrorRms/Mean.
/// </para>
/// </remarks>
public static class SerialChain
{
    public static ChainResult Run(ChainConfig cfg, double[] demand)
    {
        Validate(cfg);
        var run = new ChainRun(cfg, demand);
        if (cfg.InitialMean > 0)
        {
            run.Prefill(cfg.InitialMean);
        }

        for (int t = 0; t < demand.Length; t++)
        {
            for (int k = 0; k < run.Stages; k++)
            {
                run.Step(t, k);
            }

            run.AgeBacklogs();
            if (t >= cfg.Warmup)
            {
                run.Measure(t);
            }
        }

        return run.Result();
    }

    /// <summary>Rejects a configuration the period loop cannot run (every failure is an <see cref="ArgumentException"/>).</summary>
    private static void Validate(ChainConfig cfg)
    {
        ValidateStagesAndOrdering(cfg);
        ValidateLeadTimes(cfg);
        ValidateInfoSharingSigma(cfg);
        if (!(cfg.ShortageEscalation >= 0.0))
        {
            throw new ArgumentException("ChainConfig.ShortageEscalation (κ) must be ≥ 0.");
        }
    }

    /// <summary>The IS-M flag needs info sharing and the estimated-error σ̂ with a free μ̂ (else it is a no-op or an undefined estimator).</summary>
    private static void ValidateInfoSharingSigma(ChainConfig cfg)
    {
        if (!cfg.InfoSharingSigmaFromIncomingOrders)
        {
            return;
        }

        if (!cfg.InfoSharing)
        {
            throw new ArgumentException("ChainConfig.InfoSharingSigmaFromIncomingOrders needs InfoSharing = true.");
        }

        if (cfg.Safety != SafetySource.EstimatedError || cfg.ConstantMean != null)
        {
            throw new ArgumentException("ChainConfig.InfoSharingSigmaFromIncomingOrders needs Safety = EstimatedError and no ConstantMean.");
        }
    }

    /// <summary>Stage count, per-stage arrays, R and the window p, and the R = 1-only σ̂ estimators.</summary>
    private static void ValidateStagesAndOrdering(ChainConfig cfg)
    {
        int n = cfg.LeadTimes.Length;
        if (n == 0)
        {
            throw new ArgumentException("At least one stage is required.");
        }

        RequirePerStage(cfg.Phases, n, nameof(cfg.Phases));
        RequirePerStage(cfg.Z, n, nameof(cfg.Z));
        RequirePerStage(cfg.Holding, n, nameof(cfg.Holding));
        RequirePerStage(cfg.Value, n, nameof(cfg.Value));
        RequirePerStage(cfg.OrderCost, n, nameof(cfg.OrderCost));
        RequirePerStage(cfg.ShortageCost, n, nameof(cfg.ShortageCost));

        int r = cfg.R;
        int p = cfg.Window;
        if (r < 1 || p < r || p % r != 0)
        {
            throw new ArgumentException("R must be ≥ 1 and divide the window p (plan R1).");
        }

        if (cfg.Safety == SafetySource.ChenOnePeriodError && r != 1)
        {
            throw new ArgumentException("Chen's one-period σ̂ (Eq. 3) is defined for R = 1 only.");
        }

        bool ewma = cfg.Safety == SafetySource.EwmaAbsError;
        if (ewma && r != 1)
        {
            throw new ArgumentException("The NRL EWMA σ̂ (Eq. 5) is defined for R = 1 only.");
        }

        if (ewma && !(cfg.AbsErrorSmoothing > 0.0 && cfg.AbsErrorSmoothing <= 1.0))
        {
            throw new ArgumentException("SafetySource.EwmaAbsError needs AbsErrorSmoothing α_s in (0, 1].");
        }
    }

    /// <summary>
    /// Lead times: one Kaplan entry per stage with E[L] equal to the planning L, every L finite and at least 1, an integer
    /// for deterministic arrivals, and an integer at R &gt; 1 under the integer cover rule.
    /// </summary>
    private static void ValidateLeadTimes(ChainConfig cfg)
    {
        int n = cfg.LeadTimes.Length;
        KaplanLeadTime[]? kaplan = cfg.StochasticLeadTimes;
        if (kaplan != null)
        {
            if (kaplan.Length != n)
            {
                throw new ArgumentException("StochasticLeadTimes needs one entry per stage.");
            }

            for (int k = 0; k < n; k++)
            {
                if (Math.Abs(kaplan[k].MeanLeadTime - cfg.LeadTimes[k]) > 1e-9)
                {
                    throw new ArgumentException($"Stage {k}: planning lead time {cfg.LeadTimes[k]} must equal E[L] = {kaplan[k].MeanLeadTime}.");
                }
            }
        }

        bool useCoverRule = UsesCoverRule(cfg, ResolveCoverRule(cfg));
        for (int k = 0; k < n; k++)
        {
            double l = cfg.LeadTimes[k];
            if (double.IsNaN(l) || double.IsInfinity(l))
            {
                throw new ArgumentException($"Stage {k}: the lead time must be finite.");
            }

            if (l < 1.0)
            {
                throw new ArgumentException($"Stage {k}: the lead time must be at least 1 period, got {l}.");
            }

            if (l != Math.Floor(l))
            {
                if (kaplan == null)
                {
                    throw new ArgumentException($"Stage {k}: a non-integer lead time {l} needs StochasticLeadTimes (deterministic arrivals are integer).");
                }

                if (cfg.R > 1 && !useCoverRule)
                {
                    throw new ArgumentException($"Stage {k}: the integer cover rule needs an integer planning lead time at R > 1, got {l} (leave ChainConfig.Cover unset for CycleAverage, or pick WorstPhase / LotsInTransit).");
                }
            }
        }
    }

    /// <summary>
    /// The cover rule in force (<see cref="ChainConfig.Cover"/>; class remarks). Unset: the integer rule when every planning
    /// L is an integer, B (CycleAverage) for the whole chain otherwise.
    /// </summary>
    private static CoverRule ResolveCoverRule(ChainConfig cfg)
    {
        return cfg.Cover
            ?? (cfg.R > 1 && cfg.LeadTimes.Any(v => v != Math.Floor(v)) ? CoverRule.CycleAverage : CoverRule.Integer);
    }

    /// <summary>True if c and the error window come from a <see cref="CoverRules"/> option (only at R &gt; 1).</summary>
    private static bool UsesCoverRule(ChainConfig cfg, CoverRule rule)
    {
        return rule != CoverRule.Integer && cfg.R > 1;
    }

    /// <summary>
    /// Plan of stage k (class remarks): cover c of S, the cover and weights of the realised error window, the periods that
    /// scale KnownSigma and the Chen/NRL constants, and the lag after which an order time's error is settled.
    /// </summary>
    private static StagePlan PlanStage(ChainConfig cfg, int k, int[] phases, int fixedLead, CoverRule rule)
    {
        bool posError = cfg.InfoSharing && !cfg.InfoSharingSigmaFromIncomingOrders;
        int r = cfg.R;
        double lp = cfg.LeadTimes[k] + r - 1;
        int errorSpan = (int)Math.Ceiling(lp);
        if (UsesCoverRule(cfg, rule))
        {
            // The error compares against the smooth cover of the option for POS input (retailer, IS-F), the lumpy one
            // otherwise (including IS-M, whose error input is the incoming demand); KnownSigma scales by √(smooth cover).
            double[] pmf = CoverRules.LeadPmf(cfg.StochasticLeadTimes?[k], fixedLead);
            double smooth = CoverRules.Cover(rule, pmf, r, null);
            int? customerOffset = k > 0 ? PhaseTarget.Offset(r, phases[k], phases[k - 1]) : null;
            bool lumpyInput = customerOffset != null && !posError;
            double ruleCover = customerOffset == null ? smooth : CoverRules.Cover(rule, pmf, r, customerOffset);
            double ruleErrorCover = lumpyInput ? ruleCover : smooth;
            double[] ruleWeights = CoverRules.Window(ruleErrorCover, r, lumpyInput ? customerOffset : null, errorSpan);
            return new StagePlan(ruleCover, ruleErrorCover, smooth, ruleWeights, SettleLag(ruleWeights.Length, r));
        }

        double cover = k > 0 && r > 1 ? r * PhaseTarget.Lumps((int)lp, r, phases[k], phases[k - 1]) : lp;
        double errorCover = posError ? lp : cover;
        return new StagePlan(cover, errorCover, lp, ProtectionWindow.Weights(lp), SettleLag(errorSpan, r));
    }

    /// <summary>Periods from an order time t0 to the first order time t0 + mR at which the window (t0, t0 + span] is complete.</summary>
    private static int SettleLag(int span, int r)
    {
        return (span + r - 1) / r * r;
    }

    private static int Mod(int x, int m)
    {
        return ((x % m) + m) % m;
    }

    /// <summary>An optional per-stage array (null = default) must have an entry for every stage.</summary>
    private static void RequirePerStage(Array? values, int stages, string name)
    {
        if (values != null && values.Length < stages)
        {
            throw new ArgumentException($"ChainConfig.{name} needs one entry per stage ({stages}), got {values.Length}.");
        }
    }

    /// <summary>Per-stage plan of the order-up-to level S = Cover·μ̂ + z·σ̂ and of the realised error window.</summary>
    /// <param name="Cover">Cover c of S, in periods of mean demand.</param>
    /// <param name="ErrorCover">Cover of the realised error window: c for the stage's error input (its incoming demand); Lp for POS input (retailer, IS-F).</param>
    /// <param name="SigmaPeriods">Periods that scale KnownSigma (σ·√·) and the Chen/NRL constants.</param>
    /// <param name="ErrorWeights">Weights of the periods t0+1 … t0+H on the realised side of the error.</param>
    /// <param name="SettleLag">Lag from an order time t0 to the order time at which its error is settled (a multiple of R).</param>
    private sealed record StagePlan(double Cover, double ErrorCover, double SigmaPeriods, double[] ErrorWeights, int SettleLag);

    /// <summary>State of one run of a validated configuration: plan, inventories, pipelines, forecasters, measurement sums.</summary>
    private sealed class ChainRun
    {
        private readonly ChainConfig _cfg;
        private readonly double[] _demand;
        private readonly int _r;
        private readonly int _p;
        private readonly int[] _phases;
        private readonly double[] _z;
        private readonly double[] _holding;
        private readonly double[] _value;
        private readonly int[] _fixedLead;
        private readonly KaplanLeadTime[]? _kaplan;
        private readonly int[][]? _drawnDue;
        private readonly StagePlan[] _plan;

        // Forecasting: the input each stage forecasts from (its incoming demand, or POS demand under info sharing), and the
        // μ̂ used at each order time while σ̂ is estimated (NaN otherwise).
        private readonly StageForecast[] _forecast;
        private readonly ErrorWindow[] _errorWindow;
        private readonly AbsErrorEwma[]? _absErrorEwma;
        private readonly double[][] _forecastInput;
        private readonly double[][] _muHatAtOrder;

        // The demand the realised error is taken on: the forecast input, or the incoming demand for IS-M.
        private readonly double[][] _errorInput;

        // Inventory. _arrivals[k][t]: quantity arriving at stage k at the start of t. _onOrder: placed and not yet received
        // (includes what the supplier still owes); _inTransit: dispatched to the stage and not yet arrived (the physical pipeline).
        private readonly double[][] _arrivals;
        private readonly double[] _onHand;
        private double _retailerStockFreeForDemand; // V_t of C46 (set in Step, read in Measure)
        private readonly double[] _backlog;
        private readonly int[] _backlogWeeks;
        private readonly double[] _onOrder;
        private readonly double[] _inTransit;

        // Per-period records (ChainResult), whole-run totals and the sums over the measurement window.
        private readonly double[][] _orders;
        private readonly double[][] _incoming;
        private readonly double[][] _shipPeriod;
        private readonly double[][] _onHandPeriod;
        private readonly double[][] _backlogPeriod;
        private readonly RunTotals _totals;
        private readonly WindowSums _sums;

        public ChainRun(ChainConfig cfg, double[] demand)
        {
            int n = cfg.LeadTimes.Length;
            int horizon = demand.Length;
            _cfg = cfg;
            _demand = demand;
            _r = cfg.R;
            _p = cfg.Window;
            _phases = cfg.Phases ?? new int[n];
            _z = cfg.Z ?? new double[n];
            _holding = cfg.Holding ?? new double[n];
            _value = cfg.Value ?? new double[n];
            Stages = n;

            _fixedLead = cfg.LeadTimes.Select(l => (int)l).ToArray();
            _kaplan = cfg.StochasticLeadTimes;
            _drawnDue = DrawArrivals(cfg, horizon);
            CoverRule rule = ResolveCoverRule(cfg);
            _plan = Enumerable.Range(0, n).Select(k => PlanStage(cfg, k, _phases, _fixedLead[k], rule)).ToArray();

            _forecast = Enumerable.Range(0, n).Select(_ => new StageForecast(_p, _r)).ToArray();
            _errorWindow = Enumerable.Range(0, n).Select(_ => new ErrorWindow(_p)).ToArray();
            _absErrorEwma = cfg.Safety == SafetySource.EwmaAbsError
                ? Enumerable.Range(0, n).Select(_ => new AbsErrorEwma(cfg.AbsErrorSmoothing)).ToArray()
                : null;
            _forecastInput = PerStage(n, horizon, 0.0);
            _muHatAtOrder = PerStage(n, horizon, double.NaN);

            _arrivals = PerStage(n, ArrivalSlots(cfg, _fixedLead, horizon), 0.0);
            _onHand = new double[n];
            _backlog = new double[n];
            _backlogWeeks = new int[n];
            _onOrder = new double[n];
            _inTransit = new double[n];

            _orders = PerStage(n, horizon, 0.0);
            _incoming = PerStage(n, horizon, 0.0);
            _errorInput = cfg.InfoSharingSigmaFromIncomingOrders ? _incoming : _forecastInput;
            _shipPeriod = PerStage(n, horizon, 0.0);
            _onHandPeriod = PerStage(n, horizon, 0.0);
            _backlogPeriod = PerStage(n, horizon, 0.0);
            _totals = new RunTotals(n);
            _sums = new WindowSums(n);
        }

        public int Stages { get; }

        /// <summary>
        /// Starts at the mean μ0 (plan S11): forecasts prefilled, and μ0 dispatched in each period before t = 0
        /// (deterministic: −L..−1; Kaplan: −m−1..−1). Those due at t ≥ 0 are in the pipeline (on-order L·μ0, in expectation
        /// E[L]·μ0); earlier dispatches arrived before t = 0.
        /// </summary>
        public void Prefill(double mu0)
        {
            for (int k = 0; k < Stages; k++)
            {
                _forecast[k].Prefill(mu0, Mod(_phases[k], _r));
                int oldest = _drawnDue == null ? -_fixedLead[k] : -_kaplan![k].MaxLag - 1;
                for (int dispatchPeriod = oldest; dispatchPeriod < 0; dispatchPeriod++)
                {
                    int at = Due(k, dispatchPeriod);
                    if (at >= 0)
                    {
                        Dispatch(k, at, mu0, _totals.InitialInTransit);
                        _onOrder[k] += mu0;
                    }
                }
            }
        }

        /// <summary>Period t at stage k: steps 1–5 of the period loop (class summary).</summary>
        public void Step(int t, int k)
        {
            // 1 receive
            double received = _arrivals[k][t];
            _onHand[k] += received;

            _onOrder[k] -= received;
            _inTransit[k] -= received;
            _totals.Arrived[k] += received;

            // 2 incoming demand
            double incomingDemand = k == 0 ? _demand[t] : _orders[k - 1][t];
            _incoming[k][t] = incomingDemand;
            _totals.Incoming[k] += incomingDemand;

            // 3 demand and shipment
            if (k == 0)
            {
                // V_t of C46: on-hand after arrivals less the backlog carried into t (that stock ships to earlier periods first).
                _retailerStockFreeForDemand = Math.Max(0.0, _onHand[0] - _backlog[0]);
            }

            _backlog[k] += incomingDemand;
            bool perfectUpstream = _cfg.PerfectSupplier && k > 0;
            double shipment = perfectUpstream ? incomingDemand : Math.Max(0.0, Math.Min(_onHand[k], _backlog[k]));
            _onHand[k] -= shipment;
            _backlog[k] -= shipment;
            _totals.Shipped[k] += shipment;
            if (k > 0 && !_cfg.PerfectSupplier)
            {
                Dispatch(k - 1, Due(k - 1, t), shipment, _totals.Dispatched);
            }

            // 4 forecast input
            double input = _cfg.InfoSharing ? _demand[t] : incomingDemand;
            _forecastInput[k][t] = input;
            _forecast[k].Observe(input);

            // 5 order at an order time
            double order = Mod(t - _phases[k], _r) == 0 ? PlaceOrder(t, k) : 0.0;

            _orders[k][t] = order;
            _shipPeriod[k][t] = shipment;
            _onHandPeriod[k][t] = _onHand[k];
            _backlogPeriod[k][t] = _backlog[k];
        }

        /// <summary>
        /// Counter of consecutive periods with a backlog per stage (n_{k,t} in <see cref="ChainConfig.ShortageEscalation"/>).
        /// Called once per period, after every stage has stepped and before <see cref="Measure"/>, also during warm-up.
        /// </summary>
        public void AgeBacklogs()
        {
            for (int k = 0; k < Stages; k++)
            {
                _backlogWeeks[k] = _backlog[k] > 1e-9 ? _backlogWeeks[k] + 1 : 0;
            }
        }

        /// <summary>
        /// Measurement at the end of period t, after every stage has shipped (plan §2 counting rule: arrivals of t+1 excluded).
        /// Stage k dispatches its shipment to stage k−1 after k−1's step, so a reading inside the stage loop would miss it in
        /// _inTransit[k−1] and report μ·(L−2).
        /// </summary>
        public void Measure(int t)
        {
            for (int k = 0; k < Stages; k++)
            {
                double stock = Math.Max(0.0, _onHand[k]);
                double pipeline = _inTransit[k] - _arrivals[k][t + 1];
                _sums.OnHand[k] += stock;
                _sums.Backlog[k] += _backlog[k];
                _sums.Pipeline[k] += pipeline;
                _sums.HoldingCost[k] += _holding[k] * stock;
                _sums.CapitalCost[k] += _cfg.CapitalRate * _value[k] * (stock + pipeline);
                double unitCost = _cfg.ShortageCost == null ? 0.0 : _cfg.ShortageCost[k];
                _sums.ShortageCost[k] += unitCost * _backlog[k] * (1.0 + _cfg.ShortageEscalation * _backlogWeeks[k]);
            }

            // Retailer service: fill rate (shipped / demanded) and ready rate (periods without backlog).
            _sums.RetailerDemand += _incoming[0][t];
            _sums.RetailerShipped += _shipPeriod[0][t];
            _sums.RetailerImmediate += Math.Min(_incoming[0][t], _retailerStockFreeForDemand);
            if (_backlog[0] <= 1e-9)
            {
                _sums.ReadyPeriods++;
            }
        }

        public ChainResult Result()
        {
            int measured = Math.Max(0, _demand.Length - _cfg.Warmup);
            double scale = measured > 0 ? measured : 1;
            return new ChainResult
            {
                Orders = _orders,
                Incoming = _incoming,
                Warmup = _cfg.Warmup,
                Shipped = _shipPeriod,
                OnHandPeriod = _onHandPeriod,
                BacklogPeriod = _backlogPeriod,
                MeanOnHand = _sums.OnHand.Select(v => v / scale).ToArray(),
                MeanBacklog = _sums.Backlog.Select(v => v / scale).ToArray(),
                MeanPipeline = _sums.Pipeline.Select(v => v / scale).ToArray(),
                ReadyRate = _sums.ReadyPeriods / scale,
                FillRate = _sums.RetailerDemand > 0 ? _sums.RetailerShipped / _sums.RetailerDemand : 1.0,
                ImmediateFillRate = _sums.RetailerDemand > 0 ? _sums.RetailerImmediate / _sums.RetailerDemand : 1.0,
                HoldingCost = _sums.HoldingCost,
                CapitalCost = _sums.CapitalCost,
                ShortageCost = _sums.ShortageCost,
                OrderCost = _sums.OrderCost,
                OrderCount = _totals.OrderCount,
                ProtectionErrorRms = _sums.ErrorSquares.Select((v, k) => _sums.ErrorCount[k] > 0 ? Math.Sqrt(v / _sums.ErrorCount[k]) : 0.0).ToArray(),
                ProtectionErrorMean = _sums.ErrorSum.Select((v, k) => _sums.ErrorCount[k] > 0 ? v / _sums.ErrorCount[k] : 0.0).ToArray(),
                CoverPeriods = _plan.Select(s => s.Cover).ToArray(),
                MeanSigmaHat = _sums.SigmaSum.Select((v, k) => _sums.SigmaCount[k] > 0 ? v / _sums.SigmaCount[k] : 0.0).ToArray(),
                OnHandEnd = _onHand,
                BacklogEnd = _backlog,
                ScheduledEnd = _inTransit,
                ArrivedTotal = _totals.Arrived,
                ShippedTotal = _totals.Shipped,
                IncomingTotal = _totals.Incoming,
                ScheduledAdded = _totals.Dispatched,
                ScheduledInitial = _totals.InitialInTransit,
            };
        }

        /// <summary>
        /// Kaplan lead times (plan S10): the arrival period of every dispatch, fixed when it is dispatched, from one uniform per
        /// stage and period drawn up front (stage k draws from SplitMix64.Derive(LeadTimeSeed, k)). Null for deterministic
        /// arrivals.
        /// </summary>
        private static int[][]? DrawArrivals(ChainConfig cfg, int horizon)
        {
            KaplanLeadTime[]? kaplan = cfg.StochasticLeadTimes;
            return kaplan?.Select((lead, k) => lead.DrawArrivals(new SplitMix64(SplitMix64.Derive(cfg.LeadTimeSeed, (ulong)k)), horizon - 1)).ToArray();
        }

        /// <summary>Length of the arrival arrays: the horizon plus the longest possible lead time (and a small margin).</summary>
        private static int ArrivalSlots(ChainConfig cfg, int[] fixedLead, int horizon)
        {
            KaplanLeadTime[]? kaplan = cfg.StochasticLeadTimes;
            int maxLead = kaplan == null
                ? fixedLead.Max()
                : Enumerable.Range(0, kaplan.Length).Max(k => Math.Max((int)Math.Ceiling(cfg.LeadTimes[k]), kaplan[k].MaxLag + 1));
            return horizon + maxLead + 3;
        }

        private static double[][] PerStage(int stages, int length, double initial)
        {
            return Enumerable.Range(0, stages).Select(_ => Enumerable.Repeat(initial, length).ToArray()).ToArray();
        }

        /// <summary>
        /// Order of stage k at its order time t: close the forecast bucket, settle the due error, then q = S − IP with
        /// S = c·μ̂ + z·σ̂ (clipped at 0 unless negative orders are allowed).
        /// </summary>
        private double PlaceOrder(int t, int k)
        {
            _forecast[k].CloseBucket();
            if (SettledError(t, k) is double e)
            {
                RecordError(t, k, e);
            }

            StagePlan plan = _plan[k];
            double mu = _cfg.ConstantMean ?? _forecast[k].Mean;
            double safety = SafetyTerm(k, plan.SigmaPeriods);
            double target = plan.Cover * mu + _z[k] * safety;
            double inventoryPosition = _onHand[k] - _backlog[k] + _onOrder[k];
            double order = target - inventoryPosition;
            if (!_cfg.AllowNegativeOrders && order < 0.0)
            {
                order = 0.0;
            }

            if (_cfg.Safety != SafetySource.KnownSigma && _cfg.ConstantMean == null)
            {
                _muHatAtOrder[k][t] = mu;
                if (t >= _cfg.Warmup)
                {
                    _sums.SigmaSum[k] += safety;
                    _sums.SigmaCount[k]++;
                }
            }

            if (order > 0.0)
            {
                _totals.OrderCount[k]++;
                if (t >= _cfg.Warmup)
                {
                    _sums.OrderCost[k] += _cfg.OrderCost == null ? 0.0 : _cfg.OrderCost[k];
                }
            }

            _onOrder[k] += order;
            if (_cfg.PerfectSupplier || k == Stages - 1)
            {
                Dispatch(k, Due(k, t), order, _totals.Dispatched);
            }

            return order;
        }

        /// <summary>
        /// Error settled at order time t (class remarks), or null if none is due: the protection-period error of the order
        /// time t − SettleLag (EstimatedError), or the one-period error of order time t − 1 (Chen, NRL). _muHatAtOrder is
        /// non-NaN only while σ̂ is estimated (no KnownSigma, no ConstantMean).
        /// </summary>
        private double? SettledError(int t, int k)
        {
            double[] muHat = _muHatAtOrder[k];
            double[] input = _errorInput[k];
            switch (_cfg.Safety)
            {
                case SafetySource.EstimatedError:
                {
                    int t0 = t - _plan[k].SettleLag;
                    if (t0 < 0 || double.IsNaN(muHat[t0]))
                    {
                        return null;
                    }

                    double realised = 0.0;
                    double[] weights = _plan[k].ErrorWeights;
                    for (int i = 0; i < weights.Length; i++)
                    {
                        realised += weights[i] * input[t0 + 1 + i];
                    }

                    return realised - _plan[k].ErrorCover * muHat[t0];
                }

                case SafetySource.ChenOnePeriodError:
                case SafetySource.EwmaAbsError:
                    return t >= 1 && !double.IsNaN(muHat[t - 1]) ? input[t] - muHat[t - 1] : null;

                default:
                    return null;
            }
        }

        /// <summary>Feeds e to the stage's σ̂ estimator and, in the measurement window, to ProtectionErrorRms/Mean.</summary>
        private void RecordError(int t, int k, double e)
        {
            if (_absErrorEwma != null)
            {
                _absErrorEwma[k].Add(e);
            }
            else
            {
                _errorWindow[k].Add(e);
            }

            if (t >= _cfg.Warmup)
            {
                _sums.ErrorSquares[k] += e * e;
                _sums.ErrorSum[k] += e;
                _sums.ErrorCount[k]++;
            }
        }

        /// <summary>σ̂ term of S (before z): σ·√Lp, the RMS σ̂, Chen's C·RMS or the NRL K_L·Δ̂; 0 with a constant mean.</summary>
        private double SafetyTerm(int k, double lp)
        {
            return _cfg.Safety switch
            {
                SafetySource.KnownSigma => _cfg.KnownSigma * Math.Sqrt(lp),
                _ when _cfg.ConstantMean.HasValue => 0.0,
                SafetySource.EstimatedError => _errorWindow[k].Sigma,
                SafetySource.ChenOnePeriodError => _errorWindow[k].Sigma * ChenFormulas.SigmaConstantIid(lp, _p),
                SafetySource.EwmaAbsError => NrlFormulas.KFactorIid(lp, _p) * _absErrorEwma![k].Mad,
                _ => throw new ArgumentOutOfRangeException(nameof(ChainConfig.Safety), _cfg.Safety, "Unknown SafetySource."),
            };
        }

        /// <summary>Arrival period of what is dispatched to stage k in period s.</summary>
        private int Due(int k, int s)
        {
            return _drawnDue == null ? s + _fixedLead[k] : _drawnDue[k][s + _kaplan![k].MaxLag + 1];
        }

        /// <summary>Puts x on stage k's inbound pipeline, arriving at period `at`, and books it in tally.</summary>
        private void Dispatch(int k, int at, double x, double[] tally)
        {
            _arrivals[k][at] += x;
            _inTransit[k] += x;
            tally[k] += x;
        }
    }

    /// <summary>Whole-run conservation bookkeeping per stage (ChainResult; V6).</summary>
    private sealed class RunTotals(int stages)
    {
        /// <summary>Dispatched to the stage during the run (ChainResult.ScheduledAdded).</summary>
        public double[] Dispatched { get; } = new double[stages];

        /// <summary>In transit to the stage at t = 0 from the prefill (ChainResult.ScheduledInitial).</summary>
        public double[] InitialInTransit { get; } = new double[stages];

        public double[] Arrived { get; } = new double[stages];

        public double[] Shipped { get; } = new double[stages];

        public double[] Incoming { get; } = new double[stages];

        public int[] OrderCount { get; } = new int[stages];
    }

    /// <summary>Sums over the measurement window (t ≥ Warmup): per stage, and the retailer's service counts.</summary>
    private sealed class WindowSums(int stages)
    {
        public double[] OnHand { get; } = new double[stages];

        public double[] Backlog { get; } = new double[stages];

        public double[] Pipeline { get; } = new double[stages];

        public double[] HoldingCost { get; } = new double[stages];

        public double[] CapitalCost { get; } = new double[stages];

        public double[] ShortageCost { get; } = new double[stages];

        public double[] OrderCost { get; } = new double[stages];

        public double[] ErrorSquares { get; } = new double[stages];

        public double[] ErrorSum { get; } = new double[stages];

        public int[] ErrorCount { get; } = new int[stages];

        public double[] SigmaSum { get; } = new double[stages];

        public int[] SigmaCount { get; } = new int[stages];

        public double RetailerDemand { get; set; }

        public double RetailerShipped { get; set; }

        public double RetailerImmediate { get; set; }

        public double ReadyPeriods { get; set; }
    }
}
