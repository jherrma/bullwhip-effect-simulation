namespace Bullwhip.Core.Stats;

public static class Descriptive
{
    /// <summary>Population variance of x[from..to).</summary>
    public static double Variance(double[] x, int from, int to)
    {
        int n = to - from;
        double mean = 0.0;
        for (int i = from; i < to; i++)
        {
            mean += x[i];
        }

        mean /= n;
        double ss = 0.0;
        for (int i = from; i < to; i++)
        {
            double d = x[i] - mean;
            ss += d * d;
        }

        return ss / n;
    }

    /// <summary>Mean of x[from..to).</summary>
    public static double Mean(double[] x, int from, int to)
    {
        double sum = 0.0;
        for (int i = from; i < to; i++)
        {
            sum += x[i];
        }

        return sum / (to - from);
    }
}
