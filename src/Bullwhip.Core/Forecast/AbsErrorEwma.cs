namespace Bullwhip.Core.Forecast;

/// <summary>
/// Exponentially smoothed mean absolute one-period forecast error of Chen, Ryan &amp; Simchi-Levi (2000), NRL 47(4), Eq. (5),
/// p. 273: Δ̂_t = α_s·|e_{t−1}| + (1 − α_s)·Δ̂_{t−1}. The paper's process started infinitely long ago, so it sets no start.
/// Start (our choice): the n-th error gets the weight max(α_s, 1/n). Δ̂ is the plain mean of the first ⌊1/α_s⌋ errors and
/// follows the recursion from there. Every Δ̂ is a convex combination of the errors seen, so E[Δ̂] = E|e| from the first
/// error on (stationary errors): no start-up bias towards an arbitrary initial value (0 would bias σ̂ low for ≈ 1/α_s
/// periods). Zero before the first error.
/// </summary>
public sealed class AbsErrorEwma
{
    private readonly double _alpha;
    private long _count;

    public AbsErrorEwma(double alpha)
    {
        if (!(alpha > 0.0 && alpha <= 1.0))
        {
            throw new ArgumentOutOfRangeException(nameof(alpha), "The smoothing constant α_s must be in (0, 1].");
        }

        _alpha = alpha;
    }

    /// <summary>Δ̂, the smoothed mean absolute error.</summary>
    public double Mad { get; private set; }

    public void Add(double error)
    {
        _count++;
        double w = Math.Max(_alpha, 1.0 / _count);
        Mad = w * Math.Abs(error) + (1.0 - w) * Mad;
    }
}
