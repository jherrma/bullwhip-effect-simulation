namespace Bullwhip.Core.Analytic;

/// <summary>Closed forms from Chen, Drezner, Ryan & Simchi-Levi (2000), Management Science 46(3), 436–443.</summary>
public static class ChenFormulas
{
    /// <summary>
    /// Eq. (5), Thm 2.2, p. 438: Var(q)/Var(D) ≥ 1 + (2L/p + 2L²/p²)(1 − ρ^p); tight when z = 0.
    /// Here L is the model lead time Lp (equal to Chen's L at R = 1).
    /// </summary>
    public static double SingleStageBullwhipRatio(int leadTime, int window, double rho)
    {
        double a = 2.0 * leadTime / window;
        double b = 2.0 * leadTime * leadTime / ((double)window * window);
        return 1.0 + (a + b) * (1.0 - Math.Pow(rho, window));
    }
}
