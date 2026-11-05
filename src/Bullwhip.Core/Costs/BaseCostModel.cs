namespace Bullwhip.Core.Costs;

/// <summary>
/// Inputs of the base cost model. Rates are annual where the name says so and are converted to weekly by
/// <see cref="WeeksPerYear"/>. The defaults are the base case of the thesis (DECISIONS F4, C28, research/k-bestellkosten.md).
/// </summary>
public sealed record CostParameters
{
    /// <summary>Mean weekly demand μ (units per week).</summary>
    public double Mu { get; init; } = 100;

    /// <summary>Number of stages N (index 0 is the retailer).</summary>
    public int Stages { get; init; } = 4;

    /// <summary>Storage rate s per year (share of the item value). With the capital rate i = 8 % the total is 10 % (APQC median, F4).</summary>
    public double StorageRate { get; init; } = 0.02;

    /// <summary>Capital rate i per year (share of the item value).</summary>
    public double CapitalRate { get; init; } = 0.08;

    /// <summary>Value of one unit at the retailer (stage 0).</summary>
    public double RetailerValue { get; init; } = 30;

    /// <summary>Value of one unit at the upstream-most stage. Intermediate stages are geometric between the two (F4, option 2).</summary>
    public double SupplierValue { get; init; } = 10;

    /// <summary>Weeks per year used to convert annual rates to weekly ones.</summary>
    public double WeeksPerYear { get; init; } = 52;

    /// <summary>Shortage cost b in units of the retailer holding rate h₁ (b = 9·h₁ gives a critical ratio of 0.9).</summary>
    public double ShortageToHoldingRatio { get; init; } = 9;

    /// <summary>Order interval R at which the fixed cost K is calibrated (the Economic Order Quantity optimum is R* = 4).</summary>
    public double CalibrationInterval { get; init; } = 4;

    /// <summary>Multiplier on the calibrated K. 1 is the base case; the sweep of C28 uses 0.25 to 4.</summary>
    public double OrderCostScale { get; init; } = 1;
}

/// <summary>
/// Cost rates of the serial chain, derived from <see cref="CostParameters"/>. All costs are in the same monetary units as the
/// item values, per week.
/// </summary>
/// <remarks>
/// <para>
/// Item value per stage (geometric, our assumption F4): v_k = v_0·(v_{N−1}/v_0)^(k/(N−1)). The retailer keeps 30 and the
/// upstream-most stage 10 (ratio 3.0 from Alvarez-Blaser et al. 2025, NBER w34110).
/// </para>
/// <para>
/// Holding and capital cost are charged separately in the simulation (<see cref="ChainResult.HoldingCost"/>,
/// <see cref="ChainResult.CapitalCost"/>): holding h_k = s·v_k/52 per unit-week on on-hand stock, capital i/52·v_k per unit-week on
/// on-hand stock and pipeline. Their sum is the economic holding rate (s + i)·v_k/52 used for calibration.
/// </para>
/// <para>
/// Shortage cost b = 9·h₁ at the retailer, with h₁ = (s + i)·v₁/52, gives the critical ratio b/(b + h₁) = 0.9.
/// </para>
/// <para>
/// Fixed order cost K_k. Cost per week C(R) = K/R + h·μ·R/2 (fixed cost per order over the interval plus average cycle
/// stock). Its minimum is at R* = √(2K/(h·μ)), so requiring R* = R_cal gives K = R_cal²·h·μ/2. With R_cal = 4 this is
/// K = 8·μ·h, and with h = (s + i)·v/52 it is K_k = 8·μ·(s + i)·v_k/52. Results: 46.2, 32.0, 22.2, 15.4. The constant 8 is
/// specific to R_cal = 4 and changes with CalibrationInterval. The square-root formula is the standard economic order quantity
/// result; the calibration target R_cal = 4 is our choice, not a value from the literature.
/// </para>
/// </remarks>
public static class BaseCostModel
{
    /// <summary>Item value per stage, geometric from the retailer value to the upstream value.</summary>
    public static double[] StageValues(CostParameters p)
    {
        var values = new double[p.Stages];
        for (int k = 0; k < p.Stages; k++)
        {
            double share = p.Stages == 1 ? 0.0 : (double)k / (p.Stages - 1);
            values[k] = p.RetailerValue * Math.Pow(p.SupplierValue / p.RetailerValue, share);
        }

        return values;
    }

    /// <summary>Holding cost per unit-week on on-hand stock, h_k^storage = s·v_k/52 (charged as <see cref="ChainResult.HoldingCost"/>).</summary>
    public static double[] StorageRatePerUnitWeek(CostParameters p)
    {
        return StageValues(p).Select(v => p.StorageRate * v / p.WeeksPerYear).ToArray();
    }

    /// <summary>Capital rate per week, i/52 (multiplied by the item value in <see cref="ChainResult.CapitalCost"/>).</summary>
    public static double CapitalRatePerWeek(CostParameters p)
    {
        return p.CapitalRate / p.WeeksPerYear;
    }

    /// <summary>Economic holding rate per unit-week, h_k = (s + i)·v_k/52 (storage plus capital), used for calibration.</summary>
    public static double[] HoldingRatePerUnitWeek(CostParameters p)
    {
        return StageValues(p).Select(v => (p.StorageRate + p.CapitalRate) * v / p.WeeksPerYear).ToArray();
    }

    /// <summary>Shortage cost per unit-week of backlog at the retailer, b = 9·h₁.</summary>
    public static double ShortageCost(CostParameters p)
    {
        return p.ShortageToHoldingRatio * HoldingRatePerUnitWeek(p)[0];
    }

    /// <summary>
    /// Shortage cost per unit-week of backlog at every stage: b_1 = 9·h₁ at the retailer and
    /// b_k = <paramref name="upstreamMultiplier"/>·9·h_k at the stages k ≥ 2 (a sensitivity; 0 charges only the retailer).
    /// </summary>
    public static double[] ShortageCostPerStage(CostParameters p, double upstreamMultiplier)
    {
        var holding = HoldingRatePerUnitWeek(p);
        var b = new double[p.Stages];
        for (int k = 0; k < p.Stages; k++)
        {
            double multiplier = k == 0 ? 1.0 : upstreamMultiplier;
            b[k] = multiplier * p.ShortageToHoldingRatio * holding[k];
        }

        return b;
    }

    /// <summary>Fixed cost per order K_k = OrderCostScale·R_cal²·h_k·μ/2 (8·μ·h_k for R_cal = 4).</summary>
    public static double[] OrderCost(CostParameters p)
    {
        double factor = p.OrderCostScale * p.CalibrationInterval * p.CalibrationInterval / 2.0 * p.Mu;
        return HoldingRatePerUnitWeek(p).Select(h => factor * h).ToArray();
    }

    /// <summary>Optimal order interval R* = √(2K/(h·μ)) of one stage for the given fixed cost and holding rate.</summary>
    public static double OptimalInterval(double orderCost, double holdingRate, double mu)
    {
        return Math.Sqrt(2.0 * orderCost / (holdingRate * mu));
    }
}
