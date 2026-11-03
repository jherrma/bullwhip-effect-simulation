namespace Bullwhip.Core.Analytic;

/// <summary>Closed forms from Chen, Drezner, Ryan & Simchi-Levi (2000), Management Science 46(3), 436–443.</summary>
public static class ChenFormulas
{
    /// <summary>
    /// Eq. (5), Thm 2.2, p. 438: Var(q)/Var(D) ≥ 1 + (2L/p + 2L²/p²)(1 − ρ^p); tight when z = 0.
    /// Here L is the model lead time Lp (equal to Chen's L at R = 1). A real L is the cover of S = L·μ̂: at z = 0,
    /// q_t = D_t + L(μ̂_t − μ̂_{t−1}) = (1 + L/p)D_t − (L/p)D_{t−p}, so the formula holds for any real L (iid demand).
    /// </summary>
    public static double SingleStageBullwhipRatio(double leadTime, int window, double rho)
    {
        double a = 2.0 * leadTime / window;
        double b = 2.0 * leadTime * leadTime / ((double)window * window);
        return 1.0 + (a + b) * (1.0 - Math.Pow(rho, window));
    }

    /// <summary>
    /// Constant C of Eq. (3), p. 437, for iid demand. Not from the paper: Chen et al. name C_{L,ρ} and refer to Ryan
    /// (1997) for it. Our choice makes E[σ̂²] equal to the Lp-period error variance, the target of our own realised-error
    /// σ̂: the one-period error e_t = D_t − (1/p)·Σ_{i=1..p} D_{t−i} has Var = σ²(1 + 1/p), the Lp-period error
    /// Var = σ²(Lp + Lp²/p), so C² = (Lp + Lp²/p)/(1 + 1/p). Lp = 2, p = 8: C = 1.4907. Real Lp: the same target.
    /// </summary>
    public static double SigmaConstantIid(double leadTime, int window)
    {
        double lp = leadTime;
        return Math.Sqrt((lp + lp * lp / window) / (1.0 + 1.0 / window));
    }
}
