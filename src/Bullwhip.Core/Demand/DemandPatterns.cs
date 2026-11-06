using Bullwhip.Core.Rng;

namespace Bullwhip.Core.Demand;

/// <summary>The four demand patterns of DECISIONS C51 (trend and jump are separate scenarios).</summary>
public enum DemandPattern
{
    Stable,
    Seasonal,
    Trend,
    Jump,
    Promotion,
}

/// <summary>
/// Parameters of the mean profile m_t of DECISIONS C51; demand is D_t = m_t + σ·N(0,1) with iid noise.
/// Stable: m_t = μ. Seasonal: m_t = μ(1 + A·sin(2πt/Period)).
/// Trend: m_t = μ for t &lt; T0, else μ + g(t − T0) with g = (TrendEndFactor − 1)·μ / (TrendEndTime − T0)
/// (so m = TrendEndFactor·μ at t = TrendEndTime = warm-up + measurement = 2200; g = 0.025 at the defaults).
/// Jump: m_t = μ(1 + J) for t ≥ TJ, else μ. Promotion, with w = t mod PromoPeriod: μ(1 + P) for w &lt; d,
/// μ(1 − Q) for d ≤ w &lt; d + e, else μ.
/// </summary>
public sealed record DemandPatternParameters
{
    public double Mu { get; init; } = 100;
    public double Sigma { get; init; } = 10;
    public double Amplitude { get; init; } = 0.2;
    public int Period { get; init; } = 52;
    public int TrendStart { get; init; } = 200;
    public int TrendEndTime { get; init; } = 2200;
    public double TrendEndFactor { get; init; } = 1.5;
    public double JumpSize { get; init; } = 0.3;
    public int JumpTime { get; init; } = 1200;
    public double PromoSize { get; init; } = 0.5;
    public int PromoDuration { get; init; } = 4;
    public double DropSize { get; init; } = 0.3;
    public int DropDuration { get; init; } = 8;
    public int PromoPeriod { get; init; } = 52;

    /// <summary>Trend slope g per week.</summary>
    public double TrendSlope => (TrendEndFactor - 1.0) * Mu / (TrendEndTime - TrendStart);
}

/// <summary>Mean profile and generator of the C51 demand patterns (pure, no I/O).</summary>
public static class DemandPatterns
{
    /// <summary>Expected demand m_t of week t.</summary>
    public static double MeanProfile(DemandPattern pattern, DemandPatternParameters p, int t)
    {
        if (pattern == DemandPattern.Seasonal && p.Period <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(p), "Period must be positive.");
        }

        if (pattern == DemandPattern.Promotion && p.PromoPeriod <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(p), "PromoPeriod must be positive.");
        }

        if (pattern == DemandPattern.Trend && p.TrendEndTime == p.TrendStart)
        {
            throw new ArgumentException("TrendEndTime must differ from TrendStart.", nameof(p));
        }

        return pattern switch
        {
            DemandPattern.Stable => p.Mu,
            DemandPattern.Seasonal => p.Mu * (1.0 + p.Amplitude * Math.Sin(2.0 * Math.PI * t / p.Period)),
            DemandPattern.Trend => t < p.TrendStart ? p.Mu : p.Mu + p.TrendSlope * (t - p.TrendStart),
            DemandPattern.Jump => t < p.JumpTime ? p.Mu : p.Mu * (1.0 + p.JumpSize),
            DemandPattern.Promotion => Promotion(p, t),
            _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
        };
    }

    private static double Promotion(DemandPatternParameters p, int t)
    {
        int w = t % p.PromoPeriod;
        if (w < p.PromoDuration)
        {
            return p.Mu * (1.0 + p.PromoSize);
        }

        return w < p.PromoDuration + p.DropDuration ? p.Mu * (1.0 - p.DropSize) : p.Mu;
    }

    /// <summary>
    /// D_0 = m_0 without a draw; D_t = m_t + σ·N(0,1) for t = 1..length−1, the t-th normal of the replication stream (C52).
    /// Stable is exactly <see cref="Ar1Demand.Generate"/> with ρ = 0, so all patterns share the noise of the base case
    /// and Seasonal with A = 0 is bit-identical to Stable.
    /// </summary>
    public static double[] Generate(DemandPattern pattern, DemandPatternParameters p, int length, SplitMix64 rng)
    {
        if (pattern == DemandPattern.Stable)
        {
            return Ar1Demand.Generate(length, p.Mu, 0.0, p.Sigma, rng);
        }

        var demand = new double[length];
        if (length > 0)
        {
            demand[0] = MeanProfile(pattern, p, 0);
        }

        for (int t = 1; t < length; t++)
        {
            demand[t] = MeanProfile(pattern, p, t) + p.Sigma * rng.NextStandardNormal();
        }

        return demand;
    }
}
