namespace Bullwhip.Core.Forecast;

/// <summary>
/// Rolling RMS of realised protection-period forecast errors: the estimated σ̂ of plan §2.
/// RMS about zero, not the std about the sample mean, because S adds nothing to Lp·μ̂ for a bias, so a persistent
/// forecast bias is protection-period risk and must stay in σ̂.
/// </summary>
public sealed class ErrorWindow
{
    private readonly double[] _errors;
    private int _next;
    private int _count;

    public ErrorWindow(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _errors = new double[capacity];
    }

    public void Add(double error)
    {
        _errors[_next] = error;
        _next = (_next + 1) % _errors.Length;
        if (_count < _errors.Length)
        {
            _count++;
        }
    }

    public double Sigma
    {
        get
        {
            if (_count == 0)
            {
                return 0.0;
            }

            double ss = 0.0;
            for (int i = 0; i < _count; i++)
            {
                ss += _errors[i] * _errors[i];
            }

            return Math.Sqrt(ss / _count);
        }
    }
}
