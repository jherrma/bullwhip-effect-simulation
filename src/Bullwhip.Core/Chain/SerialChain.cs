using System.Linq;
using Bullwhip.Core.Analytic;
using Bullwhip.Core.Forecast;
using Bullwhip.Core.Rng;

namespace Bullwhip.Core.Chain;

/// <summary>
/// Period loop of the serial chain (plan §3). Per period t, for stages k = 0..N−1 (downstream first):
/// 1 receive arrivals; 2 read incoming demand; 3 ship (backlog, min(on-hand, backlog));
/// 4 observe demand for the forecast; 5 at an order time place q = S − IP.
/// </summary>
public static class SerialChain
{
    public static ChainResult Run(ChainConfig cfg, double[] demand)
    {
        int n = cfg.LeadTimes.Length;
        if (n == 0)
        {
            throw new ArgumentException("At least one stage is required.");
        }

        int horizon = demand.Length;
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

        // Kaplan lead times (plan S10): the arrival period of every dispatch is fixed when it is dispatched, from one
        // uniform per stage and period drawn up front; Due(k, s) is t + L_k without them (unchanged path).
        KaplanLeadTime[]? kaplan = cfg.StochasticLeadTimes;
        int[][]? due = null;
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

            due = new int[n][];
            for (int k = 0; k < n; k++)
            {
                due[k] = kaplan[k].DrawArrivals(new SplitMix64(SplitMix64.Derive(cfg.LeadTimeSeed, (ulong)k)), horizon - 1);
            }
        }

        int Due(int k, int s) => due == null ? s + cfg.LeadTimes[k] : due[k][s + kaplan![k].MaxLag + 1];

        int maxLead = kaplan == null
            ? cfg.LeadTimes.Max()
            : Enumerable.Range(0, n).Max(k => Math.Max(cfg.LeadTimes[k], kaplan[k].MaxLag + 1));
        int len = horizon + maxLead + 3;
        int[] phases = cfg.Phases ?? new int[n];
        double[] zs = cfg.Z ?? new double[n];
        double[] holding = cfg.Holding ?? new double[n];
        double[] value = cfg.Value ?? new double[n];

        var arr = new double[n][];
        var q = new double[n][];
        var inc = new double[n][];
        var obs = new double[n][];
        var pending = new double[n][];
        var fc = new StageForecast[n];
        var err = new ErrorWindow[n];
        for (int k = 0; k < n; k++)
        {
            arr[k] = new double[len];
            q[k] = new double[horizon];
            inc[k] = new double[horizon];
            obs[k] = new double[horizon];
            pending[k] = Enumerable.Repeat(double.NaN, horizon).ToArray();
            fc[k] = new StageForecast(p, r);
            // σ̂ = RMS of the last p order-time errors (p samples, as at R = 1; spans p·R periods). Every sample is the
            // error of an actual order, so E[σ̂²] = σ_e². Per-period errors are not: upstream the window (s, s+Lp] holds
            // a phase-dependent lump count, and consecutive ones share Lp−1 periods and μ̂. p/R samples gave E[σ̂] =
            // 0.886·σ_e (R=4, L=2, p=8); p gives 0.964 (R = 1: 0.958), the Jensen bias in GateA.RmsMeanFactor.
            err[k] = new ErrorWindow(p);
        }

        // Periods of mean demand that S covers: Lp, or n·R for the lumpy incoming demand of an upstream stage at R > 1
        // (PhaseTarget). The error term covers the forecast input, which is POS demand under info sharing.
        var cover = new int[n];
        var coverObs = new int[n];
        for (int k = 0; k < n; k++)
        {
            int lpk = cfg.LeadTimes[k] + r - 1;
            cover[k] = k > 0 && r > 1 ? r * PhaseTarget.Lumps(lpk, r, phases[k], phases[k - 1]) : lpk;
            coverObs[k] = cfg.InfoSharing ? lpk : cover[k];
        }

        var onHand = new double[n];
        var backlog = new double[n];
        var onOrder = new double[n];
        var sched = new double[n];
        var schedAdded = new double[n];
        var schedInitial = new double[n];
        var arrived = new double[n];
        var shipped = new double[n];
        var incomingTotal = new double[n];
        var orderCount = new int[n];
        var shipPeriod = new double[n][];
        var onHandPeriod = new double[n][];
        var backlogPeriod = new double[n][];
        for (int k = 0; k < n; k++)
        {
            shipPeriod[k] = new double[horizon];
            onHandPeriod[k] = new double[horizon];
            backlogPeriod[k] = new double[horizon];
        }

        if (cfg.InitialMean > 0)
        {
            double mu0 = cfg.InitialMean;
            for (int k = 0; k < n; k++)
            {
                fc[k].Prefill(mu0, Mod(phases[k], r));
                if (due == null)
                {
                    for (int tau = 0; tau < cfg.LeadTimes[k]; tau++)
                    {
                        arr[k][tau] += mu0;
                        sched[k] += mu0;
                        schedInitial[k] += mu0;
                        onOrder[k] += mu0;
                    }
                }
                else
                {
                    // Pre-history: μ dispatched in each period −m−1..−1; those due at t ≥ 0 are in the pipeline
                    // (expected on-order E[L]·μ, as Lμ for fixed L). Earlier dispatches have arrived before t = 0.
                    for (int s = -kaplan![k].MaxLag - 1; s < 0; s++)
                    {
                        int at = Due(k, s);
                        if (at >= 0)
                        {
                            arr[k][at] += mu0;
                            sched[k] += mu0;
                            schedInitial[k] += mu0;
                            onOrder[k] += mu0;
                        }
                    }
                }
            }
        }

        var sumOnHand = new double[n];
        var sumBacklog = new double[n];
        var sumPipeline = new double[n];
        var holdingCost = new double[n];
        var capitalCost = new double[n];
        var shortageCost = new double[n];
        var orderCost = new double[n];
        var errSq = new double[n];
        var errCount = new int[n];
        var sigmaSum = new double[n];
        var sigmaCount = new int[n];
        double demandWindow = 0.0;
        double shippedWindow = 0.0;
        double readyPeriods = 0.0;
        int measured = Math.Max(0, horizon - cfg.Warmup);

        for (int t = 0; t < horizon; t++)
        {
            for (int k = 0; k < n; k++)
            {
                // 1 receive
                double a = arr[k][t];
                onHand[k] += a;
                onOrder[k] -= a;
                sched[k] -= a;
                arrived[k] += a;

                // 2 incoming demand
                double d = k == 0 ? demand[t] : q[k - 1][t];
                inc[k][t] = d;
                incomingTotal[k] += d;

                // 3 demand and shipment
                backlog[k] += d;
                bool perfectUpstream = cfg.PerfectSupplier && k > 0;
                double s = perfectUpstream ? d : Math.Max(0.0, Math.Min(onHand[k], backlog[k]));
                onHand[k] -= s;
                backlog[k] -= s;
                shipped[k] += s;
                if (k > 0 && !cfg.PerfectSupplier)
                {
                    arr[k - 1][Due(k - 1, t)] += s;
                    sched[k - 1] += s;
                    schedAdded[k - 1] += s;
                }

                // 4 forecast input
                double x = cfg.InfoSharing ? demand[t] : d;
                obs[k][t] = x;
                fc[k].Observe(x);
                int lp = cfg.LeadTimes[k] + r - 1;

                // 5 order at order time
                double qk = 0.0;
                if (Mod(t - phases[k], r) == 0)
                {
                    fc[k].CloseBucket();

                    // Protection-period error (plan §2, R1). The order at order time t0 sees IP after d[t0]; it
                    // arrives at t0+L and the next one (placed t0+R) at t0+R+L, so net stock at the end of t0+Lp is
                    // S − D(t0, t0+Lp]. S must cover e = D(t0, t0+Lp] − c·μ̂(t0), c = cover (Lp, or n·R lumps), so
                    // the realised side is the exact per-period sum over (t0, t0+Lp]. Smooth iid demand (σ², window
                    // p): D and μ̂ use disjoint periods, Var(e) = Lp·σ² + Lp²·σ²/p, which the RMS reproduces. A sum
                    // over R-buckets with the partial bucket weighted w = min(R, Lp − jR)/R has the right mean but
                    // puts w²·R·σ² instead of w·R·σ² on that bucket (σ̂ 4.7 % low at R=4, L=2, p=8). With lumps the
                    // per-period window holds n lumps, and c = n·R removes the offset (Lp − nR)·μ that would
                    // otherwise enter σ̂. The window closes by the order time t0+mR, m = ceil(Lp/R), where the
                    // error is settled before σ̂ is read: one sample per order time. R = 1: the window (t−Lp, t].
                    if (cfg.Safety == SafetySource.EstimatedError && cfg.ConstantMean == null)
                    {
                        int t0 = t - (lp + r - 1) / r * r;
                        if (t0 >= 0 && !double.IsNaN(pending[k][t0]))
                        {
                            double realised = 0.0;
                            for (int i = t0 + 1; i <= t0 + lp; i++)
                            {
                                realised += obs[k][i];
                            }

                            double e = realised - coverObs[k] * pending[k][t0];
                            err[k].Add(e);
                            if (t >= cfg.Warmup)
                            {
                                errSq[k] += e * e;
                                errCount[k]++;
                            }
                        }
                    }

                    // Chen et al. (2000) Eq. (3): e_t = D_t − D̂¹_t with D̂¹_t the MA of the p periods before t, i.e. μ̂ of
                    // order time t − 1 (Chen's order at the start of t+1 is the model's order at the end of t).
                    if (cfg.Safety == SafetySource.ChenOnePeriodError && cfg.ConstantMean == null
                        && t >= 1 && !double.IsNaN(pending[k][t - 1]))
                    {
                        double e1 = obs[k][t] - pending[k][t - 1];
                        err[k].Add(e1);
                        if (t >= cfg.Warmup)
                        {
                            errSq[k] += e1 * e1;
                            errCount[k]++;
                        }
                    }

                    double mu = cfg.ConstantMean ?? fc[k].Mean;
                    double safety = cfg.Safety == SafetySource.KnownSigma
                        ? cfg.KnownSigma * Math.Sqrt(lp)
                        : (cfg.ConstantMean.HasValue ? 0.0 : err[k].Sigma);
                    if (cfg.Safety == SafetySource.ChenOnePeriodError)
                    {
                        safety *= ChenFormulas.SigmaConstantIid(lp, p);
                    }

                    double target = cover[k] * mu + zs[k] * safety;
                    double ip = onHand[k] - backlog[k] + onOrder[k];
                    qk = target - ip;
                    if (!cfg.AllowNegativeOrders && qk < 0.0)
                    {
                        qk = 0.0;
                    }

                    if (cfg.Safety != SafetySource.KnownSigma && cfg.ConstantMean == null)
                    {
                        pending[k][t] = mu;
                        if (t >= cfg.Warmup)
                        {
                            sigmaSum[k] += safety;
                            sigmaCount[k]++;
                        }
                    }

                    if (qk > 0.0)
                    {
                        orderCount[k]++;
                        if (t >= cfg.Warmup)
                        {
                            orderCost[k] += cfg.OrderCost;
                        }
                    }

                    onOrder[k] += qk;
                    if (cfg.PerfectSupplier || k == n - 1)
                    {
                        arr[k][Due(k, t)] += qk;
                        sched[k] += qk;
                        schedAdded[k] += qk;
                    }
                }

                q[k][t] = qk;
                shipPeriod[k][t] = s;
                onHandPeriod[k][t] = onHand[k];
                backlogPeriod[k][t] = backlog[k];

                // measurement
                if (t >= cfg.Warmup)
                {
                    double on = Math.Max(0.0, onHand[k]);
                    double pipe = sched[k] - arr[k][t + 1];
                    sumOnHand[k] += on;
                    sumBacklog[k] += backlog[k];
                    sumPipeline[k] += pipe;
                    holdingCost[k] += holding[k] * on;
                    capitalCost[k] += cfg.CapitalRate * value[k] * (on + pipe);
                    if (k == 0)
                    {
                        shortageCost[0] += cfg.ShortageCost * backlog[0];
                        demandWindow += d;
                        shippedWindow += s;
                        if (backlog[0] <= 1e-9)
                        {
                            readyPeriods++;
                        }
                    }
                }
            }
        }

        double scale = measured > 0 ? measured : 1;
        return new ChainResult
        {
            Orders = q,
            Incoming = inc,
            Warmup = cfg.Warmup,
            Shipped = shipPeriod,
            OnHandPeriod = onHandPeriod,
            BacklogPeriod = backlogPeriod,
            MeanOnHand = sumOnHand.Select(v => v / scale).ToArray(),
            MeanBacklog = sumBacklog.Select(v => v / scale).ToArray(),
            MeanPipeline = sumPipeline.Select(v => v / scale).ToArray(),
            ReadyRate = readyPeriods / scale,
            FillRate = demandWindow > 0 ? shippedWindow / demandWindow : 1.0,
            HoldingCost = holdingCost,
            CapitalCost = capitalCost,
            ShortageCost = shortageCost,
            OrderCost = orderCost,
            OrderCount = orderCount,
            ProtectionErrorRms = errSq.Select((v, k) => errCount[k] > 0 ? Math.Sqrt(v / errCount[k]) : 0.0).ToArray(),
            MeanSigmaHat = sigmaSum.Select((v, k) => sigmaCount[k] > 0 ? v / sigmaCount[k] : 0.0).ToArray(),
            OnHandEnd = onHand,
            BacklogEnd = backlog,
            ScheduledEnd = sched,
            ArrivedTotal = arrived,
            ShippedTotal = shipped,
            IncomingTotal = incomingTotal,
            ScheduledAdded = schedAdded,
            ScheduledInitial = schedInitial,
        };
    }

    private static int Mod(int x, int m)
    {
        return ((x % m) + m) % m;
    }
}
