namespace Bullwhip.Core.Chain;

/// <summary>
/// Lead-time distribution of the base case: a mean transit time of 10 days on integer weeks with a coefficient of
/// variation of the lead time of 0.30 (DECISIONS C24, C27, C75), and its shortened variants for countermeasure 02.
/// </summary>
/// <remarks>
/// <para>
/// Mapping (plan §2, R10): model L = calendar transit weeks + 1. An order placed at the end of t is available at the start
/// of t + L, so L = 1 is zero transit (Chen et al. 2000, Management Science 46(3), p. 437: L = 1 if there is no lead time;
/// Chen, Ryan &amp; Simchi-Levi 2000, Naval Research Logistics 47(4), p. 271: L is the lead time plus 1). Kaplan's lag
/// T = L − 1 (<see cref="KaplanLeadTime"/>) is therefore the transit time in weeks.
/// </para>
/// <para>
/// Targets. The transit time has mean 10/7 ≈ 1.43 weeks, so E[L] = <see cref="MeanLeadTime"/> = 2.43. The coefficient of
/// variation of the lead time is CV(L) = <see cref="LeadTimeCv"/> = 0.30, the median reported by Robb (DECISIONS C24).
/// </para>
/// <para>
/// Distribution. Kaplan's no-crossing model needs a nondecreasing hazard h_a = P(T = a | T ≥ a)
/// (<see cref="KaplanLeadTime"/>). The transit time is at least one week (h_0 = 0). On T in {1, 2, 3} the largest CV(L)
/// with a nondecreasing hazard and E[L] = 2.43 is 0.278 (reached at h_1 = h_2), so the support is T in {1, 2, 3, 4}, i.e.
/// L in {2, 3, 4, 5}, with h_1 = h_2 = h, h_3 free and h_4 = 1. With the survival probabilities
/// P(T &gt; 1) = 1 − h, P(T &gt; 2) = (1 − h)², P(T &gt; 3) = (1 − h)²·(1 − h_3), the two targets are
/// E[T] = 1 + Σ_{a ≥ 1} P(T &gt; a) = 1.43 and E[T²] = 1 + Σ_{a ≥ 1} (2a + 1)·P(T &gt; a) = (0.30·2.43)² + 1.43²; their
/// solution is the hazard table of this class. The pmf follows as P(T = a) = h_a·Π_{s &lt; a}(1 − h_s):
/// P(L = 2, 3, 4, 5) = 0.6897, 0.2140, 0.0728, 0.0234, and CV(T) = 0.510.
/// </para>
/// <para>
/// Countermeasure 02 (shorter lead time; at w = 1 also deterministic). The dose w in [0, 1] raises every hazard of the transit time
/// from week 1 on: h'_a = h_a + w·(1 − h_a), a ≥ 1. A shipment that would stay in transit after week a arrives in that
/// week with probability w instead. The raised hazards stay nondecreasing, so every dose is a Kaplan lead time; mixing
/// the base pmf with a point mass at T = 1 raises h_1 only and is not one. w = 0 is the base case, w = 1 the
/// deterministic lead time L = 2 (T = 1).
/// </para>
/// </remarks>
public static class BaseLeadTime
{
    /// <summary>E[L] of the base case in weeks: transit 1.43 weeks plus 1.</summary>
    public const double MeanLeadTime = 2.43;

    /// <summary>CV(L) of the base case: standard deviation of L over E[L].</summary>
    public const double LeadTimeCv = 0.30;

    /// <summary>Hazard h_a = P(T = a | T ≥ a) of the base transit time, a = 0..4 (class remarks).</summary>
    private static readonly double[] TransitHazard = { 0.0, 0.689721594469328, 0.689721594469328, 0.7564324234714825, 1.0 };

    /// <summary>
    /// Lag pmf P(T = i) of one link at the dose <paramref name="w"/> of countermeasure 02 (class remarks); w = 0 is the
    /// base case with i = 0..4. Entries after the last possible lag are cut (<see cref="KaplanLeadTime"/> needs
    /// P(T = m) &gt; 0 at the last entry), so w = 1 gives the point mass at T = 1.
    /// </summary>
    public static double[] LagPmf(double w = 0)
    {
        if (!(w >= 0 && w <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(w), w, "The dose must be in [0, 1].");
        }

        var pmf = new List<double>();
        double survival = 1.0;
        for (int a = 0; a < TransitHazard.Length && survival > 0; a++)
        {
            double hazard = RaisedHazard(a, w);
            pmf.Add(survival * hazard);
            survival *= 1 - hazard;
        }

        return pmf.ToArray();
    }

    /// <summary>h'_a = h_a + w·(1 − h_a) for a ≥ 1 (exactly 1 at w = 1); h'_0 = 0, the transit time is at least one week.</summary>
    private static double RaisedHazard(int a, double w)
    {
        if (a == 0)
        {
            return 0.0;
        }

        return w == 1 ? 1.0 : TransitHazard[a] + w * (1 - TransitHazard[a]);
    }
}
