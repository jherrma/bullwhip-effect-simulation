namespace Bullwhip.Core.Chain;

/// <summary>
/// Phase-aware cover of the order-up-to level S for a stage with lumpy incoming demand (R &gt; 1, stage k &gt; 0).
/// </summary>
/// <remarks>
/// Stage k orders at t0 ≡ φ_k (mod R). Its incoming demand is the order of its customer k−1: one lump of mean R·μ per
/// R periods, at t ≡ φ_{k−1} (mod R). The order of t0 arrives at t0+L, the next one (t0+R) at t0+R+L, so for
/// t in t0+L..t0+Lp (Lp = L+R−1) the net stock is NS(t) = S − D(t0, t].
///
/// Lumps in the window: a lump at t0 itself is already in IP (k−1 orders before k reads demand in the same period), so
/// the first one after t0 lands at t0+a, a = ((φ_{k−1} − φ_k − 1) mod R) + 1 in 1..R, and (t0, t0+Lp] holds
/// n = floor((Lp − a)/R) + 1 lumps (Lp ≥ R ≥ a for L ≥ 1). So E[D(t0, t0+Lp]] = n·R·μ, not Lp·μ.
///
/// Net stock at the end of the protection period: E[NS(t0+Lp)] = S − n·R·μ. With S = Lp·μ + z·σ̂ this is
/// z·σ̂ + (Lp − nR)·μ, an offset between −(R−1)·μ and +(R−1)·μ (R=4, L=2: +μ for phases 0000 and 0123, −3μ for a
/// lump at t0+1). The target S = n·R·μ̂ + z·σ̂ makes it z·σ̂, the intended safety level.
///
/// Cycle stock: exactly one lump lands in t0+L..t0+Lp, at t0+L+c, c = (a − L) mod R. Before it NS = z·σ̂ + R·μ,
/// from it on NS = z·σ̂, so the mean net stock over the R periods is z·σ̂ + c·μ. At z = 0 and constant demand the mean
/// on-hand is the deterministic cycle stock c·μ: R=4, L=2, μ=100, phases 0000 → a=4, n=1, c=2, 200; phases 0123 →
/// a=3, n=1, c=1, 100; phases 0321 → a=1, n=2, c=3, 300 (smooth demand at the retailer: (R−1)/2·μ = 150).
/// R = 1: a = 1, n = Lp, i.e. the plain Lp·μ̂.
/// </remarks>
public static class PhaseTarget
{
    /// <summary>Number n of the customer's order times in the protection window (t0, t0+Lp] of an own order time t0.</summary>
    public static int Lumps(int lp, int r, int ownPhase, int customerPhase)
    {
        int a = (((customerPhase - ownPhase - 1) % r) + r) % r + 1;
        return (lp - a) / r + 1;
    }
}
