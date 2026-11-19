using Bullwhip.Core.Chain;
using Bullwhip.Core.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Bullwhip.Tests;

/// <summary>Shared chains, closed-form references and checks of the countermeasure tests (one place, CLAUDE.md).</summary>
internal static class CountermeasureTestChains
{
    public const int Reps = 200;
    public const int Periods = 2000;
    public const int Warmup = 200;
    public const double Mu = 100;
    public const double Sigma = 10;

    /// <summary>
    /// Lag pmf of one link at the dose w of countermeasure 02 (DECISIONS C75): <see cref="BaseLeadTime.LagPmf"/>, the base
    /// distribution (L in 2..5, E[L] = 2.43) at w = 0 and the point mass at L = 2 at w = 1.
    /// </summary>
    public static double[] LagPmf(double w) => BaseLeadTime.LagPmf(w);

    /// <summary>
    /// Expected covers of the four stages at dose w and interval r with equal order phases (customer offset a = R), from the
    /// cycle-average rule written out: retailer E[L] + R − 1; upstream E[R·(⌊(L − 1)/R⌋ + 1)], the lumps of R periods that
    /// a lead time L spans. For L in 2..5 that is L at R = 1, {2, 4, 4, 6} at R = 2 and {4, 4, 4, 8} at R = 4.
    /// </summary>
    public static double[] Covers(double w, int r)
    {
        var lag = LagPmf(w);
        double upstream = 0;
        for (int transit = 0; transit < lag.Length; transit++)
        {
            upstream += lag[transit] * r * (transit / r + 1);
        }

        return new[] { new KaplanLeadTime(lag).MeanLeadTime + r - 1, upstream, upstream, upstream };
    }

    /// <summary>The four-stage base chain with Kaplan lags <see cref="LagPmf"/>(w) at every link, R = r, p = 8, z = 0, unclipped.</summary>
    public static ChainConfig BaseChain(double w = 0, int r = 4)
    {
        var lag = new KaplanLeadTime(LagPmf(w));
        return new ChainConfig
        {
            LeadTimes = Enumerable.Repeat(lag.MeanLeadTime, 4).ToArray(),
            StochasticLeadTimes = Enumerable.Repeat(lag, 4).ToArray(),
            R = r,
            Window = 8,
            Z = new double[] { 0, 0, 0, 0 },
            AllowNegativeOrders = true,
            InitialMean = 100,
            Warmup = Warmup,
        };
    }

    /// <summary>BR4 per stage (C44 method): per replication the variance ratio of non-overlapping 4-period sums, then mean and se over replications.</summary>
    public static (double[] Mean, double[] Se) Br4PerStage(ChainConfig cfg, int reps)
    {
        var ratios = Enumerable.Range(0, 4).Select(_ => new double[reps]).ToArray();
        for (int rep = 0; rep < reps; rep++)
        {
            var demand = GateA.Demand(rep, Warmup + Periods, Mu, 0, Sigma);
            var res = SerialChain.Run(cfg with { LeadTimeSeed = GateA.LeadTimeSeed(rep) }, demand);
            double dVar = SumVariance(demand[Warmup..]);
            for (int k = 0; k < 4; k++)
            {
                ratios[k][rep] = SumVariance(res.Orders[k][Warmup..]) / dVar;
            }
        }

        var stats = ratios.Select(GateA.MeanAndSe).ToArray();
        return (stats.Select(x => x.Mean).ToArray(), stats.Select(x => x.Se).ToArray());
    }

    /// <summary>Decentralised closed form: BR4_k = sum of squared coefficients of the product over i ≤ k of ((1 + c_i/p) − (c_i/p)·x).</summary>
    public static double[] DecentralisedBr4(double[] c, int p)
    {
        var poly = new double[] { 1 };
        var result = new double[c.Length];
        for (int k = 0; k < c.Length; k++)
        {
            double a = c[k] / p;
            var next = new double[poly.Length + 1];
            for (int j = 0; j < poly.Length; j++)
            {
                next[j] += poly[j] * (1 + a);
                next[j + 1] -= poly[j] * a;
            }

            poly = next;
            result[k] = poly.Sum(x => x * x);
        }

        return result;
    }

    /// <summary>Information-sharing closed form: BR4_k = (1 + C_k/p)² + (C_k/p)², C_k = sum of c_i for i ≤ k.</summary>
    public static double[] InfoSharingBr4(double[] c, int p)
    {
        double cum = 0;
        var result = new double[c.Length];
        for (int k = 0; k < c.Length; k++)
        {
            cum += c[k];
            double a = cum / p;
            result[k] = (1 + a) * (1 + a) + a * a;
        }

        return result;
    }

    public static void CheckStages(string name, double[] refs, double[] mean, double[] se, ITestOutputHelper o)
    {
        double z = GateA.BonferroniZ(refs.Length);
        var ok = refs.Select((r, i) => GateA.Passes(r, mean[i], se[i], z)).ToArray();
        Table(name, refs, mean, se, z, ok, o);
        Assert.True(ok.All(x => x), $"{name}: a stage failed");
    }

    public static void AssertClosedForm(string name, double[] refs, (double[] Mean, double[] Se) sim, ITestOutputHelper o) =>
        CheckStages(name, refs, sim.Mean, sim.Se, o);

    private static void Table(string name, double[] refs, double[] mean, double[] se, double z, bool[] ok, ITestOutputHelper o)
    {
        for (int i = 0; i < refs.Length; i++)
        {
            o.WriteLine($"{name} stage {i + 1}: ref={refs[i],8:0.0000} sim={mean[i],8:0.0000} rel={mean[i] / refs[i] - 1,8:+0.00%;-0.00%} se={se[i],7:0.0000} z={z:0.00} {(ok[i] ? "PASS" : "FAIL")}");
        }
    }

    private static double SumVariance(double[] x)
    {
        int count = x.Length / 4;
        var sums = Enumerable.Range(0, count).Select(b => x.Skip(b * 4).Take(4).Sum()).ToArray();
        double mean = sums.Average();
        return sums.Sum(v => (v - mean) * (v - mean)) / (sums.Length - 1);
    }
}
