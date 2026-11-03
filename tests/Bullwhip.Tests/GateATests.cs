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
        LeadTimes = new[] { 2, 2, 2, 2 },
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

    [Fact]
    public void V4_SafetyStockOneSided()
    {
        var cfg = new ChainConfig { LeadTimes = new[] { 2 }, Window = 8, Z = new[] { 1.0 }, InitialMean = 100 };
        var (mean, se) = GateA.BullwhipPerStage(cfg, 10, 0, 100, 2000, 200);
        _out.WriteLine($"V4: sim={mean[0]:0.0000} se={se[0]:0.0000} floor={0.98 * 1.625:0.0000}");
        Assert.True(mean[0] >= 0.98 * 1.625);
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
            LeadTimes = new[] { 2 }, Window = 8, PerfectSupplier = true, Safety = SafetySource.KnownSigma,
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
            LeadTimes = new[] { 1 }, Window = 4, R = 4, Phases = new[] { 0 }, Z = new[] { 0.0 },
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
        LeadTimes = new[] { 2 }, Window = 8, R = 4, Phases = new[] { 0 }, Z = new[] { 1.0 }, InitialMean = 100,
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
            LeadTimes = new[] { 2 }, Window = 8, PerfectSupplier = true, Safety = SafetySource.KnownSigma,
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
        var cfg = new ChainConfig { LeadTimes = new[] { 3 }, Window = 8, PerfectSupplier = true, InitialMean = 100, Warmup = 200 };
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
        var cfg = new ChainConfig { LeadTimes = new[] { 1, 1 }, Window = 1, Z = new[] { 0.0, 0 }, InitialMean = 0, Warmup = 0 };
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
