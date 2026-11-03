using Bullwhip.Core.Rng;

namespace Bullwhip.Core.Chain;

/// <summary>
/// No-crossing stochastic lead time of Kaplan (1970), Management Science 16(7), p. 495 (plan S10).
/// </summary>
/// <remarks>
/// Kaplan's matrix (p. 495): with V outstanding orders (ages 0..V−1, the current order included) before the delivery
/// and U after it, P(U = j | V = i) = p_j for j &lt; i, 1 − (p_0 + … + p_{i−1}) for j = i, 0 for j &gt; i, with
/// p_0..p_m ≥ 0 summing to one. This is exactly U = min(C, V) for one draw C ~ (p_0..p_m) per period: every outstanding
/// order of age ≥ C arrives, so orders never overtake. An order is still out after age a iff C &gt; a in each of the
/// periods at ages 0..a, so P(T &gt; a) = Π_{s≤a} (1 − F(s)) with F(s) = p_0 + … + p_s: the age-a hazard is F(a), not
/// p_a. The lag formula printed on p. 495, P(T &gt; i) = Π (1 − p_j), reads p_j as the hazard and does not follow from
/// the matrix (p = (0.2, 0.3, 0.5): matrix 0.2/0.4/0.4, E[T] = 1.2; printed 0.2/0.24/0.56, E[T] = 1.36); the printed
/// inversion p_i = [1 − P(T &gt; i)]/P(T &gt; i−1) does not invert either formula. The inversion used here follows from the
/// matrix: F(a) = h_a = P(T = a)/P(T ≥ a), p_0 = h_0, p_a = h_a − h_{a−1}. It needs a nondecreasing hazard (p_a ≥ 0);
/// other lag distributions cannot be represented in Kaplan's model.
///
/// Timing: Kaplan orders at the start of a period before that period's deliveries (T = 0 possible); the model orders
/// at the end of t and receives at the start of t+L. So Kaplan's T = L − 1 and E[L] = E[T] + 1 (plan §2, L ≥ 1).
/// </remarks>
public sealed class KaplanLeadTime
{
    private readonly double[] _lag;
    private readonly double[] _cut;
    private readonly double[] _cutCdf;

    /// <param name="lagPmf">Target distribution P(T = i), i = 0..m, of Kaplan's lag T = L − 1; P(T = m) &gt; 0.</param>
    public KaplanLeadTime(IReadOnlyList<double> lagPmf)
    {
        int m = lagPmf.Count - 1;
        if (m < 0 || lagPmf.Any(x => x < 0 || double.IsNaN(x)) || Math.Abs(lagPmf.Sum() - 1.0) > 1e-12 || lagPmf[m] <= 0)
        {
            throw new ArgumentException("The lag distribution must be non-negative, sum to one and end with P(T = m) > 0.");
        }

        _lag = lagPmf.ToArray();
        _cut = new double[m + 1];
        _cutCdf = new double[m + 1];
        double previous = 0.0;
        for (int a = 0; a <= m; a++)
        {
            double tail = 0.0;
            for (int i = a; i <= m; i++)
            {
                tail += _lag[i];
            }

            double hazard = a == m ? 1.0 : _lag[a] / tail;
            if (hazard < previous - 1e-12)
            {
                throw new ArgumentException($"Hazard falls at lag {a} ({previous:G6} → {hazard:G6}): not representable in Kaplan's model.");
            }

            hazard = Math.Max(hazard, previous);
            _cut[a] = hazard - previous;
            _cutCdf[a] = hazard;
            previous = hazard;
        }
    }

    /// <summary>Maximum lag m (Kaplan's T); the model lead time is at most m + 1.</summary>
    public int MaxLag => _lag.Length - 1;

    /// <summary>Target P(T = i), i = 0..m.</summary>
    public IReadOnlyList<double> LagPmf => _lag;

    /// <summary>Kaplan's p_0..p_m (sum one), the per-period distribution of the cut C.</summary>
    public IReadOnlyList<double> CutPmf => _cut;

    public double MeanLag => _lag.Select((x, i) => i * x).Sum();

    public double LagVariance
    {
        get
        {
            double mean = MeanLag;
            return _lag.Select((x, i) => (i - mean) * (i - mean) * x).Sum();
        }
    }

    /// <summary>E[L] = E[T] + 1, the planning lead time (plan S10: planning on the known mean).</summary>
    public double MeanLeadTime => MeanLag + 1.0;

    /// <summary>Kaplan's matrix for these p_j: element [i − 1, j] = P(U = j | V = i), i = 1..m+1, j = 0..m.</summary>
    public double[,] TransitionMatrix() => Matrix(_cut);

    /// <summary>Kaplan's matrix (p. 495) for arbitrary p_0..p_m: p_j for j &lt; i, 1 − (p_0 + … + p_{i−1}) for j = i.</summary>
    public static double[,] Matrix(IReadOnlyList<double> p)
    {
        int m = p.Count - 1;
        var matrix = new double[m + 1, m + 1];
        for (int i = 1; i <= m + 1; i++)
        {
            double below = 0.0;
            for (int j = 0; j < i && j <= m; j++)
            {
                matrix[i - 1, j] = p[j];
                below += p[j];
            }

            if (i <= m)
            {
                matrix[i - 1, i] = 1.0 - below;
            }
        }

        return matrix;
    }

    /// <summary>
    /// Lag distribution of one order implied by a transition matrix, computed from the matrix alone (no inversion
    /// formula): U is a Markov chain with U_t ~ row(U_{t−1} + 1). The tagged order is placed when V = U_{t−1} + 1 with
    /// U_{t−1} stationary; at age a it is in position a and arrives iff U ≤ a, otherwise V' = U + 1 next period.
    /// </summary>
    public static double[] LagPmfFromMatrix(double[,] matrix, out double leftover)
    {
        int m = matrix.GetLength(1) - 1;
        var pi = Enumerable.Repeat(1.0 / (m + 1), m + 1).ToArray();
        for (int iter = 0; iter < 100_000; iter++)
        {
            var next = new double[m + 1];
            for (int u = 0; u <= m; u++)
            {
                for (int j = 0; j <= m; j++)
                {
                    next[j] += pi[u] * matrix[u, j];
                }
            }

            double change = next.Select((x, i) => Math.Abs(x - pi[i])).Sum();
            pi = next;
            if (change < 1e-16)
            {
                break;
            }
        }

        // alive[v − 1]: probability that the tagged order is still out and v orders are outstanding before delivery.
        var alive = pi.ToArray();
        var lag = new double[m + 1];
        for (int a = 0; a <= m; a++)
        {
            var next = new double[m + 1];
            for (int v = a + 1; v <= m + 1; v++)
            {
                for (int j = 0; j <= m; j++)
                {
                    double mass = alive[v - 1] * matrix[v - 1, j];
                    if (j <= a)
                    {
                        lag[a] += mass;
                    }
                    else
                    {
                        next[j] += mass; // V' = j + 1 → index j
                    }
                }
            }

            alive = next;
        }

        leftover = alive.Sum();
        return lag;
    }

    /// <summary>Cut C for a uniform u in (0, 1): the smallest j with u ≤ F(j).</summary>
    public int Cut(double u)
    {
        for (int j = 0; j < _cutCdf.Length - 1; j++)
        {
            if (u <= _cutCdf[j])
            {
                return j;
            }
        }

        return _cutCdf.Length - 1;
    }

    /// <summary>
    /// Arrival periods of the dispatches s = −m−1 .. last (element s + m + 1), from one uniform per period for the
    /// periods −m .. last + m (plan §3: one draw per stage and period). The dispatch of period s has age t − 1 − s at the
    /// receipt of period t and arrives at the first t ≥ s + 1 with C_t ≤ t − 1 − s, at the latest at s + 1 + m.
    /// Later dispatches never arrive earlier (no crossing; simultaneous arrival is allowed).
    /// </summary>
    public int[] DrawArrivals(SplitMix64 rng, int last)
    {
        int m = MaxLag;
        var cut = new int[last + 2 * m + 1];
        for (int i = 0; i < cut.Length; i++)
        {
            cut[i] = Cut(rng.NextUniform());
        }

        var due = new int[last + m + 2];
        for (int s = -m - 1; s <= last; s++)
        {
            int t = s + 1;
            while (t < s + 1 + m && cut[t + m] > t - 1 - s)
            {
                t++;
            }

            due[s + m + 1] = t;
        }

        return due;
    }
}
