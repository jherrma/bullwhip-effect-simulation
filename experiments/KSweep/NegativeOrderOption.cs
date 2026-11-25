namespace KSweep;

/// <summary>
/// Command-line option <c>--negative-orders allow</c> of the study modes built on the base sweep: the chain no longer clips
/// negative orders at zero, so a stage may return stock upstream at no cost, as in Chen et al. (2000). Without the option the
/// base case clips negative orders at zero (<see cref="SweepSettings.AllowNegativeOrders"/> = false) and the output is unchanged.
/// </summary>
public sealed record NegativeOrderOption
{
    /// <summary>The only accepted value of the flag.</summary>
    public const string AllowValue = "allow";

    /// <summary>Modes whose settings come from <c>Program.BaseSweep</c> and therefore reach <see cref="ChainCostEvaluator"/>.</summary>
    public static readonly IReadOnlySet<string> SupportedModes = new HashSet<string>
    {
        "base-kpi", "k-base", "k-base-neg", "gate-b-base", "gate-b-base-neg", "ratio-scan", "r-grid-window",
        "countermeasure", "countermeasure-smoke", "countermeasure-structural",
    };

    /// <summary>True if negative orders are allowed; false (clip at zero) is the base case.</summary>
    public bool Allow { get; init; }

    /// <summary>True if the option leaves the base case (clipping) unchanged.</summary>
    public bool IsDefault => !Allow;

    /// <summary>
    /// Reads <c>--negative-orders allow</c> from any position of <paramref name="args"/> and returns the option together with the
    /// remaining arguments in their original order. Any value other than <c>allow</c> is an error.
    /// </summary>
    public static (NegativeOrderOption Option, string[] Remaining) Extract(string[] args)
    {
        const string flag = "--negative-orders";
        var rest = args.ToList();
        int at = rest.IndexOf(flag);
        if (at < 0)
        {
            return (new NegativeOrderOption(), rest.ToArray());
        }

        if (at + 1 >= rest.Count)
        {
            throw new ArgumentException($"{flag} needs a value: {AllowValue}.");
        }

        string value = rest[at + 1];
        rest.RemoveRange(at, 2);
        if (rest.Contains(flag))
        {
            throw new ArgumentException($"{flag} is given more than once.");
        }

        if (value != AllowValue)
        {
            throw new ArgumentException($"{flag} {value}: the only accepted value is '{AllowValue}' (clipping at zero is the default).");
        }

        return (new NegativeOrderOption { Allow = true }, rest.ToArray());
    }

    /// <summary>Throws unless <paramref name="mode"/> applies the option; a non-default option on another mode would be ignored silently.</summary>
    public void EnsureSupported(string mode)
    {
        if (!IsDefault && !SupportedModes.Contains(mode))
        {
            throw new ArgumentException($"--negative-orders applies only to the modes {string.Join(", ", SupportedModes)}; mode '{mode}' does not use it.");
        }
    }

    /// <summary>The settings with negative orders allowed or clipped.</summary>
    public SweepSettings ApplyTo(SweepSettings settings) => settings with { AllowNegativeOrders = Allow };

    /// <summary>One report line naming the order rule.</summary>
    public string Describe() => Allow
        ? "Orders: negative orders allowed (returns upstream at no cost, Chen et al. 2000); base case clips at zero"
        : "Orders: negative orders clipped at zero (base case)";
}
