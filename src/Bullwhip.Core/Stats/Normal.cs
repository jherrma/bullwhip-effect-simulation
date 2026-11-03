namespace Bullwhip.Core.Stats;

/// <summary>Standard normal CDF and quantile. Erf uses Abramowitz–Stegun 7.1.26 (abs. error ~1.5e-7).</summary>
public static class Normal
{
    public static double Cdf(double z)
    {
        return 0.5 * Erfc(-z / Math.Sqrt(2.0));
    }

    public static double Quantile(double p)
    {
        if (p <= 0.0 || p >= 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(p), "p must lie in (0, 1).");
        }

        double lo = -40.0;
        double hi = 40.0;
        for (int i = 0; i < 200; i++)
        {
            double mid = 0.5 * (lo + hi);
            if (Cdf(mid) < p)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return 0.5 * (lo + hi);
    }

    private static double Erfc(double x)
    {
        double sign = x < 0 ? -1.0 : 1.0;
        double ax = Math.Abs(x);
        double t = 1.0 / (1.0 + 0.3275911 * ax);
        double poly = ((((1.061405429 * t - 1.453152027) * t + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t;
        double erfAbs = 1.0 - poly * Math.Exp(-ax * ax);
        return 1.0 - sign * erfAbs;
    }
}
