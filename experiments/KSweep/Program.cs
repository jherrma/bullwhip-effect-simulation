using System.Globalization;
using Bullwhip.Core.Costs;
using KSweep;

// K sweep of the order-interval comparison (DECISIONS C28), Gate B (DECISIONS A4, C30) or one of the upstream shortage studies.
// Prints the report and, if a path is given, writes it there.
// Usage: dotnet run -c Release --project experiments/KSweep -- [output-file]
//        dotnet run -c Release --project experiments/KSweep -- gate-b [output-file]
//        dotnet run -c Release --project experiments/KSweep -- upstream [output-file]       (DECISIONS C31)
//        dotnet run -c Release --project experiments/KSweep -- larger-beta [output-file]    (DECISIONS C32)
//        dotnet run -c Release --project experiments/KSweep -- single-stage [output-file]   (DECISIONS C33)
//        dotnet run -c Release --project experiments/KSweep -- k-factor <scale> [output-file]   (DECISIONS C37)
//        dotnet run -c Release --project experiments/KSweep -- base-kpi [output-file]      (DECISIONS C44)
//        dotnet run -c Release --project experiments/KSweep -- ratio-scan <ratio> [output-file]   (k-base with shortage/holding ratio b/h₁ = <ratio> instead of 26)
//        dotnet run -c Release --project experiments/KSweep -- r-grid-window <p> [output-file]   (DECISIONS C87: k-base at forecast window p, sigma-hat window 8, R = every divisor of p from 2 to p/2)
//        dotnet run -c Release --project experiments/KSweep -- countermeasure <id> [output-file]
//        dotnet run -c Release --project experiments/KSweep -- countermeasure-smoke <id> [output-file]   (plumbing check only, 2 + 3 replications)
//        dotnet run -c Release --project experiments/KSweep -- countermeasure-structural <id> [output-file]   (DECISIONS C59, C67, C68: sigma = 0, z = 0; ids lt, lt-dose, ri)
//        gate-b-bme, upstream-bme, larger-beta-bme: the same studies at the BME base case (factor 6331/2400, DECISIONS C39).
// Option (any position): --split <s> <i> sets the storage rate s and the capital rate i of every mode built on BaseSweep
//        (base-kpi, k-base, countermeasure, ...). s + i must equal the default sum (0.10) exactly, so the calibration
//        (b = 26·h₁, K from (s + i)) is unchanged and only the split between on-hand storage and capital varies.
//        Without the option the defaults of CostParameters apply. The chosen split is written to stderr only.
// Options (any position): --pattern <stable|seasonal|trend|jump|promotion> sets the demand pattern of DECISIONS C51/C52 with
//        its standard parameters; --rho <value> sets the AR(1) autocorrelation of DECISIONS C50 (stationary sigma held).
//        Only one of the two may be given. They apply to the modes listed in DemandOption.SupportedModes (base-kpi, k-base,
//        k-base-neg, gate-b-base, gate-b-base-neg, ratio-scan, r-grid-window, countermeasure, countermeasure-smoke); any other
//        mode refuses them. With either option the report starts with one line naming the demand; without them the demand
//        is the stable base case and the output is unchanged.

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var lines = new List<string>();

void Say(string line)
{
    Console.WriteLine(line);
    lines.Add(line);
}

var defaultCosts = new CostParameters();
var baseCosts = defaultCosts;
int splitAt = Array.IndexOf(args, "--split");
if (splitAt >= 0)
{
    if (splitAt + 2 >= args.Length)
    {
        throw new ArgumentException("--split needs two values: <storage rate s> <capital rate i>.");
    }

    double s = double.Parse(args[splitAt + 1], CultureInfo.InvariantCulture);
    double i = double.Parse(args[splitAt + 2], CultureInfo.InvariantCulture);
    if (s < 0 || i < 0 || s + i != defaultCosts.StorageRate + defaultCosts.CapitalRate)
    {
        throw new ArgumentException($"--split {s} {i}: both rates must be non-negative and s + i must equal the default sum {defaultCosts.StorageRate + defaultCosts.CapitalRate:R} exactly (got {s + i:R}).");
    }

    baseCosts = defaultCosts with { StorageRate = s, CapitalRate = i };
    args = args.Take(splitAt).Concat(args.Skip(splitAt + 3)).ToArray();
    Console.Error.WriteLine($"Holding-cost split: storage rate s = {s:R}, capital rate i = {i:R}, sum {s + i:R}");
}

(DemandOption demandOption, args) = DemandOption.Extract(args);

string raw = args.Length > 0 ? args[0] : "";
string baseName = raw.EndsWith("-bme", StringComparison.Ordinal) ? raw[..^4] : raw;
bool bme = baseName != raw && baseName is "gate-b" or "upstream" or "larger-beta";
string mode = bme ? baseName : raw is "gate-b" or "upstream" or "larger-beta" or "single-stage" or "base-kpi" or "countermeasure" or "countermeasure-smoke" or "countermeasure-structural" or "k-factor" or "k-bme-neg" or "gate-b-base" or "gate-b-base-neg" or "k-base" or "k-base-neg" or "ratio-scan" or "r-grid-window" ? raw : "k-sweep";
string? output = args.Skip(mode == "k-sweep" ? 0 : mode is "k-factor" or "countermeasure" or "countermeasure-smoke" or "countermeasure-structural" or "ratio-scan" or "r-grid-window" ? 2 : 1).FirstOrDefault();
demandOption.EnsureSupported(mode);
if (!demandOption.IsDefault)
{
    Console.Error.WriteLine(demandOption.Describe());
    Say(demandOption.Describe());
}

switch (mode)
{
    case "upstream":
        new UpstreamStudyRunner(UpstreamOnBase(new UpstreamStudySettings(), bme)).Run(Say);
        break;
    case "larger-beta":
        new UpstreamStudyRunner(UpstreamOnBase(UpstreamStudySettings.LargerBeta(), bme)).Run(Say);
        break;
    case "single-stage":
        new SingleStageStudyRunner(new SingleStageStudySettings()).Run(Say);
        break;
    case "base-kpi":
        new BaseKpiReportRunner(BaseSweep(neg: false)).Run(Say);
        break;
    case "countermeasure":
        RunCountermeasure(args[1], BaseSweep(neg: false));
        break;
    case "countermeasure-smoke":
        RunCountermeasure(args[1], SmokeSweep());
        break;
    case "countermeasure-structural":
        new StructuralCompanionRunner(BaseSweep(neg: false)).Run(args[1], Say);
        break;
    case "k-factor":
        KFactorStudy.Run(double.Parse(args[1], CultureInfo.InvariantCulture), Say);
        break;
    case "k-bme-neg":
        new KSweepRunner(BmeNegativeSweep()).Run(Say);
        break;
    case "gate-b-base":
        new GateBRunner(BaseGateB(neg: false)).Run(Say);
        break;
    case "gate-b-base-neg":
        new GateBRunner(BaseGateB(neg: true)).Run(Say);
        break;
    case "k-base":
        new KSweepRunner(BaseSweep(neg: false)).Run(Say);
        break;
    case "k-base-neg":
        new KSweepRunner(BaseSweep(neg: true)).Run(Say);
        break;
    case "ratio-scan":
        new KSweepRunner(RatioSweep(double.Parse(args[1], CultureInfo.InvariantCulture))).Run(Say);
        break;
    case "r-grid-window":
        RunWindowGrid(int.Parse(args[1], CultureInfo.InvariantCulture));
        break;
    case "gate-b":
        new GateBRunner(GateBOnBase(new GateBSettings(), bme)).Run(Say);
        break;
    default:
        new KSweepRunner(new SweepSettings()).Run(Say);
        break;
}

if (output != null)
{
    File.WriteAllLines(output, lines);
}

// Countermeasure 03 (id "ri") has its own runner; every other id goes through the variant registry.
void RunCountermeasure(string id, SweepSettings sweep)
{
    if (id == "ri")
    {
        new OrderIntervalRunner(sweep, new OrderCostCutSettings()).Run(Say);
        return;
    }

    new CountermeasureRunner(sweep, Countermeasures.For(id)).Run(Say);
}

// C87: the k-base sweep at forecast window p, so that order intervals that do not divide p = 8 (for example R = 6) can run.
void RunWindowGrid(int p)
{
    var sweep = WindowGridSweep(p);
    Say($"Order-interval grid at forecast window p = {p} (DECISIONS C87); sigma-hat window {sweep.Variant.SigmaWindow} order times; R = {string.Join(", ", sweep.Intervals)}");
    new KSweepRunner(sweep).Run(Say);
}

// The -bme modes use the BME order cost with β = 0 as recorded in C39 and C40; the β = 1 base case is not rerun here.
static GateBSettings GateBOnBase(GateBSettings s, bool bme) =>
    bme ? s with { Sweep = s.Sweep with { Costs = new CostParameters { UpstreamShortageMultiplier = 0 } } } : s;

// C40: the K sweep at the BME base only (β = 0), with safety factors −4 to 8 (negative z allowed), on the C28 replications.
static SweepSettings BmeNegativeSweep() => new()
{
    Costs = new CostParameters { UpstreamShortageMultiplier = 0 },
    Scales = new[] { 6331.0 / 2400.0 },
    Grid = Enumerable.Range(0, 49).Select(i => -4.0 + 0.25 * i).ToArray(),
};

// C42: the base case (BME order cost, β = 1, DECISIONS C38, C41). neg = true uses z from −4 to 8, otherwise 0 to 8.
// The demand pattern and rho come from --pattern / --rho (stable, rho = 0 without them).
SweepSettings BaseSweep(bool neg) => demandOption.ApplyTo(new()
{
    Costs = baseCosts,
    Scales = new[] { 6331.0 / 2400.0 },
    Grid = neg
        ? Enumerable.Range(0, 49).Select(i => -4.0 + 0.25 * i).ToArray()
        : Enumerable.Range(0, 33).Select(i => 0.25 * i).ToArray(),
});

// The k-base sweep with the shortage/holding ratio b/h₁ set to the given value; everything else is BaseSweep(neg: false).
SweepSettings RatioSweep(double ratio)
{
    var sweep = BaseSweep(neg: false);
    return sweep with { Costs = sweep.Costs with { ShortageToHoldingRatio = ratio } };
}

// C87: BaseSweep(neg: false) with forecast window p and the sigma-hat window held at 8 order times (its base value at every R,
// as in measure 04b); R runs through every divisor of p from 2 to p/2.
SweepSettings WindowGridSweep(int p)
{
    if (p < 4)
    {
        throw new ArgumentOutOfRangeException(nameof(p), p, "The forecast window p must be at least 4 so that the grid holds an R from 2 to p/2.");
    }

    return BaseSweep(neg: false) with
    {
        Window = p,
        Intervals = Enumerable.Range(2, p / 2 - 1).Where(r => p % r == 0).ToArray(),
        Variant = new Variant { Name = $"window p={p}", SigmaWindow = 8 },
    };
}

// Plumbing check of the countermeasure mode: replications 9000+, z = 1; the numbers are not valid results.
SweepSettings SmokeSweep() => BaseSweep(neg: false) with
{
    TrainFirst = 9000,
    TrainReps = 2,
    TestFirst = 9100,
    TestReps = 3,
    Grid = new[] { 1.0 },
    Passes = 1,
};

GateBSettings BaseGateB(bool neg) => new()
{
    Sweep = BaseSweep(neg) with { TrainFirst = 200, TestFirst = 2000 },
};

static UpstreamStudySettings UpstreamOnBase(UpstreamStudySettings s, bool bme) =>
    bme ? s with { Sweep = s.Sweep with { Costs = new CostParameters { UpstreamShortageMultiplier = 0 } } } : s;
