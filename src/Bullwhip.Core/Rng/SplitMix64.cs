namespace Bullwhip.Core.Rng;

/// <summary>
/// SplitMix64 generator with a Box–Muller normal sampler. Deterministic for a given seed on every platform.
/// (The plan named MathNet.Numerics; it is not in the local package cache, so the sampler is built in.)
/// </summary>
public sealed class SplitMix64
{
    private ulong _state;
    private double? _spare;

    public SplitMix64(ulong seed)
    {
        _state = seed;
    }

    /// <summary>Derives an independent substream seed from a base seed and a stream index.</summary>
    public static ulong Derive(ulong baseSeed, ulong stream)
    {
        var mixer = new SplitMix64(baseSeed ^ (stream * 0x9E3779B97F4A7C15UL));
        return mixer.NextUInt64();
    }

    public ulong NextUInt64()
    {
        _state += 0x9E3779B97F4A7C15UL;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform on the open interval (0, 1).</summary>
    public double NextUniform()
    {
        return ((NextUInt64() >> 11) + 1.0) / 9007199254740993.0;
    }

    /// <summary>Standard normal draw (Box–Muller, both values used).</summary>
    public double NextStandardNormal()
    {
        if (_spare is double spare)
        {
            _spare = null;
            return spare;
        }

        double u1 = NextUniform();
        double u2 = NextUniform();
        double radius = Math.Sqrt(-2.0 * Math.Log(u1));
        double angle = 2.0 * Math.PI * u2;
        _spare = radius * Math.Sin(angle);
        return radius * Math.Cos(angle);
    }
}
