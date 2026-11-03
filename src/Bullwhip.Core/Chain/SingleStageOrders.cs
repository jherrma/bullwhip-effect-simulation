using Bullwhip.Core.Forecast;

namespace Bullwhip.Core.Chain;

/// <summary>
/// Order arithmetic of one retailer under order-up-to with z = 0 and a moving-average forecast (Chen et al. 2000, Eq. 4):
///   y_t = L · MA_p(D_{t−1}, …, D_{t−p}),   q_t = y_t − y_{t−1} + D_{t−1}.
/// No clipping: negative orders are returned as they are (validation mode).
/// Entries before index p+1 are NaN.
/// </summary>
public static class SingleStageOrders
{
    public static double[] Compute(double[] demand, int leadTime, int window)
    {
        var orders = new double[demand.Length];
        var forecast = new MovingAverage(window);
        double previousTarget = 0.0;
        bool havePrevious = false;

        for (int t = 0; t < demand.Length; t++)
        {
            if (forecast.IsFull)
            {
                double target = leadTime * forecast.Mean;
                orders[t] = havePrevious
                    ? target - previousTarget + demand[t - 1]
                    : double.NaN;
                previousTarget = target;
                havePrevious = true;
            }
            else
            {
                orders[t] = double.NaN;
            }

            forecast.Observe(demand[t]);
        }

        return orders;
    }
}
