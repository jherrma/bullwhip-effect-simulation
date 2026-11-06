using Bullwhip.Core.Rng;

namespace Bullwhip.Core.Demand;

/// <summary>
/// Chen et al. (2000) Eq. (1): D_t = μ + ρ·D_{t−1} + ε_t with iid normal ε (std σ).
/// The intercept is set to μ(1−ρ) so that E[D] = μ. For ρ = 0 this is iid demand.
/// </summary>
public static class Ar1Demand
{
    public static double[] Generate(int length, double mu, double rho, double sigma, SplitMix64 rng)
    {
        var demand = new double[length];
        double intercept = mu * (1.0 - rho);
        demand[0] = mu;
        for (int t = 1; t < length; t++)
        {
            demand[t] = intercept + rho * demand[t - 1] + sigma * rng.NextStandardNormal();
        }

        return demand;
    }

    /// <summary>Stationary variance σ²/(1−ρ²) (Chen et al. 2000, Sec. 2).</summary>
    public static double StationaryVariance(double sigma, double rho)
    {
        return sigma * sigma / (1.0 - rho * rho);
    }

    /// <summary>
    /// Innovation std σ·sqrt(1−ρ²) that keeps the stationary std at σ (DECISIONS C50, C52).
    /// Throws <see cref="ArgumentOutOfRangeException"/> unless −1 &lt; ρ &lt; 1 (NaN included).
    /// </summary>
    public static double InnovationSigma(double sigma, double rho)
    {
        if (!(rho > -1.0 && rho < 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(rho), rho, "Rho must lie in (-1, 1).");
        }

        return sigma * Math.Sqrt(1.0 - rho * rho);
    }
}
