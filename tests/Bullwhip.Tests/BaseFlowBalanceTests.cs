using Bullwhip.Core.Chain;
using Bullwhip.Core.Stats;
using Bullwhip.Core.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Bullwhip.Tests;

/// <summary>
/// Flow balance of the base case (DECISIONS C78, C88): four stages, Kaplan lead time with the base pmf, cover rule B,
/// equal order phases, p = 8, z &gt; 0 at the optimum of the study <c>k-base</c>, R = 4 and R = 8, on the held-out
/// replications 1000–1099. Every expectation comes from the order rule, the lead-time pmf and conservation; none is taken
/// from program output.
/// </summary>
/// <remarks>
/// <para>
/// Balance. On-order = in transit + what the supplier still owes, and the supplier owes exactly its backlog (all of it is
/// this stage's orders; the external source above stage 4 owes nothing). So at the end of every period
/// NS_k = IP_k − InTransit_k − B_{k+1}, with NS = on-hand − backlog and IP the inventory position. Unclipped, IP after an
/// order time τ is S_τ = c_k·μ̂_τ + z_k·σ̂_τ. The retailer then loses D(τ, τ + i] in the next R − 1 periods; an upstream stage
/// with equal phases receives its next lump only at τ + R, so its IP stays S_τ over the cycle. Each dispatch stays in
/// transit (counted with the arrival due at t + 1) for L periods, independent of its quantity, so E[InTransit] = μ·E[L]
/// (Little's law). The time average over full cycles is
/// E[NS_k] = c_k·μ + z_k·E[σ̂_k] − [k = 1]·μ·(R − 1)/2 − μ·E[L] − E[B_{k+1}],
/// with E[μ̂] = μ at every stage in steady state, E[L] from the hazards of DECISIONS C82 (2), and c_k from cover rule B:
/// the retailer E[L] + R − 1, an upstream stage with equal phases R·E[⌊(L − 1)/R⌋ + 1] (DECISIONS C21, C82 (7)).
/// </para>
/// <para>
/// Inputs taken from the same run: E[σ̂_k] (the mean σ̂ at the order times) and E[B_{k+1}]. Upstream σ̂ and backlogs have no
/// closed form at z &gt; 0, because their input is the downstream orders, which depend on the downstream σ̂. Given these two,
/// the balance checks the cover, the use of z, the order timing and the realised lead time: a cover that is off by Δc moves
/// NS by Δc·μ, a mean lead time that is off by ΔL moves it by ΔL·μ.
/// </para>
/// <para>
/// Clipping. A clipped order leaves IP above S, so with orders clipped at 0 (the base case) the balance is a lower bound
/// only; it is exact in the mean for the same chain with negative orders allowed.
/// </para>
/// <para>
/// No closed form at the base case (z &gt; 0, clipped, four stages), therefore not asserted here: the split of NS into
/// on-hand and backlog (the retailer ready rate), because upstream shortfalls delay the retailer's arrivals and σ̂ is random;
/// upstream σ̂; BR4 (closed form only at z = 0 without clipping, <c>OrderIntervalCountermeasureTests</c>). The split is
/// asserted where a closed form exists: one stage with external supply and known σ. The retailer's protection-period error
/// does not depend on z, clipping or the upstream stages and is asserted at the base case.
/// </para>
/// </remarks>
public class BaseFlowBalanceTests
{
    private const double H = 0.689721594469328;
    private const double H3 = 0.7564324234714825;
    private const int FirstRep = 1000;
    private const int Reps = 100;
    private const int P = 8;
    private const double Mu = GateA.Mu;
    private const double Sigma = GateA.Sigma;

    /// <summary>P(L = l), l = 0..5, from the transit hazards h_1 = h_2 = H, h_3 = H3, h_4 = 1 (L = T + 1).</summary>
    private static readonly double[] LeadPmf = { 0, 0, H, (1 - H) * H, (1 - H) * (1 - H) * H3, (1 - H) * (1 - H) * (1 - H3) };

    private static readonly double MeanLead = Enumerable.Range(0, LeadPmf.Length).Sum(l => l * LeadPmf[l]);

    private readonly ITestOutputHelper _out;

    public BaseFlowBalanceTests(ITestOutputHelper output)
    {
        _out = output;
    }

    [Fact]
    public void LeadPmfFromTheHazards_HasTheBaseMeanAndCovers()
    {
        Assert.Equal(1.0, LeadPmf.Sum(), 12);
        Assert.Equal(2.43, MeanLead, 9);
        Assert.Equal(5.43, Cover(0, 4), 9);
        Assert.Equal(4 + 4 * LeadPmf[5], Cover(1, 4), 12);
        Assert.Equal(9.43, Cover(0, 8), 9);
        Assert.Equal(8.0, Cover(1, 8), 12);
    }

    [Theory]
    [InlineData(4, 3.00, 1.50, 1.50, 1.50)]
    [InlineData(8, 0.75, 1.00, 1.00, 0.75)]
    public void NetStock_WithNegativeOrdersAllowed_MatchesTheBalanceOfOrderRuleAndLeadTime(int r, double z1, double z2, double z3, double z4)
    {
        // Exact in the mean: |mean residual| ≤ 4 standard errors over the replications, per stage.
        var residuals = BalanceResiduals(r, new[] { z1, z2, z3, z4 }, clipped: false);
        for (int k = 0; k < 4; k++)
        {
            var (mean, se) = MeanAndStandardError(residuals[k]);
            _out.WriteLine($"R={r} stage {k + 1}: NS minus balance = {mean:0.000} (SE {se:0.000})");
            Assert.InRange(mean, -4 * se, 4 * se);
        }
    }

    [Theory]
    [InlineData(4, 3.00, 1.50, 1.50, 1.50)]
    [InlineData(8, 0.75, 1.00, 1.00, 0.75)]
    public void NetStock_WithClippedOrders_IsAtLeastTheBalance(int r, double z1, double z2, double z3, double z4)
    {
        // The configuration of the reported numbers. Clipping only raises IP above S, so the residual is ≥ 0 in the mean;
        // its size (the mean excess of IP over S) has no closed form and is printed, not asserted.
        var residuals = BalanceResiduals(r, new[] { z1, z2, z3, z4 }, clipped: true);
        for (int k = 0; k < 4; k++)
        {
            var (mean, se) = MeanAndStandardError(residuals[k]);
            _out.WriteLine($"R={r} stage {k + 1}: NS minus balance = {mean:0.000} (SE {se:0.000}), clipped");
            Assert.True(mean >= -4 * se, $"stage {k + 1}: residual {mean} below the lower bound (SE {se})");
        }
    }

    [Theory]
    [InlineData(4, 3.00, 1.50, 1.50, 1.50)]
    [InlineData(8, 0.75, 1.00, 1.00, 0.75)]
    public void RetailerProtectionError_MatchesTheClosedForm_AtTheBaseCase(int r, double z1, double z2, double z3, double z4)
    {
        // The retailer's error e = Σ a_i·D_{t0+i} − c·μ̂(t0) uses customer demand only. The two-weight window has Σa = Σa² = c
        // (ProtectionWindow remarks) and μ̂ uses the p periods up to t0, so E[e] = 0 and E[e²] = σ²·(c + c²/p):
        // 30.19² at R = 4 (c = 5.43), 45.33² at R = 8 (c = 9.43). The mean σ̂ is below √E[e²] (Jensen).
        double c = Cover(0, r);
        double expectedSquare = Sigma * Sigma * (c + c * c / P);
        var squares = new double[Reps];
        var sigmaHat = new double[Reps];
        for (int i = 0; i < Reps; i++)
        {
            var res = RunChain(BaseChain(r, new[] { z1, z2, z3, z4 }, clipped: true), FirstRep + i);
            squares[i] = res.ProtectionErrorRms[0] * res.ProtectionErrorRms[0];
            sigmaHat[i] = res.MeanSigmaHat[0];
        }

        var (mean, se) = MeanAndStandardError(squares);
        _out.WriteLine($"R={r}: retailer error RMS {Math.Sqrt(mean):0.000} against {Math.Sqrt(expectedSquare):0.000}; mean sigma-hat {sigmaHat.Average():0.000}");
        Assert.InRange(mean, expectedSquare - 4 * se, expectedSquare + 4 * se);
        Assert.True(sigmaHat.Average() < Math.Sqrt(expectedSquare));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    public void OnHandBacklogSplit_OfOneStageWithKnownSigma_MatchesTheNormalMixture(int r)
    {
        // One stage, external supply with the base Kaplan lead time, S = c·μ̂ + z·σ·√c (KnownSigma, z = 1). Orders never
        // cross, so NS(t) = S_τ − D(τ, t] with τ the newest order time whose lot has arrived by t, and P(τ ≥ t − j) =
        // P(L ≤ j). At phase i = t mod R the exposure j = i + mR has weight P(L ≤ j) − P(L ≤ j − R); given j,
        // NS ~ N((c − j)·μ + z·σ·√c, σ²·(c²/p + j)) (μ̂ and D(τ, t] use disjoint periods). Ready rate P(NS ≥ 0), backlog
        // E[(−NS)⁺] and on-hand E[NS⁺] follow from the normal loss function. Negative orders have probability below 1e-20
        // (mean R·μ, standard deviation below 60), so clipping does not enter.
        const double Z = 1.0;
        double c = Cover(0, r);
        var (ready, onHand, backlog) = NormalMixtureSplit(r, c, Z);
        var lag = new KaplanLeadTime(BaseLeadTime.LagPmf());
        var cfg = new ChainConfig
        {
            LeadTimes = new[] { lag.MeanLeadTime },
            StochasticLeadTimes = new[] { lag },
            R = r,
            Window = P,
            Z = new[] { Z },
            Safety = SafetySource.KnownSigma,
            KnownSigma = Sigma,
            AllowNegativeOrders = false,
            InitialMean = Mu,
            Warmup = GateA.Warmup,
        };
        var measured = new double[3][] { new double[2 * Reps], new double[2 * Reps], new double[2 * Reps] };
        for (int i = 0; i < 2 * Reps; i++)
        {
            var res = RunChain(cfg, FirstRep + i);
            measured[0][i] = res.ReadyRate;
            measured[1][i] = res.MeanOnHand[0];
            measured[2][i] = res.MeanBacklog[0];
        }

        AssertWithinFourStandardErrors($"R={r} ready rate", measured[0], ready);
        AssertWithinFourStandardErrors($"R={r} on-hand", measured[1], onHand);
        AssertWithinFourStandardErrors($"R={r} backlog", measured[2], backlog);
    }

    /// <summary>Cover rule B at equal phases (class remarks): retailer E[L] + R − 1, upstream R·E[⌊(L − 1)/R⌋ + 1].</summary>
    private static double Cover(int stage, int r)
    {
        if (stage == 0)
        {
            return MeanLead + r - 1;
        }

        return Enumerable.Range(1, LeadPmf.Length - 1).Sum(l => LeadPmf[l] * r * ((l - 1) / r + 1));
    }

    /// <summary>Per stage and replication: mean NS over the measured periods minus the balance of the class remarks.</summary>
    private static double[][] BalanceResiduals(int r, double[] z, bool clipped)
    {
        var residuals = Enumerable.Range(0, 4).Select(_ => new double[Reps]).ToArray();
        var cfg = BaseChain(r, z, clipped);
        for (int i = 0; i < Reps; i++)
        {
            var res = RunChain(cfg, FirstRep + i);
            for (int k = 0; k < 4; k++)
            {
                double netStock = MeasuredMean(res.OnHandPeriod[k], res.Warmup) - MeasuredMean(res.BacklogPeriod[k], res.Warmup);
                double supplierBacklog = k < 3 ? MeasuredMean(res.BacklogPeriod[k + 1], res.Warmup) : 0.0;
                double cycle = k == 0 ? Mu * (r - 1) / 2.0 : 0.0;
                double balance = Cover(k, r) * Mu + z[k] * res.MeanSigmaHat[k] - cycle - Mu * MeanLead - supplierBacklog;
                residuals[k][i] = netStock - balance;
            }
        }

        return residuals;
    }

    /// <summary>Ready rate, mean on-hand and mean backlog of the one-stage chain (test comment).</summary>
    private static (double Ready, double OnHand, double Backlog) NormalMixtureSplit(int r, double c, double z)
    {
        double ready = 0.0;
        double onHand = 0.0;
        double backlog = 0.0;
        for (int i = 0; i < r; i++)
        {
            for (int m = 0; i + (m - 1) * r < LeadPmf.Length; m++)
            {
                int j = i + m * r;
                double weight = LeadCdf(j) - LeadCdf(j - r);
                double mean = (c - j) * Mu + z * Sigma * Math.Sqrt(c);
                double sd = Sigma * Math.Sqrt(c * c / P + j);
                double u = mean / sd;
                double shortfall = sd * Math.Exp(-0.5 * u * u) / Math.Sqrt(2 * Math.PI) - mean * Normal.Cdf(-u);
                ready += weight * Normal.Cdf(u) / r;
                backlog += weight * shortfall / r;
                onHand += weight * (mean + shortfall) / r;
            }
        }

        return (ready, onHand, backlog);
    }

    private static double LeadCdf(int j) => j < 0 ? 0.0 : LeadPmf.Take(j + 1).Sum();

    private static ChainConfig BaseChain(int r, double[] z, bool clipped)
    {
        var lag = new KaplanLeadTime(BaseLeadTime.LagPmf());
        return new ChainConfig
        {
            LeadTimes = Enumerable.Repeat(lag.MeanLeadTime, 4).ToArray(),
            StochasticLeadTimes = Enumerable.Repeat(lag, 4).ToArray(),
            R = r,
            Window = P,
            Z = z,
            AllowNegativeOrders = !clipped,
            InitialMean = Mu,
            Warmup = GateA.Warmup,
        };
    }

    private static ChainResult RunChain(ChainConfig cfg, int rep) =>
        SerialChain.Run(cfg with { LeadTimeSeed = GateA.LeadTimeSeed(rep) }, GateA.Demand(rep, GateA.Warmup + GateA.Periods, Mu, 0, Sigma));

    private static double MeasuredMean(double[] perPeriod, int warmup) => perPeriod.Skip(warmup).Average();

    private static (double Mean, double StandardError) MeanAndStandardError(double[] x)
    {
        double mean = x.Average();
        double sd = Math.Sqrt(x.Sum(v => (v - mean) * (v - mean)) / (x.Length - 1));
        return (mean, sd / Math.Sqrt(x.Length));
    }

    private void AssertWithinFourStandardErrors(string label, double[] measured, double expected)
    {
        var (mean, se) = MeanAndStandardError(measured);
        _out.WriteLine($"{label}: {mean:0.00000} against closed form {expected:0.00000} (SE {se:0.00000})");
        Assert.InRange(mean, expected - 4 * se, expected + 4 * se);
    }
}
