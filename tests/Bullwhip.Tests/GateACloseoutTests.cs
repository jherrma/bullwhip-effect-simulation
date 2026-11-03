using Bullwhip.Core.Analytic;
using Bullwhip.Core.Chain;
using Bullwhip.Core.Rng;
using Bullwhip.Core.Stats;
using Bullwhip.Core.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Bullwhip.Tests;

/// <summary>
/// Gate A close-out (plan §5): V7 under Kaplan stochastic lead times, V4 estimator comparison (Chen Eq. 3 vs realised
/// error), V7 at p = 400 against Chen's formula, V13 with Bonferroni and a second parameter point. Plan settings:
/// 100 replications × 2000 periods after 200 warm-up, μ = 100, σ = 10, fixed seeds (GateA.Demand, GateA.LeadTimeSeed).
/// </summary>
public class GateACloseoutTests
{
    private const int Reps = GateA.Replications;
    private const int Periods = GateA.Periods;
    private const int Warm = GateA.Warmup;
    private const double Sigma = GateA.Sigma;
    private const double Var = Sigma * Sigma;

    // Target lags of Kaplan's T = L − 1, fixed before the first run: E[T] = 1, so E[L] = 2 = the planning L, with
    // Var(T) = 0.5 (A) and 1.0 (B). Hazards 0.25, 0.667, 1 and 0.4, 0.5, 0.667, 1 are nondecreasing (representable).
    private static readonly double[] LagA = { 0.25, 0.50, 0.25 };
    private static readonly double[] LagB = { 0.40, 0.30, 0.20, 0.10 };

    // Base-case lag (plan §2, S10): door-to-door transit T = L − 1 ~ gamma with mean 5.4 and CV 0.30 (shape 1/0.09, scale
    // 0.486), rounded to the nearest week: P(T = i) = F(i + ½) − F(i − ½), F(−½) := 0, i = 0..20, renormalised; truncated
    // share 1 − F(20.5) = 3.6e−9 (< 0.1 %). F by a series / continued-fraction regularised incomplete gamma in double
    // precision, cross-checked against quadrature of the density (|ΔF| ≤ 4e−13). The rounding moves the moments a little:
    // E[T] = 5.39999922, so E[L] = 6.39999922 (7.8e−7 below 6.4; an earlier coarse quadrature put it at 1e−4), CV(T) = 0.3047.
    // The hazard is nondecreasing (checked by the KaplanLeadTime constructor).
    internal static readonly double[] LagBase =
    {
        1.0215369815754723e-08, 0.00031839334897785067, 0.014677007230507447, 0.09163712768244942,
        0.20608523492045466, 0.25139573696779643, 0.2051233796515591, 0.12615514375022455,
        0.06296225634340086, 0.026767884640706487, 0.010024367577260896, 0.0033873417259871804,
        0.0010514033910037294, 0.0003038709360491412, 8.264441198664055e-05, 2.1329915191046416e-05,
        5.259662952369362e-06, 1.2460257555989471e-06, 2.848989312460469e-07, 6.31131713598911e-08,
        1.359026418497758e-08,
    };

    private readonly ITestOutputHelper _out;

    public GateACloseoutTests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static ChainConfig Four(KaplanLeadTime? lag = null) => new()
    {
        LeadTimes = new double[] { 2, 2, 2, 2 },
        Window = 8,
        InitialMean = 100,
        Warmup = Warm,
        StochasticLeadTimes = lag == null ? null : new[] { lag, lag, lag, lag },
    };

    private static string Fmt(IEnumerable<double> x) => string.Join(" / ", x.Select(v => v.ToString("0.#####")));

    private static double Mean(double[] pmf) => pmf.Select((x, i) => i * x).Sum();

    private bool Check(string name, double reference, double[] values, double z)
    {
        var (mean, se) = GateA.MeanAndSe(values);
        bool ok = GateA.Passes(reference, mean, se, z);
        _out.WriteLine($"{name}: sim={mean:0.000000} ref={reference:0.000000} rel={mean / reference - 1:+0.000%;-0.000%} se={se:0.000000} tol=2% & |sim−ref| ≤ {z:0.00}·se {(ok ? "PASS" : "FAIL")}");
        return ok;
    }

    [Fact]
    public void V7_KaplanMatrixImpliesTargetLag()
    {
        // Analytic: p_j from the inversion, Kaplan's matrix (p. 495) from p_j, then the lag of one order from the matrix
        // alone (Markov propagation, KaplanLeadTime.LagPmfFromMatrix). It must give back the target and E[T] exactly.
        foreach (var (name, target) in new[] { ("A", LagA), ("B", LagB) })
        {
            var lt = new KaplanLeadTime(target);
            var matrix = lt.TransitionMatrix();
            var implied = KaplanLeadTime.LagPmfFromMatrix(matrix, out double leftover);
            double impliedMean = Mean(implied);
            double targetMean = Mean(target);
            for (int i = 0; i < matrix.GetLength(0); i++)
            {
                var row = Enumerable.Range(0, matrix.GetLength(1)).Select(j => matrix[i, j]).ToArray();
                _out.WriteLine($"Kaplan {name} matrix row V={i + 1}: {Fmt(row)}");
                Assert.True(Math.Abs(row.Sum() - 1.0) < 1e-12);
            }

            _out.WriteLine($"Kaplan {name}: target P(T=i) {Fmt(target)}; p_j {Fmt(lt.CutPmf)}; matrix-implied {Fmt(implied)}");
            _out.WriteLine($"Kaplan {name}: E[T] implied={impliedMean:0.000000000000} target={targetMean:0.000000000000} tol=1e-12; Var(T)={lt.LagVariance:0.0000}; E[L]={lt.MeanLeadTime:0.0000} (planning L = 2)");
            Assert.True(leftover < 1e-12);
            for (int i = 0; i < target.Length; i++)
            {
                Assert.True(Math.Abs(implied[i] - target[i]) < 1e-12, $"{name}: P(T={i}) {implied[i]} vs {target[i]}");
            }

            Assert.True(Math.Abs(impliedMean - targetMean) < 1e-12);
            Assert.True(Math.Abs(lt.MeanLeadTime - 2.0) < 1e-12);
        }

        // Kaplan's printed lag formula (p. 495) reads p_j as the age hazard: p_j = P(T=j)/P(T≥j), p_m = 1 − Σ. Through the
        // printed matrix this gives another lag (target A: E[T] = 0.8125, not 1), so that reading cannot set up P.
        var hazard = new[] { 0.25, 0.5 / 0.75, 0.0 };
        hazard[2] = 1.0 - hazard[0] - hazard[1];
        var printed = KaplanLeadTime.LagPmfFromMatrix(KaplanLeadTime.Matrix(hazard), out _);
        _out.WriteLine($"Kaplan A, hazard reading of p_j: matrix-implied {Fmt(printed)}, E[T]={Mean(printed):0.0000} vs target 1");
        Assert.True(Math.Abs(Mean(printed) - 1.0) > 0.1);
    }

    [Fact]
    public void V7_KaplanSamplerReproducesLagMoments()
    {
        // Sampler: per replication one stream (stage 0 of GateA.LeadTimeSeed(rep)), 2000 dispatches. Mean and variance of
        // the realised L against E[L] = 2 and Var(L) = Var(T), plan pass rule, Bonferroni over 4 points. No crossing.
        double z = GateA.BonferroniZ(4);
        bool ok = true;
        foreach (var (name, target) in new[] { ("A", LagA), ("B", LagB) })
        {
            var lt = new KaplanLeadTime(target);
            int m = lt.MaxLag;
            var means = new double[Reps];
            var vars = new double[Reps];
            for (int rep = 0; rep < Reps; rep++)
            {
                var due = lt.DrawArrivals(new SplitMix64(SplitMix64.Derive(GateA.LeadTimeSeed(rep), 0)), Periods - 1);
                for (int i = 1; i < due.Length; i++)
                {
                    Assert.True(due[i] >= due[i - 1], "orders crossed");
                }

                var lags = Enumerable.Range(0, Periods).Select(s => (double)(due[s + m + 1] - s)).ToArray();
                Assert.True(lags.All(l => l >= 1 && l <= m + 1));
                means[rep] = Descriptive.Mean(lags, 0, Periods);
                vars[rep] = Descriptive.Variance(lags, 0, Periods);
            }

            ok &= Check($"Kaplan {name} sampler E[L]", lt.MeanLeadTime, means, z);
            ok &= Check($"Kaplan {name} sampler Var(L)", lt.LagVariance, vars, z);
        }

        Assert.True(ok);
    }

    private static void AssertConservation(ChainResult res)
    {
        double lhs = 0, rhs = 0;
        for (int k = 0; k < res.OnHandEnd.Length; k++)
        {
            Assert.True(Math.Abs(res.OnHandEnd[k] - (res.ArrivedTotal[k] - res.ShippedTotal[k])) <= 1e-9 * Math.Max(1, res.ArrivedTotal[k]));
            Assert.True(Math.Abs(res.BacklogEnd[k] - (res.IncomingTotal[k] - res.ShippedTotal[k])) <= 1e-9 * Math.Max(1, res.IncomingTotal[k]));
            lhs += res.OnHandEnd[k] + res.ScheduledEnd[k] - res.ScheduledInitial[k];
            rhs += res.ScheduledAdded[k] - res.ShippedTotal[k];
        }

        Assert.True(Math.Abs(lhs - rhs) <= 1e-9 * Math.Max(1, res.ArrivedTotal.Sum()));
    }

    [Fact]
    public void V7b_BullwhipInvariantUnderKaplanLeadTimes()
    {
        // Four stages, normal mode (upstream shortages possible), L = 2 planned at every stage, z = 0, no clipping.
        // Per lag variance: BR at stages 1 and 2 against the exact filter reference and against the fixed-L simulation
        // (same demand), plus the retailer pipeline of a perfect-supplier run with the same lags against Little's law
        // μ·(E[L] − 1) = 100 (the counting rule V14 checks). 2 variances × 5 = 10 points. In normal mode the pipeline is
        // measured after every stage has shipped in the period, so it also shows μ·E[(L − 1)⁺] = μ·(E[L] − 1) (L ≥ 1);
        // that is printed, not asserted. (Measured inside the stage loop it was μ·E[(L − 2)⁺]: stage k−1 missed s_k[t].)
        double z = GateA.BonferroniZ(10);
        var refs = new[] { GateA.FilterReference(1, 2, 8), GateA.FilterReference(2, 2, 8) };
        bool ok = true;
        foreach (var (name, target) in new[] { ("A Var(L)=0.5", LagA), ("B Var(L)=1.0", LagB) })
        {
            var lt = new KaplanLeadTime(target);
            var brFixed = new[] { new double[Reps], new double[Reps] };
            var brStoch = new[] { new double[Reps], new double[Reps] };
            var pipe = new double[Reps];
            double maxOrderDiff = 0, maxOnHandDiff = 0, blFixed = 0, blStoch = 0, pipeNormal = 0;
            for (int rep = 0; rep < Reps; rep++)
            {
                var d = GateA.Demand(rep, Warm + Periods, 100, 0, Sigma);
                var a = SerialChain.Run(Four(), d);
                var stoch = Four(lt) with { LeadTimeSeed = GateA.LeadTimeSeed(rep) };
                var b = SerialChain.Run(stoch, d);
                var c = SerialChain.Run(stoch with { PerfectSupplier = true }, d);
                AssertConservation(b);
                AssertConservation(c);
                pipeNormal += b.MeanPipeline[0] / Reps;
                for (int k = 0; k < 2; k++)
                {
                    brFixed[k][rep] = a.Bullwhip(k, Var);
                    brStoch[k][rep] = b.Bullwhip(k, Var);
                }

                pipe[rep] = c.MeanPipeline[0];
                blFixed += a.MeanBacklog[0] / Reps;
                blStoch += b.MeanBacklog[0] / Reps;
                for (int k = 0; k < 4; k++)
                {
                    for (int t = Warm; t < d.Length; t++)
                    {
                        maxOrderDiff = Math.Max(maxOrderDiff, Math.Abs(a.Orders[k][t] - b.Orders[k][t]));
                        maxOnHandDiff = Math.Max(maxOnHandDiff, Math.Abs(a.OnHandPeriod[k][t] - b.OnHandPeriod[k][t]));
                    }
                }
            }

            for (int k = 0; k < 2; k++)
            {
                ok &= Check($"V7b {name} stage {k + 1} BR vs exact", refs[k], brStoch[k], z);
                ok &= Check($"V7b {name} stage {k + 1} BR vs fixed-L sim {GateA.MeanAndSe(brFixed[k]).Mean:0.000000}", GateA.MeanAndSe(brFixed[k]).Mean, brStoch[k], z);
            }

            ok &= Check($"V7b {name} retailer pipeline, perfect supplier (Little)", 100.0 * (lt.MeanLeadTime - 1), pipe, z);
            double shifted = 100.0 * (lt.MeanLeadTime - 1); // μ·E[(L − 1)⁺], L ≥ 1: items due at t+2 or later at the end of t
            _out.WriteLine($"V7b {name}: normal-mode retailer pipeline {pipeNormal:0.000} vs μ·E[(L − 1)⁺] = {shifted:0.000} (printed only)");
            _out.WriteLine($"V7b {name}: max |q_stoch − q_fixed| (t ≥ warm-up, 4 stages) = {maxOrderDiff:E2}; max |on-hand diff| = {maxOnHandDiff:0.00}; retailer mean backlog fixed {blFixed:0.000} vs Kaplan {blStoch:0.000}");
            Assert.True(maxOrderDiff < 1e-9, "orders are not pathwise invariant");
            Assert.True(maxOnHandDiff > 1.0, "stochastic lead time had no physical effect");
        }

        Assert.True(ok);
    }

    [Fact]
    public void V4b_ChenSigmaVersusRealisedErrorSigma()
    {
        // V4 parameters: one stage, L = 2, p = 8, z = 1, R = 1, iid, CRN (the same demand for all three runs).
        // Chen p. 438: Var(q) = bound·Var(D) + 2z(1 + 2L/p)·Cov(D, σ̂) + z²·Var(σ̂_t − σ̂_{t−1}), Cov = 0 by Lemma 2.1 for
        // Eq. (3). The cross term is the sample identity cross = BR(z) − BR(0) − z²·Var̂(Δσ̂)/σ², Δσ̂ = (q_z − q_0)/z.
        // Two-sided (Bonferroni over 5): mean σ̂ of each estimator against its exact Jensen value b·σ_e (C makes
        // E[σ̂²] = σ_e² for Chen too), the one-period error RMS against σ√(1 + 1/p), and the cross term of each against 0.
        const double zSafety = 1.0;
        var zero = new ChainConfig { LeadTimes = new double[] { 2 }, Window = 8, Z = new[] { 0.0 }, InitialMean = 100, Warmup = Warm };
        var ours = zero with { Z = new[] { zSafety } };
        var chen = ours with { Safety = SafetySource.ChenOnePeriodError };
        double bound = ChenFormulas.SingleStageBullwhipRatio(2, 8, 0.0);
        double sigmaE = Sigma * Math.Sqrt(2 + 4.0 / 8);
        double sigma1 = Sigma * Math.Sqrt(1 + 1.0 / 8);
        double bOurs = GateA.RmsMeanFactor(2, 8, 1, 8);
        double bChen = GateA.RmsMeanFactor(1, 8, 1, 8);
        int to = Warm + Periods;

        var br0 = new double[Reps];
        var brO = new double[Reps];
        var brC = new double[Reps];
        var sigO = new double[Reps];
        var sigC = new double[Reps];
        var rms1 = new double[Reps];
        var crossO = new double[Reps];
        var crossC = new double[Reps];
        var diff = new double[Reps];
        var exO = new double[Reps];
        var exC = new double[Reps];
        double Cross(double[] qz, double[] q0)
        {
            var delta = qz.Select((x, t) => (x - q0[t]) / zSafety).ToArray();
            return (Descriptive.Variance(qz, Warm, to) - Descriptive.Variance(q0, Warm, to)
                - zSafety * zSafety * Descriptive.Variance(delta, Warm, to)) / Var;
        }

        for (int rep = 0; rep < Reps; rep++)
        {
            var d = GateA.Demand(rep, to, 100, 0, Sigma);
            var r0 = SerialChain.Run(zero, d);
            var rO = SerialChain.Run(ours, d);
            var rC = SerialChain.Run(chen, d);
            br0[rep] = r0.Bullwhip(0, Var);
            brO[rep] = rO.Bullwhip(0, Var);
            brC[rep] = rC.Bullwhip(0, Var);
            sigO[rep] = rO.MeanSigmaHat[0];
            sigC[rep] = rC.MeanSigmaHat[0];
            rms1[rep] = rC.ProtectionErrorRms[0];
            crossO[rep] = Cross(rO.Orders[0], r0.Orders[0]);
            crossC[rep] = Cross(rC.Orders[0], r0.Orders[0]);
            diff[rep] = brO[rep] - brC[rep];
            exO[rep] = brO[rep] - br0[rep];
            exC[rep] = brC[rep] - br0[rep];
        }

        double z = GateA.BonferroniZ(5);
        bool ok = Check("V4b mean σ̂ realised-error (ours)", bOurs * sigmaE, sigO, z);
        ok &= Check("V4b mean σ̂ Chen Eq. 3", bChen * sigmaE, sigC, z);
        ok &= Check("V4b Chen one-period error RMS", sigma1, rms1, z);
        foreach (var (name, x) in new[] { ("ours", crossO), ("Chen", crossC) })
        {
            var (m, se) = GateA.MeanAndSe(x);
            bool zeroOk = Math.Abs(m) <= z * se;
            ok &= zeroOk;
            _out.WriteLine($"V4b cross term {name}: sim={m:+0.000000;-0.000000} ref=0 se={se:0.000000} |sim| ≤ {z:0.00}·se {(zeroOk ? "PASS" : "FAIL")}");
        }

        _out.WriteLine($"V4b b(ours, L-period errors)={bOurs:0.00000} b(Chen, one-period errors)={bChen:0.00000} σ_e={sigmaE:0.0000} C={ChenFormulas.SigmaConstantIid(2, 8):0.00000}");
        var (m0, se0) = GateA.MeanAndSe(br0);
        var (mO, seO) = GateA.MeanAndSe(brO);
        var (mC, seC) = GateA.MeanAndSe(brC);
        var (mD, seD) = GateA.MeanAndSe(diff);
        var (mEO, seEO) = GateA.MeanAndSe(exO);
        var (mEC, seEC) = GateA.MeanAndSe(exC);
        _out.WriteLine($"V4b BR z=0 {m0:0.000000} (se {se0:0.000000}); bound {bound:0.000000}; floor 0.98·bound = {0.98 * bound:0.000000}");
        _out.WriteLine($"V4b BR z=1 ours {mO:0.000000} (se {seO:0.000000}), excess over z=0 {mEO:0.000000} (paired se {seEO:0.000000})");
        _out.WriteLine($"V4b BR z=1 Chen {mC:0.000000} (se {seC:0.000000}), excess over z=0 {mEC:0.000000} (paired se {seEC:0.000000})");
        _out.WriteLine($"V4b BR ours − Chen = {mD:+0.000000;-0.000000} (paired se {seD:0.000000}, {mD / seD:+0.0;-0.0} se) = {mD / mC:+0.000%;-0.000%} of BR, {mD / mEC:+0.0%;-0.0%} of Chen's excess");

        // V4 (one-sided, Chen Eq. 5) for both estimators, and a positive excess z²·Var(Δσ̂) for both.
        Assert.True(mO >= 0.98 * bound && mC >= 0.98 * bound);
        Assert.True(mEO > z * seEO && mEC > z * seEC);
        Assert.True(ok);
    }

    [Fact]
    public void V7c_LongWindowExcessMatchesChen()
    {
        // p = 400, L = 2 (Lp = 2), iid: BR − 1 = 2L/p + 2L²/p² = 0.01005 (Chen Eq. 5, ρ = 0). With Var(D) = σ² as the
        // denominator the replication noise of BR (±0.03 per replication) is three times BR − 1, so BR − 1 is measured
        // on the same demand: x = [Var̂(q¹) − Var̂(D)]/σ² over the window. E[x] = BR − 1 − O(L/n²) (≈ 1e−6, 0.01 %).
        // Tolerance 2 % of BR − 1 (2.0e−4 on BR) and the Bonferroni CI (one point). Lp ± 1 gives 0.00501 / 0.01511
        // (−50 % / +50 %), 25 times the tolerance, so a lead-time error of one unit fails this check.
        var cfg = Four() with { Window = 400 };
        int to = Warm + Periods;
        var x = new double[Reps];
        for (int rep = 0; rep < Reps; rep++)
        {
            var d = GateA.Demand(rep, to, 100, 0, Sigma);
            var res = SerialChain.Run(cfg, d);
            x[rep] = (Descriptive.Variance(res.Orders[0], Warm, to) - Descriptive.Variance(d, Warm, to)) / Var;
        }

        double z = GateA.BonferroniZ(1);
        double reference = ChenFormulas.SingleStageBullwhipRatio(2, 400, 0.0) - 1.0;
        bool ok = Check("V7c p=400 stage 1 BR − 1", reference, x, z);
        var (mean, se) = GateA.MeanAndSe(x);
        foreach (int lp in new[] { 1, 3 })
        {
            double wrong = ChenFormulas.SingleStageBullwhipRatio(lp, 400, 0.0) - 1.0;
            bool wrongPasses = GateA.Passes(wrong, mean, se, z);
            _out.WriteLine($"V7c Lp={lp}: ref={wrong:0.000000} sim/ref−1={mean / wrong - 1:+0.0%;-0.0%} → {(wrongPasses ? "would PASS (not distinguishable)" : "FAILS (distinguishable)")}");
            Assert.False(wrongPasses);
        }

        Assert.True(ok);
    }

    [Fact]
    public void V7d_RealMeanLeadTime_P8_MatchesChenEq5() => RealMeanLeadTimeCheck(8);

    [Fact]
    public void V7d_RealMeanLeadTime_P16_MatchesChenEq5() => RealMeanLeadTimeCheck(16);

    private void RealMeanLeadTimeCheck(int p)
    {
        // R = 1, z = 0, iid N(100, 10²), four stages in normal mode, the base-case Kaplan lag (LagBase) at every stage, and
        // planning L = E[L] of that pmf as computed in code (KaplanLeadTime.MeanLeadTime), so |E[L] − L| ≤ 1e−9 holds by
        // construction. Cover: S = L·μ̂ with the real L (R = 1, Lp = L). Orders do not depend on the arrivals (IP is
        // unchanged by a receipt; V7b), so q_t = D_t + L(μ̂_t − μ̂_{t−1}) = (1 + L/p)D_t − (L/p)D_{t−p}, and stage-1
        // BR = 1 + 2L/p + 2L²/p² for any real L (Chen Eq. 5, ρ = 0). Reference at L = 6.4: p = 8 → 1 + 1.6 + 1.28 = 3.88;
        // p = 16 → 1 + 0.8 + 0.32 = 2.12. The pmf's E[L] is 7.8e−7 below 6.4, which moves BR by (2/p + 4L/p²)·ΔL = 5.1e−7
        // (p = 8) or 1.8e−7 (p = 16), below 1e−4 of the standard error. Rounding the planning L to 6 or 7 gives −6.6 % / +10.3 %
        // (p = 8) and −4.2 % / +6.5 % (p = 16); the check must reject both (as V7c does for Lp ± 1).
        // Second point: the realised error window for the non-integer Lp (ProtectionWindow) at the retailer, whose input is
        // iid. e = Σ a_i·D_{t0+i} − Lp·μ̂(t0) with Σ a_i = Σ a_i² = Lp has E[e²] = σ²(Lp + Lp²/p) exactly (Lp from the pmf).
        // The statistic is the mean square ProtectionErrorRms² (unbiased; the RMS has a Jensen bias). Plan pass rule
        // (2 % and the Bonferroni CI over the 2 points). Weighting the boundary period by its coverage f would give
        // σ²(n + f² + Lp²/p), −2.08 % (p = 8) / −2.68 % (p = 16); printed only.
        var lt = new KaplanLeadTime(LagBase);
        double planningL = lt.MeanLeadTime;
        double cv = Math.Sqrt(lt.LagVariance) / lt.MeanLag;
        _out.WriteLine($"V7d p={p}: base lag m={lt.MaxLag}, Σ={LagBase.Sum():0.000000000000000}, E[T]={lt.MeanLag:0.000000000}, E[L]={planningL:0.000000000} (6.4 − E[L] = {6.4 - planningL:E2}), CV(T)={cv:0.0000}");
        Assert.True(Math.Abs(planningL - 6.4) < 1e-6);

        var cfg = Four(lt) with { LeadTimes = new[] { planningL, planningL, planningL, planningL }, Window = p };
        int to = Warm + Periods;
        var br = new double[Reps];
        var ms = new double[Reps];
        for (int rep = 0; rep < Reps; rep++)
        {
            var res = SerialChain.Run(cfg with { LeadTimeSeed = GateA.LeadTimeSeed(rep) }, GateA.Demand(rep, to, 100, 0, Sigma));
            AssertConservation(res);
            br[rep] = res.Bullwhip(0, Var);
            ms[rep] = res.ProtectionErrorRms[0] * res.ProtectionErrorRms[0];
        }

        double z = GateA.BonferroniZ(2);
        double chen = ChenFormulas.SingleStageBullwhipRatio(6.4, p, 0.0);
        _out.WriteLine($"V7d p={p}: Chen Eq. 5 at L = 6.4: {chen:0.000000}; at the planning L: {ChenFormulas.SingleStageBullwhipRatio(planningL, p, 0.0):0.000000}");
        bool ok = Check($"V7d p={p} stage 1 BR vs Chen Eq. 5 (L = 6.4)", chen, br, z);
        double msRef = Var * (planningL + planningL * planningL / p);
        ok &= Check($"V7d p={p} retailer protection-error mean square vs σ²(Lp + Lp²/p)", msRef, ms, z);

        var (mean, se) = GateA.MeanAndSe(br);
        foreach (double wrongL in new[] { 6.0, 7.0 })
        {
            double wrong = ChenFormulas.SingleStageBullwhipRatio(wrongL, p, 0.0);
            bool wrongPasses = GateA.Passes(wrong, mean, se, z);
            _out.WriteLine($"V7d p={p} planning L={wrongL}: ref={wrong:0.000000} sim/ref−1={mean / wrong - 1:+0.0%;-0.0%} → {(wrongPasses ? "would PASS (not distinguishable)" : "FAILS (distinguishable)")}");
            Assert.False(wrongPasses);
        }

        int n = (int)Math.Floor(planningL);
        double f = planningL - n;
        double linear = Var * (n + f * f + planningL * planningL / p);
        var (msMean, msSe) = GateA.MeanAndSe(ms);
        _out.WriteLine($"V7d p={p} boundary weight f (linear rule) would give {linear:0.000} ({linear / msRef - 1:+0.00%;-0.00%}); sim {msMean:0.000} is {(msMean - linear) / msSe:+0.0;-0.0} se from it; weights {Fmt(ProtectionWindow.Weights(planningL).Skip(n - 1))}");
        Assert.True(ok);
    }

    [Fact]
    public void V13b_CostSideTwoPointsBonferroni()
    {
        // PerfectSupplier, R = 1, known σ (plan V13): NS = z·σ·√L − e, e ~ N(0, σ_e²), σ_e = σ√(L + L²/p), exactly. So
        // E[backlog] = σ_e·G(k), E[on-hand] = z·σ·√L + E[backlog], k = z·σ·√L/σ_e. Two points (the V13 point and
        // L = 3, p = 4, z = 0.5), backlog and on-hand each: plan pass rule with Bonferroni over 4.
        double zB = GateA.BonferroniZ(4);
        bool ok = true;
        foreach (var (l, p, zs) in new[] { (2, 8, 1.0), (3, 4, 0.5) })
        {
            var cfg = new ChainConfig
            {
                LeadTimes = new double[] { l }, Window = p, PerfectSupplier = true, Safety = SafetySource.KnownSigma,
                KnownSigma = Sigma, Z = new[] { zs }, InitialMean = 100, Warmup = Warm,
            };
            var bl = new double[Reps];
            var oh = new double[Reps];
            for (int rep = 0; rep < Reps; rep++)
            {
                var res = SerialChain.Run(cfg, GateA.Demand(rep, Warm + Periods, 100, 0, Sigma));
                bl[rep] = res.MeanBacklog[0];
                oh[rep] = res.MeanOnHand[0];
            }

            double sigmaE = Sigma * Math.Sqrt(l + (double)l * l / p);
            double safety = zs * Sigma * Math.Sqrt(l);
            double expBl = sigmaE * GateA.NormalLoss(safety / sigmaE);
            ok &= Check($"V13b L={l} p={p} z={zs} E[backlog]", expBl, bl, zB);
            ok &= Check($"V13b L={l} p={p} z={zs} E[on-hand]", safety + expBl, oh, zB);
        }

        Assert.True(ok);
    }

    [Fact]
    public void CapitalCost_ConstantDemand_MatchesRateValueStock()
    {
        // Convention (SerialChain, measurement block): for every period t of the window (t ≥ Warmup, Periods of them)
        // capital[k] += CapitalRate · Value[k] · (max(0, on-hand_k) + pipeline_k), pipeline_k = items dispatched to stage k
        // and not yet arrived at the end of t, excluding those arriving at t+1. CapitalCost is the SUM over the window
        // (not a per-period mean): CapitalCost[k] = r · v_k · (mean on-hand + mean pipeline) · Periods.
        // Constant demand μ = 100, L = 2 at all four stages, R = 1, normal mode, no clipping, InitialMean = μ:
        //   a) z = 0: every stage orders q = μ, S = Lp·μ = 2μ = IP, every arrival (μ) is shipped on arrival, so on-hand = 0
        //      and the pipeline is the shipment of period t that is due at t+2: μ·(L − 1) = 100 for every stage
        //      (the top stage: its own order). Expected CapitalCost[k] = r·v_k·(0 + 100)·Periods.
        //   b) KnownSigma σ = 10, z = 1: S = Lp·μ + z·σ·√Lp, so the steady on-hand is the safety term z·σ·√2 = 14.1421 at
        //      every stage (the stage above ships μ per period, the order is μ per period) and the pipeline stays 100.
        //      Expected CapitalCost[k] = r·v_k·(14.1421 + 100)·Periods after the one-off build-up of the safety stock.
        // Deterministic, so the tolerance is 1e-9 relative (floating-point accumulation only).
        const double rate = 0.05 / 52.0;
        var value = new[] { 10.0, 20.0, 30.0, 40.0 };
        var demand = Enumerable.Repeat(100.0, Warm + Periods).ToArray();
        double safety = 1.0 * Sigma * Math.Sqrt(2.0);
        foreach (var (name, cfg, onHand) in new[]
        {
            ("a) z=0", Four() with { Value = value, CapitalRate = rate }, 0.0),
            ("b) known σ, z=1", Four() with
            {
                Value = value, CapitalRate = rate, Safety = SafetySource.KnownSigma, KnownSigma = Sigma, Z = new[] { 1.0, 1.0, 1.0, 1.0 },
            }, safety),
        })
        {
            var res = SerialChain.Run(cfg, demand);
            for (int k = 0; k < 4; k++)
            {
                double expected = rate * value[k] * (onHand + 100.0) * Periods;
                _out.WriteLine($"Capital {name} stage {k + 1}: sim={res.CapitalCost[k]:0.000000} ref={expected:0.000000} on-hand={res.MeanOnHand[k]:0.0000} pipeline={res.MeanPipeline[k]:0.0000}");
                Assert.True(Math.Abs(res.MeanOnHand[k] - onHand) <= 1e-9 * 100, $"{name} stage {k + 1} on-hand");
                Assert.True(Math.Abs(res.MeanPipeline[k] - 100.0) <= 1e-9 * 100, $"{name} stage {k + 1} pipeline");
                Assert.True(Math.Abs(res.CapitalCost[k] / expected - 1.0) <= 1e-9, $"{name} stage {k + 1} capital cost");
            }
        }

        // Zero rate gives zero cost.
        var zero = SerialChain.Run(Four() with { Value = value }, demand);
        Assert.All(zero.CapitalCost, c => Assert.Equal(0.0, c));
    }

    [Fact]
    public void CapitalCost_IidDemand_IdentityAndPipelineByFlowConservation()
    {
        // iid N(100, 10²), L = 2, R = 1, z = 0, four stages, normal mode. The on-hand is E[max(0, net stock)] with no
        // closed form here, so it is not asserted. Two derivable parts:
        //   1) identity: CapitalCost[k] = r·v_k·(MeanOnHand[k] + MeanPipeline[k])·Periods, exact in every replication
        //      (relative 1e-9; both sides are sums of the same terms).
        //   2) the pipeline of stage k at the end of t is the shipment of stage k+1 in t (due at t+2; top stage: its own
        //      order), so its window mean is the mean flow μ = 100 (Little: μ·(L − 1)) up to the backlog drift over the
        //      window. Checked per stage with the suite rule (2 % and the Bonferroni CI over 4 stages).
        const double rate = 0.05 / 52.0;
        var value = new[] { 10.0, 20.0, 30.0, 40.0 };
        var cfg = Four() with { Value = value, CapitalRate = rate };
        var pipe = new double[4][];
        for (int k = 0; k < 4; k++)
        {
            pipe[k] = new double[Reps];
        }

        for (int rep = 0; rep < Reps; rep++)
        {
            var res = SerialChain.Run(cfg, GateA.Demand(rep, Warm + Periods, 100, 0, Sigma));
            for (int k = 0; k < 4; k++)
            {
                double expected = rate * value[k] * (res.MeanOnHand[k] + res.MeanPipeline[k]) * Periods;
                Assert.True(Math.Abs(res.CapitalCost[k] / expected - 1.0) <= 1e-9, $"rep {rep} stage {k + 1}");
                pipe[k][rep] = res.MeanPipeline[k];
            }
        }

        double z = GateA.BonferroniZ(4);
        bool ok = true;
        for (int k = 0; k < 4; k++)
        {
            ok &= Check($"Capital iid stage {k + 1} mean pipeline", 100.0, pipe[k], z);
        }

        Assert.True(ok);
    }
}
