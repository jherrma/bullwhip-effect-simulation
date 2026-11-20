using System.Globalization;
using Bullwhip.Core.Demand;

namespace KSweep;

/// <summary>
/// Command-line demand options of the study modes built on the base sweep: <c>--pattern &lt;name&gt;</c> selects one of the
/// demand patterns of DECISIONS C51/C52 (stable, seasonal, trend, jump, promotion) with their standard parameters
/// (<see cref="DemandPatternParameters"/>), and <c>--rho &lt;value&gt;</c> sets the AR(1) autocorrelation of DECISIONS C50
/// (stationary standard deviation held at sigma). Without either option the demand is the stable base case.
/// </summary>
public sealed record DemandOption
{
    /// <summary>Modes whose settings come from the base sweep and whose demand runs through <see cref="ChainCostEvaluator.Demand"/>.</summary>
    public static readonly IReadOnlySet<string> SupportedModes = new HashSet<string>
    {
        "base-kpi", "k-base", "k-base-neg", "gate-b-base", "gate-b-base-neg", "ratio-scan", "r-grid-window",
        "countermeasure", "countermeasure-smoke",
    };

    /// <summary>Demand pattern; Stable is the base case.</summary>
    public DemandPattern Pattern { get; init; } = DemandPattern.Stable;

    /// <summary>AR(1) autocorrelation; 0 is the base case.</summary>
    public double Rho { get; init; } = 0;

    /// <summary>True if the option leaves the base-case demand unchanged.</summary>
    public bool IsDefault => Pattern == DemandPattern.Stable && Rho == 0;

    /// <summary>
    /// Reads <c>--pattern &lt;name&gt;</c> and <c>--rho &lt;value&gt;</c> from any position of <paramref name="args"/> and returns
    /// the option together with the remaining arguments in their original order. Pattern and rho cannot be combined (C50, C51).
    /// </summary>
    public static (DemandOption Option, string[] Remaining) Extract(string[] args)
    {
        var rest = args.ToList();
        string? patternText = TakeValue(rest, "--pattern");
        string? rhoText = TakeValue(rest, "--rho");
        var option = new DemandOption
        {
            Pattern = patternText == null ? DemandPattern.Stable : ParsePattern(patternText),
            Rho = rhoText == null ? 0 : double.Parse(rhoText, NumberStyles.Float, CultureInfo.InvariantCulture),
        };

        _ = Ar1Demand.InnovationSigma(1.0, option.Rho); // rejects rho outside (-1, 1)
        if (option.Pattern != DemandPattern.Stable && option.Rho != 0)
        {
            throw new ArgumentException("--pattern and --rho cannot be combined (DECISIONS C50, C51); use only one.");
        }

        return (option, rest.ToArray());
    }

    /// <summary>Throws unless <paramref name="mode"/> applies the option; a non-default option on another mode would be ignored silently.</summary>
    public void EnsureSupported(string mode)
    {
        if (!IsDefault && !SupportedModes.Contains(mode))
        {
            throw new ArgumentException($"--pattern/--rho apply only to the modes {string.Join(", ", SupportedModes)}; mode '{mode}' does not use them.");
        }
    }

    /// <summary>The settings with this pattern and rho; the pattern parameters keep their standard values.</summary>
    public SweepSettings ApplyTo(SweepSettings settings) => settings with { Pattern = Pattern, Rho = Rho };

    /// <summary>One report line naming the demand, for example "Demand: pattern Jump (J = 0.3 from week 1200)".</summary>
    public string Describe()
    {
        var p = new DemandPatternParameters();
        string detail = Pattern switch
        {
            DemandPattern.Seasonal => $"A = {p.Amplitude:R}, period {p.Period}",
            DemandPattern.Trend => $"m = mu up to week {p.TrendStart}, then linear to {p.TrendEndFactor:R} mu at week {p.TrendEndTime}",
            DemandPattern.Jump => $"J = {p.JumpSize:R} from week {p.JumpTime}",
            DemandPattern.Promotion => $"P = {p.PromoSize:R} for {p.PromoDuration} weeks, then Q = {p.DropSize:R} for {p.DropDuration} weeks, every {p.PromoPeriod} weeks",
            _ => $"AR(1), rho = {Rho:R}, stationary sigma held",
        };
        string name = Pattern == DemandPattern.Stable ? "stable" : $"pattern {Pattern}";
        return $"Demand: {name} ({detail}; DECISIONS C50-C52)";
    }

    private static string? TakeValue(List<string> args, string flag)
    {
        int at = args.IndexOf(flag);
        if (at < 0)
        {
            return null;
        }

        if (at + 1 >= args.Count)
        {
            throw new ArgumentException($"{flag} needs a value.");
        }

        string value = args[at + 1];
        args.RemoveRange(at, 2);
        if (args.Contains(flag))
        {
            throw new ArgumentException($"{flag} is given more than once.");
        }

        return value;
    }

    private static DemandPattern ParsePattern(string text)
    {
        bool known = Enum.TryParse(text, ignoreCase: true, out DemandPattern pattern)
            && Enum.IsDefined(pattern)
            && !int.TryParse(text, out _);
        return known
            ? pattern
            : throw new ArgumentException($"--pattern {text}: expected one of {string.Join(", ", Enum.GetNames<DemandPattern>().Select(n => n.ToLowerInvariant()))}.");
    }
}
