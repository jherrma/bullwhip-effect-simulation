namespace Bullwhip.Core.Forecast;

/// <summary>Moving average over the last p observations, O(1) per update (ring buffer and running sum).</summary>
public sealed class MovingAverage
{
    private readonly double[] _buffer;
    private int _next;
    private int _count;
    private double _sum;

    public MovingAverage(int window)
    {
        if (window < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(window));
        }

        _buffer = new double[window];
    }

    public int Window => _buffer.Length;

    public bool IsFull => _count == _buffer.Length;

    /// <summary>Mean of the last p observations. Valid only when <see cref="IsFull"/>.</summary>
    public double Mean
    {
        get
        {
            if (!IsFull)
            {
                throw new InvalidOperationException("Moving average needs a full window.");
            }

            return _sum / _buffer.Length;
        }
    }

    public void Observe(double x)
    {
        if (IsFull)
        {
            _sum -= _buffer[_next];
        }

        _buffer[_next] = x;
        _sum += x;
        _next = (_next + 1) % _buffer.Length;
        if (!IsFull)
        {
            _count++;
        }
    }
}
