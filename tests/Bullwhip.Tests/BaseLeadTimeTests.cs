using Bullwhip.Core.Chain;
using Bullwhip.Core.Rng;
using Bullwhip.Core.Validation;
using Xunit;
using Xunit.Abstractions;

namespace Bullwhip.Tests;

/// <summary>
/// Base-case lead time (<see cref="BaseLeadTime"/>, DECISIONS C24, C75): 10 days of mean transit mapped with model
/// L = calendar transit weeks + 1 (plan §2, R10) and CV(L) = 0.30, so the transit T = L − 1 is 1 to 4 weeks,
/// E[T] = 1.43 and E[L] = 2.43. Countermeasure 02 raises the transit hazards by the dose w.
/// </summary>
public class BaseLeadTimeTests
{
    private const double TransitDays = 10.0;
    private readonly ITestOutputHelper _out;

    public BaseLeadTimeTests(ITestOutputHelper output)
    {
        _out = output;
    }

    [Fact]
    public void BasePmf_IsOneToFourWeeksOfTransit()
    {
        // P(T = a) = h_a·Π_{s<a}(1 − h_s) with h = 0.689721594469328 at a = 1, 2 and h_3 = 0.7564324234714825, h_4 = 1.
        const double H = 0.689721594469328;
        const double H3 = 0.7564324234714825;
        var pmf = BaseLeadTime.LagPmf();
        var expected = new[] { 0.0, H, (1 - H) * H, (1 - H) * (1 - H) * H3, (1 - H) * (1 - H) * (1 - H3) };
        Assert.Equal(5, pmf.Length);
        Assert.Equal(0.0, pmf[0]);
        for (int i = 1; i < 5; i++)
        {
            Assert.Equal(expected[i], pmf[i], 12);
        }

        // Four-digit values cited in the thesis.
        var cited = new[] { 0.0, 0.6897, 0.2140, 0.0728, 0.0234 };
        for (int i = 0; i < 5; i++)
        {
            Assert.InRange(pmf[i], cited[i] - 5e-5, cited[i] + 5e-5);
        }

        Assert.Equal(1.0, pmf.Sum(), 12);
    }

    [Fact]
    public void BaseMoments_MatchTenDaysOfTransitPlusOneAndCvPointThree()
    {
        // Moments from the pmf directly (not through KaplanLeadTime): E[L] = Σ (i + 1)·P(T = i), Var(L) = Var(T).
        var pmf = BaseLeadTime.LagPmf();
        double meanLead = pmf.Select((x, i) => (i + 1) * x).Sum();
        double variance = pmf.Select((x, i) => (i + 1 - meanLead) * (i + 1 - meanLead) * x).Sum();
        double sd = Math.Sqrt(variance);
        _out.WriteLine($"E[T]={meanLead - 1:0.000000000000} E[L]={meanLead:0.000000000000} Var(L)={variance:0.000000000000} CV(L)={sd / meanLead:0.000000000000} CV(T)={sd / (meanLead - 1):0.000000000000}");

        // 10 days = 1.4286 weeks; the distribution carries the two-digit value 1.43.
        Assert.InRange(meanLead - 1, TransitDays / 7 - 0.005, TransitDays / 7 + 0.005);
        Assert.InRange(meanLead, 2.43 - 1e-9, 2.43 + 1e-9);
        Assert.InRange(sd / meanLead, 0.30 - 1e-9, 0.30 + 1e-9);
        Assert.Equal(2.43, BaseLeadTime.MeanLeadTime);
        Assert.Equal(0.30, BaseLeadTime.LeadTimeCv);

        // CV(T) = 0.30·2.43/1.43 = 0.5098: the same standard deviation over the mean transit.
        Assert.InRange(sd / (meanLead - 1), 0.5098 - 5e-5, 0.5098 + 5e-5);

        var lag = new KaplanLeadTime(pmf);
        Assert.Equal(meanLead, lag.MeanLeadTime, 12);
        Assert.Equal(variance, lag.LagVariance, 12);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.10)]
    [InlineData(0.20)]
    [InlineData(0.30)]
    [InlineData(1)]
    public void KaplanMatrix_ImpliesThePmf_AtEveryDose(double w)
    {
        var target = BaseLeadTime.LagPmf(w);
        var implied = KaplanLeadTime.LagPmfFromMatrix(new KaplanLeadTime(target).TransitionMatrix(), out double leftover);
        Assert.InRange(leftover, 0, 1e-12);
        for (int i = 0; i < target.Length; i++)
        {
            Assert.InRange(implied[i], target[i] - 1e-12, target[i] + 1e-12);
        }
    }

    [Fact]
    public void Sampler_GivesLeadTimesOfTwoToFiveWeeksWithMean2Point43()
    {
        // 200 000 dispatches of one stream; tolerance ±0.01 on the mean and on the share of every lead time.
        const int Last = 200_000;
        var pmf = BaseLeadTime.LagPmf();
        var lag = new KaplanLeadTime(pmf);
        var due = lag.DrawArrivals(new SplitMix64(GateA.LeadTimeSeed(0)), Last);
        double sum = 0;
        var count = new int[6];
        for (int s = 0; s <= Last; s++)
        {
            int leadTime = due[s + lag.MaxLag + 1] - s;
            Assert.InRange(leadTime, 2, 5);
            sum += leadTime;
            count[leadTime]++;
        }

        double mean = sum / (Last + 1);
        _out.WriteLine($"sampled E[L]={mean:0.0000} (2.43), P(L=2..5)={string.Join(" / ", count.Skip(2).Select(c => ((double)c / (Last + 1)).ToString("0.0000")))}");
        Assert.InRange(mean, 2.43 - 0.01, 2.43 + 0.01);
        for (int leadTime = 2; leadTime <= 5; leadTime++)
        {
            double share = (double)count[leadTime] / (Last + 1);
            Assert.InRange(share, pmf[leadTime - 1] - 0.01, pmf[leadTime - 1] + 0.01);
        }
    }

    [Theory]
    [InlineData(0.10, 2.3743, 0.2852)]
    [InlineData(0.20, 2.3218, 0.2688)]
    [InlineData(0.30, 2.2724, 0.2508)]
    public void Dose_RaisesEveryTransitHazard(double w, double meanLead, double cv)
    {
        // h'_a = h_a + w·(1 − h_a) for a ≥ 1, read back from the pmf as P(T = a)/P(T ≥ a). The moments follow from the
        // survival probabilities (1 − w)^a·P(T > a) of the base: E[L] = 2 + Σ_{a=1..3} (1 − w)^a·P(T > a).
        var basePmf = BaseLeadTime.LagPmf();
        var pmf = BaseLeadTime.LagPmf(w);
        Assert.Equal(5, pmf.Length);
        Assert.Equal(0.0, pmf[0]);
        for (int a = 1; a <= 4; a++)
        {
            double baseHazard = basePmf[a] / basePmf.Skip(a).Sum();
            double hazard = pmf[a] / pmf.Skip(a).Sum();
            Assert.Equal(baseHazard + w * (1 - baseHazard), hazard, 12);
        }

        var lag = new KaplanLeadTime(pmf);
        _out.WriteLine($"w={w}: E[L]={lag.MeanLeadTime:0.000000} CV(L)={Math.Sqrt(lag.LagVariance) / lag.MeanLeadTime:0.000000}");
        Assert.InRange(lag.MeanLeadTime, meanLead - 5e-5, meanLead + 5e-5);
        Assert.InRange(Math.Sqrt(lag.LagVariance) / lag.MeanLeadTime, cv - 5e-5, cv + 5e-5);
    }

    [Fact]
    public void Dose_ZeroIsTheBaseAndOneIsTheDeterministicLeadTimeTwo()
    {
        Assert.Equal(BaseLeadTime.LagPmf(), BaseLeadTime.LagPmf(0));
        Assert.Equal(new[] { 0.0, 1.0 }, BaseLeadTime.LagPmf(1));
        Assert.Equal(2.0, new KaplanLeadTime(BaseLeadTime.LagPmf(1)).MeanLeadTime);
    }

    [Fact]
    public void LagPmf_RejectsDosesOutsideZeroToOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BaseLeadTime.LagPmf(-0.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => BaseLeadTime.LagPmf(1.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => BaseLeadTime.LagPmf(double.NaN));
    }

    [Fact]
    public void BaseChain_CoversAndPipelineFollowTheMapping()
    {
        // Four stages, R = 4, p = 8, clipped orders, z = 1. Cover rule B (CycleAverage): retailer E[L] + R − 1 = 5.43; an
        // upstream stage with equal phases (a = 4) has R·(⌊(L − 1)/R⌋ + 1) = 4 for L = 2, 3, 4 and 8 for L = 5, so
        // 4 + 4·P(L = 5) = 4.0938. Little's law: mean pipeline μ·(E[L] − 1) = 143 per stage; 50 replications, tolerance 2 %.
        const int Reps = 50;
        var lag = new KaplanLeadTime(BaseLeadTime.LagPmf());
        var cfg = new ChainConfig
        {
            LeadTimes = Enumerable.Repeat(lag.MeanLeadTime, 4).ToArray(),
            StochasticLeadTimes = Enumerable.Repeat(lag, 4).ToArray(),
            R = 4,
            Window = 8,
            Z = new double[] { 1, 1, 1, 1 },
            AllowNegativeOrders = false,
            InitialMean = GateA.Mu,
            Warmup = GateA.Warmup,
        };
        double upstream = 4 + 4 * BaseLeadTime.LagPmf()[4];
        Assert.InRange(upstream, 4.0938 - 5e-5, 4.0938 + 5e-5);
        var expectedCover = new[] { 5.43, upstream, upstream, upstream };
        var pipeline = new double[4];
        for (int rep = 0; rep < Reps; rep++)
        {
            var res = SerialChain.Run(cfg with { LeadTimeSeed = GateA.LeadTimeSeed(rep) }, GateA.Demand(rep, GateA.Warmup + GateA.Periods, GateA.Mu, 0, GateA.Sigma));
            for (int k = 0; k < 4; k++)
            {
                Assert.InRange(res.CoverPeriods[k], expectedCover[k] - 1e-12, expectedCover[k] + 1e-12);
                pipeline[k] += res.MeanPipeline[k] / Reps;
            }
        }

        _out.WriteLine($"mean pipeline per stage: {string.Join(" / ", pipeline.Select(v => v.ToString("0.00")))} (expected 143)");
        Assert.All(pipeline, v => Assert.InRange(v, 143 * 0.98, 143 * 1.02));
    }
}
