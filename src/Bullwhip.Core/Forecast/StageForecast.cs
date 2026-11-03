namespace Bullwhip.Core.Forecast;

/// <summary>
/// Moving-average demand forecast per period, on R-period buckets (plan R1). With R = 1 this is MA(p).
/// Buckets close at the stage's order times. The mean is taken over the buckets available so far.
/// </summary>
public sealed class StageForecast
{
    private readonly double[] _buckets;
    private readonly int[] _periods; // real periods (observations) behind each closed bucket
    private readonly int _r;
    private int _next;
    private int _count;
    private double _sum;
    private double _current;
    private int _currentPeriods; // periods in the open bucket (observations, plus the pre-start share after Prefill)
    private int _windowPeriods;  // running sum of _periods over the buckets in the window

    public StageForecast(int window, int r)
    {
        if (r < 1 || window < r || window % r != 0)
        {
            throw new ArgumentException("Window must be a positive multiple of R.");
        }

        _r = r;
        _buckets = new double[window / r];
        _periods = new int[window / r];
    }

    public void Observe(double x)
    {
        _current += x;
        _currentPeriods++;
    }

    public void CloseBucket()
    {
        if (_count == _buckets.Length)
        {
            _sum -= _buckets[_next];
            _windowPeriods -= _periods[_next];
        }
        else
        {
            _count++;
        }

        _buckets[_next] = _current;
        _periods[_next] = _currentPeriods;
        _sum += _current;
        _windowPeriods += _currentPeriods;
        _next = (_next + 1) % _buckets.Length;
        _current = 0.0;
        _currentPeriods = 0;
    }

    /// <summary>
    /// Per-period mean demand estimate: window sum / real periods in the window. Closed buckets only; the open bucket is
    /// excluded (as before). Without Prefill the first bucket holds phase + 1 &lt; R periods, so dividing by R would bias the
    /// mean low; the period counts remove that. Prefilled and R = 1 cases: every bucket has exactly R periods, so the
    /// denominator equals count * R as before.
    /// </summary>
    public double Mean => _windowPeriods == 0 ? 0.0 : _sum / _windowPeriods;

    /// <summary>
    /// Pre-fills the forecast with a constant per-period mean (plan S11). The stage's first bucket closes at its first
    /// order time t = phase and then covers (phase − R, phase], of which only periods 0..phase are real. The R − 1 − phase
    /// periods before t = 0 are history at the same mean, so the open bucket starts with that share. The first close is
    /// then a full R-period bucket and mu-hat(0) = mu. R = 1 (phase 0): the share is 0 and nothing changes.
    /// Period counts: each prefilled bucket counts R periods, and the pre-start share counts R − 1 − phase periods in the
    /// open bucket (they are history at mu), so the first close has R periods like every other bucket.
    /// </summary>
    public void Prefill(double mu, int phase = 0)
    {
        for (int i = 0; i < _buckets.Length; i++)
        {
            _current = mu * _r;
            _currentPeriods = _r;
            CloseBucket();
        }

        _current = mu * (_r - 1 - phase);
        _currentPeriods = _r - 1 - phase;
    }
}
