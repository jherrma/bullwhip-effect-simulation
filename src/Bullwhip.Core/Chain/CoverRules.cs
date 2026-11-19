namespace Bullwhip.Core.Chain;

/// <summary>
/// Cover c of S = c·μ̂ + z·σ̂ at R &gt; 1 for a lead-time distribution (<see cref="CoverRule"/> options A, B, C), and the realised
/// window of the protection-period error that matches it.
/// </summary>
/// <remarks>
/// Exposure. Stage k orders at t0 ≡ φ_k (mod R). Let τ(t) be the newest own order time whose lot has arrived by the start of t.
/// Orders never cross (Kaplan), so every older lot is in and no newer one is: NS(t) = S_τ − D(τ, t] (unclipped orders). The
/// newest dispatch period cleared by t is G(t) = max_{v ≤ t}(v − 1 − C_v), and P(t − G(t) &gt; y) = Π_{i&lt;y}(1 − F(i)) = P(L &gt; y)
/// for every t, so t − G(t) ~ L. τ(t) is the last order time ≤ G(t); with u = (t − 1 − φ_k) mod R the exposure is
/// X_u = t − τ = L + ((u + 1 − L) mod R), L from the lag pmf (+1). Smooth input (the retailer; POS demand under info sharing):
/// E[D(τ, t]] = μ·E[X_u]. Lumpy input (stage k &gt; 0 at R &gt; 1): the customer's lumps of R·μ land at τ + a, τ + a + R, …
/// (a = <see cref="PhaseTarget.Offset"/>), so (τ, t] holds N_u = ⌊(X_u − a)/R⌋ + 1 lumps (0 for X_u &lt; a) and the exposure is
/// R·N_u.
/// Time averages over u: E[X] = E[L] + (R − 1)/2 and R·E[N] = E[L] + R − a. At the order time (u = R − 1) X = R times the own
/// lots outstanding after ordering, so E[X_{R−1}] = R·Σ_j P(L &gt; jR) = R·E[⌈L/R⌉].
///
/// Rules. Point-mass L: X_u runs through L .. L + R − 1, so (A) and (B) give L + R − 1 (smooth) and n·R (lumpy), the integer
/// rule; (C) gives R·⌈L/R⌉, which is not the integer rule (L = 2, R = 4: 4 instead of 5).
/// (A) WorstPhase: c = max_u E[exposure_u] (smooth X, lumpy R·N with the stage's own a). E[NS | u] ≥ z·σ̂ at every phase,
///     equal at the worst one. Smooth input: the worst phase of a fixed L is u = (L − 2) mod R, which moves with L, so random
///     L smooths E[X_u] over u and c lies between E[X] and E[L] + R − 1. Lumpy input: the
///     worst phase is u = a − 1 (a lump has just landed) for every L, and there R·N = n·R(L), so (A) = (B) exactly.
/// (B) CycleAverage: c = E_L[c_int(L)] with c_int(L) = L + R − 1 (smooth) or R·(⌊(L + R − 1 − a)/R⌋ + 1) (lumpy, n·R at that L).
///     Smooth: c = E[L] + R − 1 = E[X] + (R − 1)/2. Lumpy: c = E[L] + R − a + E[(a − L) mod R]. Time-average E[NS] = z·σ̂ +
///     μ·(R − 1)/2 (smooth) or z·σ̂ + μ·E[(a − L) mod R] (lumpy): the deterministic cycle stock averaged over the integer L draws
///     (Kaplan's L is integer per order; only its mean is not).
/// (C) LotsInTransit: c = R·Σ_j P(L &gt; jR) at every stage, smooth or lumpy (the customer phase is ignored): E[NS] = z·σ̂ at the
///     own order times of the retailer.
/// R = 1: one phase, a = 1, and all three give E[L].
/// </remarks>
public static class CoverRules
{
    /// <summary>P(L = l) for l = 0..m+1 (index 0 is 0): the Kaplan lag pmf shifted by one, or a point mass at the fixed L.</summary>
    public static double[] LeadPmf(KaplanLeadTime? kaplan, int fixedLead)
    {
        if (kaplan == null)
        {
            if (fixedLead < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(fixedLead), "L must be at least 1.");
            }

            var point = new double[fixedLead + 1];
            point[fixedLead] = 1.0;
            return point;
        }

        var pmf = new double[kaplan.MaxLag + 2];
        for (int g = 0; g <= kaplan.MaxLag; g++)
        {
            pmf[g + 1] = kaplan.LagPmf[g];
        }

        return pmf;
    }

    /// <summary>E[X_u], u = 0..R−1, smooth exposure t − τ at the phase u = (t − 1 − φ) mod R.</summary>
    public static double[] SmoothExposure(IReadOnlyList<double> leadPmf, int r)
    {
        var e = new double[r];
        for (int u = 0; u < r; u++)
        {
            for (int l = 1; l < leadPmf.Count; l++)
            {
                e[u] += leadPmf[l] * X(l, u, r);
            }
        }

        return e;
    }

    /// <summary>R·E[N_u], u = 0..R−1, lumpy exposure for customer offset a.</summary>
    public static double[] LumpyExposure(IReadOnlyList<double> leadPmf, int r, int a)
    {
        var e = new double[r];
        for (int u = 0; u < r; u++)
        {
            for (int l = 1; l < leadPmf.Count; l++)
            {
                // N = ⌊(X − a)/R⌋ + 1 = (X − a + R)/R, which is 0 for X < a (X − a + R ≥ 1, so integer division floors).
                e[u] += leadPmf[l] * r * ((X(l, u, r) - a + r) / r);
            }
        }

        return e;
    }

    /// <summary>
    /// Cover c for a rule (not <see cref="CoverRule.Integer"/>). <paramref name="customerOffset"/> null: smooth input; otherwise
    /// the offset a of a lumpy input.
    /// </summary>
    public static double Cover(CoverRule rule, IReadOnlyList<double> leadPmf, int r, int? customerOffset)
    {
        if (r < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(r));
        }

        switch (rule)
        {
            case CoverRule.WorstPhase:
                return (customerOffset is int a0 ? LumpyExposure(leadPmf, r, a0) : SmoothExposure(leadPmf, r)).Max();
            case CoverRule.CycleAverage:
            {
                double c = 0.0;
                for (int l = 1; l < leadPmf.Count; l++)
                {
                    int integer = customerOffset is int a ? r * ((l + r - 1 - a) / r + 1) : l + r - 1;
                    c += leadPmf[l] * integer;
                }

                return c;
            }

            case CoverRule.LotsInTransit:
            {
                double c = 0.0;
                for (int l = 1; l < leadPmf.Count; l++)
                {
                    c += leadPmf[l] * r * ((l + r - 1) / r);
                }

                return c;
            }

            default:
                throw new ArgumentException("CoverRules covers options A, B and C only; Integer is the PhaseTarget path.", nameof(rule));
        }
    }

    /// <summary>
    /// Weights of the periods t0+1 … t0+H of the realised side of the protection-period error for cover c, so that its mean is
    /// c·μ for the stage's input. Smooth: <see cref="ProtectionWindow.Weights"/>(c) (Σa = Σa² = c; iid input: Var = c·σ²).
    /// Lumpy: the same two-weight rule over the lumps at offsets a, a + R, … with Σ = c/R lumps (a convention: lumps are orders,
    /// not iid). Zero weight elsewhere. H = max(⌈planning Lp⌉, last weighted period), so the error settles no earlier than the
    /// integer path; for a point-mass L under (A) or (B) this is the integer window (t0, t0+Lp] with weight 1 on every lump.
    /// </summary>
    public static double[] Window(double cover, int r, int? customerOffset, int planningSpan)
    {
        double[] w;
        int step;
        int first;
        if (customerOffset is int a)
        {
            w = ProtectionWindow.Weights(SnapToWholeLumps(cover / r));
            step = r;
            first = a - 1;
        }
        else
        {
            w = ProtectionWindow.Weights(cover);
            step = 1;
            first = 0;
        }

        int last = first + (w.Length - 1) * step;
        var full = new double[Math.Max(planningSpan, last + 1)];
        for (int j = 0; j < w.Length; j++)
        {
            full[first + j * step] = w[j];
        }

        return full;
    }

    /// <summary>
    /// A lump count within 1e-9 of an integer is that integer. Covers are sums of pmf × integer, and the pmf sums to
    /// 1 − 1.1e-16 in floating point, so a cover of exactly R lumps can come out as R·(1 − 1e-16).
    /// </summary>
    private static double SnapToWholeLumps(double lumps)
    {
        double nearest = Math.Round(lumps);
        return Math.Abs(lumps - nearest) < 1e-9 ? nearest : lumps;
    }

    private static int X(int l, int u, int r) => l + ((((u + 1 - l) % r) + r) % r);
}
