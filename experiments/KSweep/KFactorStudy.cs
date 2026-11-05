using Bullwhip.Core.Costs;

namespace KSweep;

/// <summary>
/// The C28 procedure at one K scale on fresh replications (DECISIONS C37): training 200–249, holdout 6000–6199.
/// </summary>
public static class KFactorStudy
{
    /// <summary>BME 2025 cost per order process in euros (DECISIONS F4, C36).</summary>
    public const double BmeOrderCost = 121.75;

    /// <summary>Runs the sweep at <paramref name="scale"/> and reports each line through <paramref name="say"/>.</summary>
    public static void Run(double scale, Action<string> say)
    {
        double baseOrderCost = BaseCostModel.OrderCost(new CostParameters { OrderCostScale = 1 })[0];
        say($"Base K_1 = {baseOrderCost:F6}; BME / K_1 = {BmeOrderCost / baseOrderCost:F6}; scale used = {scale:F6}");
        say("Training replications 200-249, holdout replications 6000-6199");
        var settings = new SweepSettings { TrainFirst = 200, TestFirst = 6000, Scales = new[] { scale } };
        new KSweepRunner(settings).Run(say);
    }
}
