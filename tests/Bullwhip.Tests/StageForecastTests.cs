using Bullwhip.Core.Forecast;
using Xunit;

namespace Bullwhip.Tests;

public class StageForecastTests
{
    [Fact]
    public void EmptyStart_R4_Phase0_MeanIsExactAfterEachClose()
    {
        var f = new StageForecast(8, 4);
        Assert.Equal(0.0, f.Mean);

        // Phase 0: the first close is at t = 0 after one real period, then every R = 4 periods (t = 4, 8, ...).
        for (int t = 0; t < 20; t++)
        {
            f.Observe(100.0);
            if (t % 4 == 0)
            {
                f.CloseBucket();
                Assert.Equal(100.0, f.Mean);
            }
        }
    }

    [Fact]
    public void EmptyStart_R4_Phase2_FirstBucketHasThreePeriods()
    {
        var f = new StageForecast(8, 4);
        for (int t = 0; t <= 2; t++)
        {
            f.Observe(100.0);
        }

        f.CloseBucket();
        Assert.Equal(100.0, f.Mean);
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(8, 4, 0)]
    [InlineData(8, 4, 3)]
    [InlineData(8, 2, 1)]
    public void Prefilled_MeanIsMuExactly(int window, int r, int phase)
    {
        var f = new StageForecast(window, r);
        f.Prefill(100.0, phase);
        Assert.Equal(100.0, f.Mean);

        // First close at t = phase after phase + 1 real periods at mu: still mu.
        for (int t = 0; t <= phase; t++)
        {
            f.Observe(100.0);
        }

        f.CloseBucket();
        Assert.Equal(100.0, f.Mean);
    }
}
