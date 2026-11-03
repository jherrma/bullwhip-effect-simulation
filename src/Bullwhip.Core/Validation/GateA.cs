using Bullwhip.Core.Chain;
using Bullwhip.Core.Demand;
using Bullwhip.Core.Rng;
using Bullwhip.Core.Stats;

namespace Bullwhip.Core.Validation;

/// <summary>Helpers for Gate A (plan §5, V2–V15): reference formulas and replication runners.</summary>
public static class GateA
{
    public const int Replications = ChenSingleStageCheck.Replications;
    public const int Periods = ChenSingleStageCheck.Periods;
    public const int Warmup = ChenSingleStageCheck.Warmup;
    public const double Mu = ChenSingleStageCheck.Mu;
    public const double Sigma = ChenSingleStageCheck.Sigma;
    public const double Alpha = ChenSingleStageCheck.Alpha;

    /// <summary>Demand of replication rep (common random numbers across configurations).</summary>
    public static double[] Demand(int rep, int length, double mu, double rho, double sigma)
    {
        var rng = new SplitMix64(SplitMix64.Derive(ChenSingleStageCheck.BaseSeed, (ulong)rep));
        return Ar1Demand.Generate(length, mu, rho, sigma, rng);
    }

    /// <summary>Per-stage mean and standard error (over replications) of Bullwhip(k, Var(D)).</summary>
    public static (double[] Mean, double[] Se) BullwhipPerStage(
        ChainConfig cfg, double sigma, double rho, int reps, int periods, int warmup)
    {
        int n = cfg.LeadTimes.Length;
        double v = Ar1Demand.StationaryVariance(sigma, rho);
        var run = cfg with { Warmup = warmup };
        var values = new double[n][];
        for (int k = 0; k < n; k++)
        {
            values[k] = new double[reps];
        }

        for (int r = 0; r < reps; r++)
        {
            var res = SerialChain.Run(run, Demand(r, warmup + periods, Mu, rho, sigma));
            for (int k = 0; k < n; k++)
            {
                values[k][r] = res.Bullwhip(k, v);
            }
        }

        var mean = new double[n];
        var se = new double[n];
        for (int k = 0; k < n; k++)
        {
            (mean[k], se[k]) = MeanAndSe(values[k]);
        }

        return (mean, se);
    }

    public static (double Mean, double Se) MeanAndSe(double[] x)
    {
        double mean = x.Average();
        double sd = Math.Sqrt(x.Sum(v => (v - mean) * (v - mean)) / (x.Length - 1));
        return (mean, sd / Math.Sqrt(x.Length));
    }

    public static double BonferroniZ(int m)
    {
        return Normal.Quantile(1.0 - Alpha / (2.0 * m));
    }

    /// <summary>Plan pass rule: within 2 % relative AND inside the Bonferroni interval.</summary>
    public static bool Passes(double reference, double mean, double se, double z)
    {
        return Math.Abs(mean / reference - 1.0) <= 0.02 && Math.Abs(mean - reference) <= z * se;
    }

    /// <summary>Filter reference, iid demand, stage k (1-based): Σ_j C(k,j)² a^(2(k−j)) b^(2j), a = 1+Lp/p, b = Lp/p.</summary>
    public static double FilterReference(int k, int lp, int p)
    {
        double a = 1.0 + (double)lp / p;
        double b = (double)lp / p;
        double sum = 0.0;
        for (int j = 0; j <= k; j++)
        {
            double c = Binomial(k, j);
            sum += c * c * Math.Pow(a, 2 * (k - j)) * Math.Pow(b, 2 * j);
        }

        return sum;
    }

    /// <summary>Chen product lower bound for stage k (1-based), equal lead times.</summary>
    public static double ChenProductBound(int k, int l, int p)
    {
        double single = 1.0 + 2.0 * l / p + 2.0 * l * l / ((double)p * p);
        return Math.Pow(single, k);
    }

    /// <summary>Chen centralised bound for stage k (1-based), equal lead times L.</summary>
    public static double ChenCentralised(int k, int l, int p)
    {
        double x = (double)k * l / p;
        return 1.0 + 2.0 * x + 2.0 * x * x;
    }

    /// <summary>Standard normal loss G(k) = φ(k) − k(1 − Φ(k)).</summary>
    public static double NormalLoss(double k)
    {
        double phi = Math.Exp(-0.5 * k * k) / Math.Sqrt(2.0 * Math.PI);
        return phi - k * (1.0 - Normal.Cdf(k));
    }

    /// <summary>
    /// E[σ̂]/σ_e for σ̂ = plain RMS of the last w order-time protection-period errors of a stage with smooth iid normal
    /// demand (cover Lp, MA over p periods, order interval R). e(t0) = D(t0, t0+Lp] − (Lp/p)·D(t0−p, t0] is linear in
    /// demand with coefficients a_i, so errors h order times apart are jointly normal with Cov = Σ_i a_i·a_{i−hR}
    /// (R=4, L=2, p=8: ρ = 0.008, −0.385, −0.077, 0, …). With C their correlation matrix, σ̂²/σ_e² = yᵀy/w, y ~ N(0, C),
    /// so E[σ̂²] = σ_e² exactly and E[√Q] = (2√π)⁻¹ ∫₀^∞ (1 − det(I + 2sC/w)^(−1/2)) s^(−3/2) ds (Jensen bias of the root).
    /// </summary>
    public static double RmsMeanFactor(int lp, int p, int r, int w)
    {
        var a = Enumerable.Range(0, p + lp).Select(i => i < p ? -(double)lp / p : 1.0).ToArray();
        double Cov(int d) => Enumerable.Range(d, Math.Max(0, a.Length - d)).Sum(i => a[i] * a[i - d]);
        var rho = Enumerable.Range(0, w).Select(h => Cov(h * r) / Cov(0)).ToArray();
        const double dx = 0.01;
        double sum = 0.0;
        var m = new double[w, w];
        for (double x = -50; x <= 50; x += dx)
        {
            // log det(I + 2sC/w) by Cholesky (s = eˣ, ds·s^(−3/2) = e^(−x/2)·dx).
            double s = Math.Exp(x), logDet = 0.0;
            for (int j = 0; j < w; j++)
            {
                for (int i = j; i < w; i++)
                {
                    double v = (i == j ? 1.0 : 0.0) + 2.0 * s * rho[i - j] / w;
                    for (int k = 0; k < j; k++)
                    {
                        v -= m[i, k] * m[j, k];
                    }

                    m[i, j] = i == j ? Math.Sqrt(v) : v / m[j, j];
                }

                logDet += 2.0 * Math.Log(m[j, j]);
            }

            sum += (1.0 - Math.Exp(-0.5 * logDet)) * Math.Exp(-0.5 * x) * dx;
        }

        return sum / (2.0 * Math.Sqrt(Math.PI));
    }

    private static double Binomial(int n, int k)
    {
        double c = 1.0;
        for (int i = 1; i <= k; i++)
        {
            c = c * (n - k + i) / i;
        }

        return c;
    }
}
