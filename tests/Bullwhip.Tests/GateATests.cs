using Bullwhip.Core.Analytic;
using Bullwhip.Core.Chain;
using Bullwhip.Core.Demand;
using Bullwhip.Core.Stats;
using Bullwhip.Core.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Bullwhip.Tests;

/// <summary>Gate A: V2–V9, V12, V12b–V12d and V13–V15 (plan §5).</summary>
public class GateATests
{
    private readonly ITestOutputHelper _out;

    public GateATests(ITestOutputHelper output)
    {
        _out = output;
    }

    private static ChainConfig Four(bool info = false) => new()
    {
        LeadTimes = new double[] { 2, 2, 2, 2 },
        Window = 8,
        InitialMean = 100,
        Warmup = 200,
        InfoSharing = info,
    };

    private void Table(string name, double[] refs, double[] mean, double[] se, double z, bool[] ok)
    {
        for (int i = 0; i < refs.Length; i++)
        {
            _out.WriteLine($"{name} stage {i + 1}: ref={refs[i],8:0.0000} sim={mean[i],8:0.0000} rel={mean[i] / refs[i] - 1,8:+0.00%;-0.00%} se={se[i],7:0.0000} z={z:0.00} {(ok[i] ? "PASS" : "FAIL")}");
        }
    }

    private void CheckStages(string name, double[] refs, double[] mean, double[] se)
    {
        double z = GateA.BonferroniZ(refs.Length);
        var ok = refs.Select((r, i) => GateA.Passes(r, mean[i], se[i], z)).ToArray();
        Table(name, refs, mean, se, z, ok);
        Assert.True(ok.All(x => x), $"{name}: a stage failed");
    }

    [Fact]
    public void V2_Decentralised()
    {
        var (mean, se) = GateA.BullwhipPerStage(Four(), 10, 0, 100, 2000, 200);
        var refs = Enumerable.Range(1, 4).Select(k => GateA.FilterReference(k, 2, 8)).ToArray();
        CheckStages("V2", refs, mean, se);
        double z = GateA.BonferroniZ(4);

        // Stage 1 two-sided against Chen Eq. (5) (ChenFormulas, independent of FilterReference): |BR/ref − 1| ≤ 2 % and
        // ref inside the Bonferroni CI. Same point as stage 1 above, so m stays 4.
        double chen1 = ChenFormulas.SingleStageBullwhipRatio(2, 8, 0.0);
        bool ok1 = GateA.Passes(chen1, mean[0], se[0], z);
        _out.WriteLine($"V2 stage 1 vs Chen Eq. (5): sim={mean[0]:0.000000} ref={chen1:0.000000} rel={mean[0] / chen1 - 1:+0.000%;-0.000%} se={se[0]:0.000000} tol=2% & {z:0.00}·se {(ok1 ? "PASS" : "FAIL")}");
        Assert.True(ok1, "V2 stage 1 outside the two-sided band around Chen Eq. (5)");

        for (int k = 1; k <= 4; k++)
        {
            double bound = GateA.ChenProductBound(k, 2, 8);
            _out.WriteLine($"V2 stage {k}: product bound {bound:0.0000}");
            Assert.True(mean[k - 1] >= bound - z * se[k - 1], $"stage {k} below Chen product bound");
        }
    }

    [Fact]
    public void V3_InfoSharing()
    {
        var (mean, se) = GateA.BullwhipPerStage(Four(true), 10, 0, 100, 2000, 200);
        var refs = Enumerable.Range(1, 4)
            .Select(k => k == 1 ? GateA.FilterReference(1, 2, 8) : GateA.ChenCentralised(k, 2, 8)).ToArray();
        CheckStages("V3", refs, mean, se);
    }

    private bool Check(string name, double reference, double[] values, double z)
    {
        var (mean, se) = GateA.MeanAndSe(values);
        bool ok = GateA.Passes(reference, mean, se, z);
        _out.WriteLine($"{name}: sim={mean:0.000000} ref={reference:0.000000} rel={mean / reference - 1:+0.000%;-0.000%} se={se:0.000000} tol=2% & |sim−ref| ≤ {z:0.00}·se {(ok ? "PASS" : "FAIL")}");
        return ok;
    }

    [Fact]
    public void V4_SafetyStockBoundThreeEstimators()
    {
        // One stage, L = 2, p = 8, R = 1, z = 1, iid N(100, 10²), 100 reps × 2000 periods after 200 warm-up, CRN: every row
        // and a z = 0 run use the same demand. Statistic: BR at z = 1 (as in the one-sided V4 before). Rows:
        // (a) our realised-error σ̂ (EstimatedError, the default): RMS of the last p L-period errors;
        // (b) Chen, Ryan & Simchi-Levi (2000), NRL p. 273, Eq. (5): σ̂ = K_L·Δ̂, K_L = c·A, c = √(π/2) (paper), A = σ_eL/σ_e
        //     our derivation (NrlFormulas), at α_s = 2/(p + 1) (Δ̂ has the variance of a p-sample mean for iid input:
        //     α/(2 − α) = 1/p) and at α_s = 0.05 (sensitivity);
        // (c) Chen et al. (2000), MS Eq. (3), p. 437, with the derived C (DECISIONS C13): the earlier comparison.
        // Decomposition (MS p. 438; NRL Eq. 7, p. 273): Var(q_z) = Var(q_0) + z²·Var(Δσ̂) + 2z·Cov(q_0, Δσ̂), Δσ̂_t = σ̂_t −
        // σ̂_{t−1}; Cov = 0 by the symmetry D − μ → μ − D, which flips q_0 − μ and leaves σ̂ unchanged (MS Lemma 2.1 for Eq. 3,
        // NRL Lemma 1 for an ES forecast; the same argument covers (a) and (b) on the MA); E[Var(q_0)] = bound·σ² (MS Eq. 5,
        // tight at z = 0).
        // X = z²·Var̂(Δσ̂)/σ² is measured per replication from Δσ̂ = (q_z − q_0)/z (CRN, no clipping: q_t = S_t − S_{t−1} + D_t
        // and μ̂ does not depend on z). For (b) X also has a closed form (GateA.EwmaMadVariance), so BR_b has the two-sided
        // reference bound + X_pred. BR_a − BR_b = X_a − X_b + (cross_a − cross_b): the derived tolerance is
        // |(BR_a − BR_b) − (X_a − X_b)| ≤ z_B·paired se. A plain |BR_a − BR_b| ≤ z_B·se has the wrong null (X_a ≠ X_b; X_b
        // grows like 2α_s²/(2 − α_s)), so it is printed, not asserted.
        // Bonferroni over m = 14: BR ≥ bound − z_B·se per row (4); (a), (c) BR − X vs bound (2); per (b) row BR vs bound +
        // X_pred, mean σ̂ vs A·σ_e (Δ̂ is linear in |e|: no Jensen bias in the mean), X vs X_pred (6); (a) − (b) (2). The
        // one-sided floor BR ≥ 0.98·bound of the plan stays for every row.
        const double zS = 1.0;
        const int lead = 2, window = 8;
        int reps = GateA.Replications, warm = GateA.Warmup, to = GateA.Warmup + GateA.Periods;
        double demandVar = GateA.Sigma * GateA.Sigma;
        double bound = ChenFormulas.SingleStageBullwhipRatio(lead, window, 0.0);
        double sigmaEL = GateA.Sigma * Math.Sqrt(lead + (double)lead * lead / window); // A·σ_e = σ_eL
        double alphaP = 2.0 / (window + 1);
        var zero = new ChainConfig { LeadTimes = new double[] { lead }, Window = window, Z = new[] { 0.0 }, InitialMean = 100, Warmup = warm };
        var ours = zero with { Z = new[] { zS } };
        var ewma = ours with { Safety = SafetySource.EwmaAbsError };
        var rows = new (string Name, ChainConfig Cfg, double Alpha, double MeanFactor)[]
        {
            ("(a) realised-error RMS ", ours, 0.0, GateA.RmsMeanFactor(lead, window, 1, window)),
            ("(b) NRL EWMA α_s=2/9   ", ewma with { AbsErrorSmoothing = alphaP }, alphaP, 1.0),
            ("(b) NRL EWMA α_s=0.05  ", ewma with { AbsErrorSmoothing = 0.05 }, 0.05, 1.0),
            ("(c) Chen Eq. 3, C deriv", ours with { Safety = SafetySource.ChenOnePeriodError }, 0.0, GateA.RmsMeanFactor(1, window, 1, window)),
        };

        int nr = rows.Length;
        double[][] New() => Enumerable.Range(0, nr).Select(_ => new double[reps]).ToArray();
        var br = New();
        var x = New();
        var sig = New();
        var rmsSig = New();
        var stockout = New();
        var rmsL = new double[reps];
        var rms1 = new double[reps];
        for (int rep = 0; rep < reps; rep++)
        {
            var d = GateA.Demand(rep, to, GateA.Mu, 0, GateA.Sigma);
            var q0 = SerialChain.Run(zero, d).Orders[0];
            double ss1 = 0;
            for (int t = warm; t < to; t++)
            {
                double e1 = d[t] - Enumerable.Range(t - window, window).Sum(i => d[i]) / window;
                ss1 += e1 * e1;
            }

            rms1[rep] = Math.Sqrt(ss1 / (to - warm));
            for (int i = 0; i < nr; i++)
            {
                var res = SerialChain.Run(rows[i].Cfg, d);
                var delta = res.Orders[0].Select((v, t) => (v - q0[t]) / zS).ToArray();
                br[i][rep] = res.Bullwhip(0, demandVar);
                x[i][rep] = zS * zS * Descriptive.Variance(delta, warm, to) / demandVar;
                sig[i][rep] = res.MeanSigmaHat[0];
                stockout[i][rep] = 1 - res.ReadyRate;

                // σ̂_t over the window = mean σ̂ + (C_t − mean C), C_t = Σ_{s ≤ t} Δσ̂_s, so mean σ̂² = (mean σ̂)² + Var(C).
                double c = 0, sc = 0, scc = 0;
                for (int t = warm; t < to; t++)
                {
                    c += delta[t];
                    sc += c;
                    scc += c * c;
                }

                double mc = sc / (to - warm);
                rmsSig[i][rep] = Math.Sqrt(sig[i][rep] * sig[i][rep] + scc / (to - warm) - mc * mc);
                if (i == 0)
                {
                    rmsL[rep] = res.ProtectionErrorRms[0];
                }
                else
                {
                    // (b), (c): the simulator's one-period errors are e_t = D_t − MA_p(D_{t−p..t−1}), the same for every row.
                    Assert.True(Math.Abs(res.ProtectionErrorRms[0] / rms1[rep] - 1) < 1e-9, $"{rows[i].Name}: one-period errors differ");
                }
            }
        }

        double zB = GateA.BonferroniZ(14);
        var (m1, se1) = GateA.MeanAndSe(rms1);
        var (mL, seL) = GateA.MeanAndSe(rmsL);
        _out.WriteLine($"V4 L=2 p=8 z=1: bound (MS Eq. 5) {bound:0.000000}, floor 0.98·bound {0.98 * bound:0.000000}, z_B(14)={zB:0.000}; A={NrlFormulas.ErrorRatioIid(lead, window):0.00000} K_L=c·A={NrlFormulas.KFactorIid(lead, window):0.00000} C={ChenFormulas.SigmaConstantIid(lead, window):0.00000}; A·σ_e = σ_eL = {sigmaEL:0.0000}");
        _out.WriteLine($"V4 one-period error RMS (every row, CRN) {m1:0.0000} (se {se1:0.0000}) vs σ√(1+1/p) = {GateA.Sigma * Math.Sqrt(1 + 1.0 / window):0.0000}; row (a) L-period error RMS {mL:0.0000} (se {seL:0.0000}) vs σ_eL {sigmaEL:0.0000}");
        _out.WriteLine("V4 row                     mean σ̂ (se)         σ̂/σ_eL  exp      RMS σ̂/σ_eL exp     1-per RMS  BR z=1 (se)          bound     X (se)               BR − X (se)          stockout (1−Φ(z)=0.1587)");
        bool ok = true;
        var xPred = new double[nr];
        for (int i = 0; i < nr; i++)
        {
            var (mB, seB) = GateA.MeanAndSe(br[i]);
            var (mX, seX) = GateA.MeanAndSe(x[i]);
            var (mS, seS) = GateA.MeanAndSe(sig[i]);
            var bx = br[i].Select((v, rep) => v - x[i][rep]).ToArray();
            var (mBx, seBx) = GateA.MeanAndSe(bx);
            double expRms = 1.0;
            if (rows[i].Alpha > 0)
            {
                var (level, step) = GateA.EwmaMadVariance(window, rows[i].Alpha);
                xPred[i] = zS * zS * Math.PI / 2 * (lead + (double)lead * lead / window) * step;
                expRms = Math.Sqrt(1 + Math.PI / 2 * level);
            }

            _out.WriteLine($"V4 {rows[i].Name}: {mS:0.0000} ({seS:0.0000})  {mS / sigmaEL:0.00000} {rows[i].MeanFactor:0.00000}  {rmsSig[i].Average() / sigmaEL:0.00000}  {expRms:0.00000}  {m1:0.0000}   {mB:0.000000} ({seB:0.000000})  {bound:0.000000}  {mX:0.000000} ({seX:0.000000})  {mBx:0.000000} ({seBx:0.000000})  {stockout[i].Average():0.0000}");
            bool floor = mB >= 0.98 * bound;
            bool holds = mB >= bound - zB * seB;
            _out.WriteLine($"V4 {rows[i].Name}: floor BR ≥ 0.98·bound {(floor ? "PASS" : "FAIL")}; bound holds BR ≥ bound − {zB:0.00}·se ({bound - zB * seB:0.000000}) {(holds ? "PASS" : "FAIL")}");
            ok &= floor && holds;
            if (rows[i].Alpha > 0)
            {
                ok &= Check($"V4 {rows[i].Name}: BR vs bound + X_pred (X_pred {xPred[i]:0.000000})", bound + xPred[i], br[i], zB);
                ok &= Check($"V4 {rows[i].Name}: mean σ̂ vs A·σ_e", sigmaEL, sig[i], zB);
                ok &= Check($"V4 {rows[i].Name}: X vs X_pred", xPred[i], x[i], zB);
            }
            else
            {
                ok &= Check($"V4 {rows[i].Name}: BR − X vs bound", bound, bx, zB);
            }
        }

        for (int i = 1; i <= 2; i++)
        {
            var raw = br[0].Select((v, rep) => v - br[i][rep]).ToArray();
            var adj = raw.Select((v, rep) => v - (x[0][rep] - x[i][rep])).ToArray();
            var (mR, seR) = GateA.MeanAndSe(raw);
            var (mA, seA) = GateA.MeanAndSe(adj);
            bool diffOk = Math.Abs(mA) <= zB * seA;
            ok &= diffOk;
            _out.WriteLine($"V4 (a) − {rows[i].Name.Trim()}: BR_a − BR_b = {mR:+0.000000;-0.000000} (paired se {seR:0.000000}, {mR / seR:+0.0;-0.0} se; predicted X_a − X_pred = {x[0].Average() - xPred[i]:+0.000000;-0.000000}); plain |Δ| ≤ {zB:0.00}·se {(Math.Abs(mR) <= zB * seR ? "holds" : "does not hold")} (not asserted)");
            _out.WriteLine($"V4 (a) − {rows[i].Name.Trim()}: (BR_a − BR_b) − (X_a − X_b) = {mA:+0.000000;-0.000000} (paired se {seA:0.000000}) |·| ≤ {zB:0.00}·se {(diffOk ? "PASS" : "FAIL")}");
        }

        Assert.True(ok);
    }

    [Fact]
    public void V5_ZeroVariance()
    {
        var cfg = Four() with { Warmup = 50 };
        var res = SerialChain.Run(cfg, Enumerable.Repeat(100.0, 500).ToArray());
        for (int k = 0; k < 4; k++)
        {
            double v = Descriptive.Variance(res.Orders[k], 50, 500);
            _out.WriteLine($"V5 stage {k + 1}: var={v:E2} backlog={res.MeanBacklog[k]:E2} onhand={res.MeanOnHand[k]:0.000000} end={res.OnHandEnd[k]:0.000000}");
            Assert.True(v < 1e-9);
            Assert.True(Math.Abs(res.MeanBacklog[k]) < 1e-9);
            Assert.True(Math.Abs(res.MeanOnHand[k] - res.OnHandEnd[k]) < 1e-9);
        }
    }

    [Fact]
    public void V6_Conservation()
    {
        var cfg = Four() with { Warmup = 1000 };
        var res = SerialChain.Run(cfg, GateA.Demand(0, 20000, 100, 0, 10));
        double lhs = 0, rhs = 0;
        for (int k = 0; k < 4; k++)
        {
            double oh = res.ArrivedTotal[k] - res.ShippedTotal[k];
            double bl = res.IncomingTotal[k] - res.ShippedTotal[k];
            double mo = Descriptive.Mean(res.Orders[k], 1000, 20000);
            _out.WriteLine($"V6 stage {k + 1}: onhand {res.OnHandEnd[k]:0.000000} vs {oh:0.000000}; backlog {res.BacklogEnd[k]:0.000000} vs {bl:0.000000}; mean order {mo:0.000}");
            Assert.True(Math.Abs(res.OnHandEnd[k] - oh) <= 1e-9 * Math.Max(1, res.ArrivedTotal[k]));
            Assert.True(Math.Abs(res.BacklogEnd[k] - bl) <= 1e-9 * Math.Max(1, res.IncomingTotal[k]));
            Assert.True(Math.Abs(mo / 100 - 1) <= 0.01);
            lhs += res.OnHandEnd[k] + res.ScheduledEnd[k] - res.ScheduledInitial[k];
            rhs += res.ScheduledAdded[k] - res.ShippedTotal[k];
        }

        _out.WriteLine($"V6 system: {lhs:0.000000} vs {rhs:0.000000}");
        Assert.True(Math.Abs(lhs - rhs) <= 1e-9 * Math.Max(1, res.ArrivedTotal.Sum()));
    }

    [Fact]
    public void V7_PerfectSupplierInvariance()
    {
        double maxDiff = 0;
        for (int rep = 0; rep < 5; rep++)
        {
            var d = GateA.Demand(rep, 2200, 100, 0, 10);
            var a = SerialChain.Run(Four(), d);
            var b = SerialChain.Run(Four() with { PerfectSupplier = true }, d);
            for (int k = 0; k < 4; k++)
            {
                for (int t = 0; t < d.Length; t++)
                {
                    maxDiff = Math.Max(maxDiff, Math.Abs(a.Orders[k][t] - b.Orders[k][t]));
                }
            }
        }

        _out.WriteLine($"V7 max |q_normal - q_perfect| = {maxDiff:E2}");
        Assert.True(maxDiff < 1e-9);

        var cfg = Four() with { Window = 400 };
        var (mean, _) = GateA.BullwhipPerStage(cfg, 10, 0, 20, 2000, 200);
        _out.WriteLine($"V7 p=400 stage 1 BR = {mean[0]:0.0000}");
        Assert.True(mean[0] < 1.05);
    }

    [Fact]
    public void V8_StockoutTiming()
    {
        var cfg = new ChainConfig
        {
            LeadTimes = new double[] { 2 }, Window = 8, PerfectSupplier = true, Safety = SafetySource.KnownSigma,
            KnownSigma = 10, Z = new[] { 1.0 }, InitialMean = 100, Warmup = 200,
        };
        double lp = 2;
        double expected = 1 - Normal.Cdf(1.0 * Math.Sqrt(lp) / Math.Sqrt(lp + lp * lp / 8));
        double sum = 0;
        for (int r = 0; r < 20; r++)
        {
            sum += 1 - SerialChain.Run(cfg, GateA.Demand(r, 2200, 100, 0, 10)).ReadyRate;
        }

        double sim = sum / 20;
        _out.WriteLine($"V8 stockout sim={sim:0.0000} expected={expected:0.0000}");
        Assert.True(Math.Abs(sim - expected) <= 0.01);
    }

    [Fact]
    public void V9_Batching()
    {
        var cfg = new ChainConfig
        {
            LeadTimes = new double[] { 1 }, Window = 4, R = 4, Phases = new[] { 0 }, Z = new[] { 0.0 },
            ConstantMean = 100, InitialMean = 100, OrderCost = 0,
        };
        var (mean, se) = GateA.BullwhipPerStage(cfg, 10, 0, 100, 2000, 200);
        double expected = 1 + 3 * 100.0 * 100.0 / 100.0;
        CheckStages("V9", new[] { expected }, mean, se);
    }

    [Fact]
    public void V12_NoPhantomSafetyStock()
    {
        var flat = Enumerable.Repeat(100.0, 1000).ToArray();
        foreach (var phases in new[] { new[] { 0, 0, 0, 0 }, new[] { 0, 1, 2, 3 } })
        {
            var cfg = Four() with { R = 4, Phases = phases };
            var a = SerialChain.Run(cfg with { Z = new[] { 0.0, 0, 0, 0 } }, flat);
            var b = SerialChain.Run(cfg with { Z = new[] { 2.0, 2, 2, 2 } }, flat);
            for (int k = 0; k < 4; k++)
            {
                _out.WriteLine($"V12 phases {string.Join("", phases)} stage {k + 1}: onhand z=0 {a.MeanOnHand[k]:0.000000} z=2 {b.MeanOnHand[k]:0.000000}");
                Assert.True(Math.Abs(a.MeanOnHand[k] - b.MeanOnHand[k]) < 1e-9);
            }
        }
    }

    [Fact]
    public void V12b_PhaseAwareCycleStock()
    {
        // R=4, L=2 (Lp=5), μ=100, z=0, constant demand (derivation in PhaseTarget). Upstream stage k gets one lump of
        // 400 at its customer's order times; the first after its own order time t0 lands at t0+a, a = ((φ_{k−1} − φ_k
        // − 1) mod 4) + 1, and the cycle stock is c·μ with c = (a − L) mod 4. Phases 0000: a=4, c=2 → 200. Phases 0123:
        // a=3, c=1 → 100. Phases 0321: a=1, n=2 lumps in the window, c=3 → 300. Retailer (smooth): (R−1)/2·μ = 150.
        // No backlog anywhere, and z=2 gives the same on-hand as z=0 (σ̂ = 0).
        var flat = Enumerable.Repeat(100.0, 1000).ToArray();
        var cases = new[] { (new[] { 0, 0, 0, 0 }, 200.0), (new[] { 0, 1, 2, 3 }, 100.0), (new[] { 0, 3, 2, 1 }, 300.0) };
        foreach (var (phases, upstream) in cases)
        {
            var cfg = Four() with { R = 4, Phases = phases };
            var a = SerialChain.Run(cfg with { Z = new[] { 0.0, 0, 0, 0 } }, flat);
            var b = SerialChain.Run(cfg with { Z = new[] { 2.0, 2, 2, 2 } }, flat);
            for (int k = 0; k < 4; k++)
            {
                double expected = k == 0 ? 150.0 : upstream;
                _out.WriteLine($"V12b phases {string.Join("", phases)} stage {k + 1}: onhand z=0 {a.MeanOnHand[k]:0.000000} z=2 {b.MeanOnHand[k]:0.000000} exp={expected:0.0} backlog={a.MeanBacklog[k]:0.000000}");
                Assert.True(Math.Abs(a.MeanOnHand[k] - expected) < 1e-9);
                Assert.True(Math.Abs(a.MeanBacklog[k]) < 1e-9);
                Assert.True(Math.Abs(a.MeanOnHand[k] - b.MeanOnHand[k]) < 1e-9);
            }
        }
    }

    private static readonly ChainConfig R4Retailer = new()
    {
        LeadTimes = new double[] { 2 }, Window = 8, R = 4, Phases = new[] { 0 }, Z = new[] { 1.0 }, InitialMean = 100,
        Warmup = 200,
    };

    private static readonly double SigmaE = 10 * Math.Sqrt(5 + 25.0 / 8);

    [Fact]
    public void V12c_ProtectionErrorRmsAtR4()
    {
        // iid N(100, 10²), R=4, L=2 (Lp=5), p=8: e = D(t0, t0+Lp] − Lp·μ̂(t0) has Var = σ²(Lp + Lp²/p), RMS 28.504
        // (plan V8). The bucket-weighted realised window gave σ√(4.25 + 25/8) = 27.157 (−4.7 %).
        // σ̂ = RMS of the last p = 8 order-time errors: E[σ̂²] = σ_e², E[σ̂] = b·σ_e, b = 0.9635 (GateA.RmsMeanFactor).
        double ss = 0;
        var rms = new double[20];
        var sig = new double[20];
        for (int r = 0; r < 20; r++)
        {
            var res = SerialChain.Run(R4Retailer, GateA.Demand(r, 2200, 100, 0, 10));
            rms[r] = res.ProtectionErrorRms[0];
            sig[r] = res.MeanSigmaHat[0];
            ss += rms[r] * rms[r] / 20;
        }

        double sim = Math.Sqrt(ss);
        var (rm, rse) = GateA.MeanAndSe(rms);
        var (sm, sse) = GateA.MeanAndSe(sig);
        double b = GateA.RmsMeanFactor(5, 8, 4, 8);
        _out.WriteLine($"V12c protection-period error RMS sim={sim:0.0000} (mean {rm:0.0000} se {rse:0.0000}) expected={SigmaE:0.0000} rel={sim / SigmaE - 1:+0.00%;-0.00%}");
        _out.WriteLine($"V12c mean σ̂ sim={sm:0.0000} se={sse:0.0000} expected b·σ_e={b * SigmaE:0.0000} (b={b:0.00000}) rel={sm / (b * SigmaE) - 1:+0.00%;-0.00%}; vs σ_e {sm / SigmaE - 1:+0.00%;-0.00%}");
        Assert.True(Math.Abs(sim / SigmaE - 1) <= 0.02);
        Assert.True(Math.Abs(sm / (b * SigmaE) - 1) <= 0.02);
    }

    [Fact]
    public void V12d_RetailerOnHandAtR4()
    {
        // Net stock at t0+j, j = L..Lp, is S − D(t0, t0+j] (unclipped orders), so E[NS] = (R−1)/2·μ + z·E[σ̂] exactly,
        // E[σ̂] = b·σ_e. Backlog only at j = Lp (j = Lp−1 needs e > z·σ̂ + μ): E[(e − z·σ̂)⁺]/R ≈ σ_e·G(z·b)/R, σ̂ at its
        // mean. Expected on-hand = 150 + 27.465 + 0.636 = 178.10.
        double b = GateA.RmsMeanFactor(5, 8, 4, 8);
        double expected = 150 + b * SigmaE + SigmaE * GateA.NormalLoss(b) / 4;
        double oh = 0, ns = 0, sig = 0;
        for (int r = 0; r < 20; r++)
        {
            var res = SerialChain.Run(R4Retailer, GateA.Demand(r, 2200, 100, 0, 10));
            oh += res.MeanOnHand[0] / 20;
            ns += (res.MeanOnHand[0] - res.MeanBacklog[0]) / 20;
            sig += res.MeanSigmaHat[0] / 20;
        }

        _out.WriteLine($"V12d retailer on-hand sim={oh:0.0000} expected={expected:0.0000} rel={oh / expected - 1:+0.00%;-0.00%}; net stock {ns:0.0000} vs 150 + mean σ̂ {150 + sig:0.0000}");
        Assert.True(Math.Abs(oh / expected - 1) <= 0.03);
    }

    private (double Backlog, double OnHand) KnownSigmaRun()
    {
        var cfg = new ChainConfig
        {
            LeadTimes = new double[] { 2 }, Window = 8, PerfectSupplier = true, Safety = SafetySource.KnownSigma,
            KnownSigma = 10, Z = new[] { 1.0 }, InitialMean = 100, Warmup = 200,
        };
        var bl = new double[20];
        var oh = new double[20];
        for (int r = 0; r < 20; r++)
        {
            var res = SerialChain.Run(cfg, GateA.Demand(r, 2200, 100, 0, 10));
            bl[r] = res.MeanBacklog[0];
            oh[r] = res.MeanOnHand[0];
        }

        return (bl.Average(), oh.Average());
    }

    [Fact]
    public void V13_CostSide()
    {
        var (bl, oh) = KnownSigmaRun();
        double sigmaE = 10 * Math.Sqrt(2 + 4.0 / 8);
        double k = 1.0 * 10 * Math.Sqrt(2) / sigmaE;
        double expBl = sigmaE * GateA.NormalLoss(k);
        double expOh = 10 * Math.Sqrt(2) + expBl;
        _out.WriteLine($"V13 backlog sim={bl:0.0000} exp={expBl:0.0000}; onhand sim={oh:0.0000} exp={expOh:0.0000}");
        Assert.True(Math.Abs(bl / expBl - 1) <= 0.02);
        Assert.True(Math.Abs(oh / expOh - 1) <= 0.02);
    }

    [Fact]
    public void V14_Little()
    {
        var cfg = new ChainConfig { LeadTimes = new double[] { 3 }, Window = 8, PerfectSupplier = true, InitialMean = 100, Warmup = 200 };
        double pipe = 0, ord = 0;
        for (int r = 0; r < 20; r++)
        {
            var res = SerialChain.Run(cfg, GateA.Demand(r, 2200, 100, 0, 10));
            pipe += res.MeanPipeline[0] / 20;
            ord += Descriptive.Mean(res.Orders[0], 200, 2200) / 20;
        }

        _out.WriteLine($"V14 pipeline sim={pipe:0.000} exp=200; order sim={ord:0.000} exp=100");
        Assert.True(Math.Abs(pipe / 200 - 1) <= 0.01);
        Assert.True(Math.Abs(ord / 100 - 1) <= 0.01);
    }

    [Fact]
    public void V15_GoldenTrace()
    {
        var cfg = new ChainConfig { LeadTimes = new double[] { 1, 1 }, Window = 1, Z = new[] { 0.0, 0 }, InitialMean = 0, Warmup = 0 };
        var res = SerialChain.Run(cfg, new double[] { 4, 4, 8, 8, 8, 8 });
        void Eq(string n, double[] exp, double[] act)
        {
            _out.WriteLine($"V15 {n}: exp={string.Join(",", exp)} act={string.Join(",", act)}");
            Assert.Equal(exp.Length, act.Length);
            for (int i = 0; i < exp.Length; i++)
            {
                Assert.True(Math.Abs(exp[i] - act[i]) < 1e-9, $"{n}[{i}] expected {exp[i]} got {act[i]}");
            }
        }

        Eq("orders0", new double[] { 8, 4, 12, 8, 8, 8 }, res.Orders[0]);
        Eq("orders1", new double[] { 16, 0, 20, 4, 8, 8 }, res.Orders[1]);
        Eq("shipped0", new double[] { 0, 0, 12, 4, 16, 8 }, res.RetailerShipped);
        Eq("shipped1", new double[] { 0, 12, 4, 16, 8, 8 }, res.Shipped[1]);
        Eq("onhand0", new double[] { 0, 0, 0, 0, 0, 0 }, res.OnHandPeriod[0]);
        Eq("onhand1", new double[] { 0, 4, 0, 4, 0, 0 }, res.OnHandPeriod[1]);
        Eq("backlog0", new double[] { 4, 8, 4, 8, 0, 0 }, res.BacklogPeriod[0]);
        Eq("backlog1", new double[] { 8, 0, 8, 0, 0, 0 }, res.BacklogPeriod[1]);
    }
}
