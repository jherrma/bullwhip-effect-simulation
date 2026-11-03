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
}
