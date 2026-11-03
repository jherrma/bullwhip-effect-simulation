namespace Bullwhip.Core.Chain;

/// <summary>
/// Weights of the realised side of the protection-period error, D(t0, t0+Lp], for a non-integer Lp (R = 1 only).
/// </summary>
/// <remarks>
/// Integer Lp: the exact per-period sum over (t0, t0+Lp] (SerialChain, unchanged). Non-integer Lp = n + f, 0 &lt; f &lt; 1,
/// arises from planning on a non-integer E[L] under Kaplan lead times. The target is the protection-period demand of the
/// planning model: mean Lp·μ and, for iid demand, variance Lp·σ², so that e = Σ a_i·D_{t0+i} − Lp·μ̂(t0) has
/// E[e²] = σ²(Lp + Lp²/p), the integer-Lp formula (μ̂ uses the p periods up to t0, disjoint from the window). This needs
/// Σ a_i = Lp and Σ a_i² = Lp. Weighting the boundary period by its coverage f (1, …, 1, f) gives Σ a_i² = n + f² &lt; Lp,
/// i.e. E[e²] low by f(1 − f)·σ² (−2.1 % at Lp = 6.4, p = 8). Weights in [0, 1] satisfy both sums only for integer Lp, so no
/// single boundary weight works. Two weights do: a_1 … a_{n−1} = 1, and periods n and n+1 share 1 + f with
/// a_n + a_{n+1} = 1 + f and a_n² + a_{n+1}² = 1 + f, so a_{n, n+1} = (1 + f ± √(1 − f²))/2. The + root on period n tends to
/// (1, 0) as f → 0 and to (1, 1) as f → 1, the integer windows (t0, t0+n] and (t0, t0+n+1]. Lp = 6.4: 1.1583 and 0.2417.
/// The window still closes at t0 + ⌈Lp⌉.
///
/// Scope. Exact in mean and variance for iid input (retailer; POS demand under info sharing). For autocorrelated input
/// (upstream stages in decentralised mode, AR(1) demand) the discrete model has no demand for a fraction of a period, so the
/// rule is a convention there. Lead-time variability is not in this error (planning on the known mean E[L], plan B4), as
/// for an integer E[L] in Kaplan mode.
/// </remarks>
public static class ProtectionWindow
{
    /// <summary>Weights a_1 … a_⌈lp⌉ of the periods t0+1 … t0+⌈lp⌉; all 1 for an integer lp. Requires lp ≥ 1.</summary>
    public static double[] Weights(double lp)
    {
        if (double.IsNaN(lp) || double.IsInfinity(lp) || lp < 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(lp), "The protection period must be finite and at least 1.");
        }

        int n = (int)Math.Floor(lp);
        double f = lp - n;
        if (f == 0.0)
        {
            return Enumerable.Repeat(1.0, n).ToArray();
        }

        var w = new double[n + 1];
        for (int i = 0; i < n - 1; i++)
        {
            w[i] = 1.0;
        }

        double root = Math.Sqrt(1.0 - f * f);
        w[n - 1] = (1.0 + f + root) / 2.0;
        w[n] = (1.0 + f - root) / 2.0;
        return w;
    }
}
