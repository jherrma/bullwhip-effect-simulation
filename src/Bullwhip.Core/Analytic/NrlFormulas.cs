namespace Bullwhip.Core.Analytic;

/// <summary>
/// σ̂ estimator of Chen, Ryan &amp; Simchi-Levi (2000), Naval Research Logistics 47(4), 269–286, p. 273: σ̂_e = c·Δ̂ with Δ̂
/// the exponentially smoothed mean absolute one-period forecast error (Eq. 5), and σ̂_eL = K_L·Δ̂, K_L = c·A, where
/// σ_eL = A·σ_e and σ_e is the standard deviation of the single-period forecast error (p. 273). The paper gives c but not A
/// ("the exact value of A will not be needed"; it refers to CDRS 2000 and Ryan 1997).
/// </summary>
public static class NrlFormulas
{
    /// <summary>c = √(π/2), p. 273: E|e| = √(2/π)·σ_e for normal forecast errors (the paper's value for normal errors).</summary>
    public static readonly double MadToSigma = Math.Sqrt(Math.PI / 2.0);

    /// <summary>
    /// A = σ_eL/σ_e for OUR model, iid demand (our derivation, not from the paper). MA(p) forecast μ̂_t of the p periods up
    /// to t: the one-period error e_t = D_t − μ̂_{t−1} has Var = σ²(1 + 1/p); the L-period error Σ_{i=1..L} D_{t+i} − L·μ̂_t
    /// has Var = σ²(L + L²/p) (future and past demand disjoint). So A² = (L + L²/p)/(1 + 1/p); L = 2, p = 8: A = 1.49071.
    /// √(L + L²/p) is σ_eL/σ (demand σ), which is A only if σ_e = σ (known mean). The NRL paper forecasts by exponential
    /// smoothing, where Var(μ̂) = α/(2−α)·σ² replaces σ²/p in both variances. Real L (Kaplan mean at R = 1): the same form.
    /// The value equals Chen's C (<see cref="ChenFormulas.SigmaConstantIid"/>): both are σ_eL/σ_e for this model, so this
    /// delegates to it. Kept under its own name because the NRL constant A and Chen's C are cited separately.
    /// </summary>
    public static double ErrorRatioIid(double leadTime, int window)
    {
        return ChenFormulas.SigmaConstantIid(leadTime, window);
    }

    /// <summary>K_L = c·A (p. 273) with A from <see cref="ErrorRatioIid"/>; L = 2, p = 8: 1.86833.</summary>
    public static double KFactorIid(double leadTime, int window)
    {
        return MadToSigma * ErrorRatioIid(leadTime, window);
    }
}
