using Bullwhip.Core.Chain;
using Bullwhip.Core.Validation;
using Xunit;

namespace Bullwhip.Tests;

// Counter escalation of the backlog cost (DECISIONS C34): shortage cost of stage k in period t is
// b_k · backlog_{k,t} · (1 + κ · n_{k,t}), n_{k,t} = consecutive periods up to t with backlog > 1e-9, counted over the whole run.
public class EscalationTests
{
    private static ChainConfig Config(double kappa, double[] backlogCost) => new()
    {
        LeadTimes = Enumerable.Repeat(1.43, 4).ToArray(),
        StochasticLeadTimes = Enumerable.Repeat(new KaplanLeadTime(new[] { 0.57, 0.43 }), 4).ToArray(),
        LeadTimeSeed = GateA.LeadTimeSeed(0), R = 4, Z = new[] { 0.0, 0.0, 0.0, 0.0 },
        ShortageCost = backlogCost, ShortageEscalation = kappa, AllowNegativeOrders = false,
        InitialMean = 100, Warmup = 200, Window = 8,
    };

    [Fact]
    public void ShortageCost_MatchesCounterFormulaOnTheBacklogSeries()
    {
        double[] b = { 0.5, 0.3, 0.2, 0.1 };
        const double kappa = 1.5;
        var res = SerialChain.Run(Config(kappa, b), GateA.Demand(0, 2200, 100, 0, 10));

        for (int k = 0; k < 4; k++)
        {
            double expected = 0.0;
            int weeks = 0;
            for (int t = 0; t < res.BacklogPeriod[k].Length; t++)
            {
                double backlog = res.BacklogPeriod[k][t];
                weeks = backlog > 1e-9 ? weeks + 1 : 0;
                if (t >= 200)
                {
                    expected += b[k] * backlog * (1.0 + kappa * weeks);
                }
            }

            Assert.Equal(expected, res.ShortageCost[k], 9);
        }
    }

    [Fact]
    public void ZeroEscalation_IsThePlainPerUnitPeriodCost()
    {
        double[] b = { 0.5, 0.3, 0.2, 0.1 };
        var res = SerialChain.Run(Config(0.0, b), GateA.Demand(0, 2200, 100, 0, 10));

        for (int k = 0; k < 4; k++)
        {
            double plain = 0.0;
            for (int t = 200; t < res.BacklogPeriod[k].Length; t++)
            {
                plain += b[k] * res.BacklogPeriod[k][t];
            }

            Assert.Equal(plain, res.ShortageCost[k], 9);
        }
    }

    [Fact]
    public void NegativeEscalation_IsRejected()
    {
        var demand = GateA.Demand(0, 2200, 100, 0, 10);
        Assert.Throws<ArgumentException>(() => SerialChain.Run(Config(-0.1, new double[] { 0.5, 0.3, 0.2, 0.1 }), demand));
    }
}
