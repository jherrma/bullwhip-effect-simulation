using Bullwhip.Core.Demand;
using Xunit;

namespace Bullwhip.Tests;

// DECISIONS C50, C52: rho must lie in (-1, 1); innovation sigma = sigma * sqrt(1 - rho^2).
public class Ar1RangeTests
{
    [Fact]
    public void RhoZero_ReturnsSigmaExactly()
    {
        Assert.Equal(10.0, Ar1Demand.InnovationSigma(10.0, 0.0));
    }

    [Fact]
    public void RhoPointSix_ReturnsEightTenthsOfSigma()
    {
        Assert.Equal(8.0, Ar1Demand.InnovationSigma(10.0, 0.6), 1e-12);
    }

    [Fact]
    public void RhoNegative_ReturnsSigmaTimesSqrt091()
    {
        Assert.Equal(10.0 * Math.Sqrt(0.91), Ar1Demand.InnovationSigma(10.0, -0.3), 1e-12);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    [InlineData(1.2)]
    [InlineData(double.NaN)]
    public void RhoOutsideOpenInterval_Throws(double rho)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Ar1Demand.InnovationSigma(10.0, rho));
    }
}
