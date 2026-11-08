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
//        dotnet run -c Release --project experiments/KSweep -- countermeasure <id> [output-file]
//        dotnet run -c Release --project experiments/KSweep -- countermeasure-smoke <id> [output-file]   (plumbing check only, 2 + 3 replications)
//        gate-b-bme, upstream-bme, larger-beta-bme: the same studies at the BME base case (factor 6331/2400, DECISIONS C39).

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var lines = new List<string>();

void Say(string line)
{
    Console.WriteLine(line);
    lines.Add(line);
}

string raw = args.Length > 0 ? args[0] : "";
string baseName = raw.EndsWith("-bme", StringComparison.Ordinal) ? raw[..^4] : raw;
bool bme = baseName != raw && baseName is "gate-b" or "upstream" or "larger-beta";
string mode = bme ? baseName : raw is "gate-b" or "upstream" or "larger-beta" or "single-stage" or "base-kpi" or "countermeasure" or "countermeasure-smoke" or "k-factor" or "k-bme-neg" or "gate-b-base" or "gate-b-base-neg" or "k-base" or "k-base-neg" ? raw : "k-sweep";
string? output = args.Skip(mode == "k-sweep" ? 0 : mode is "k-factor" or "countermeasure" or "countermeasure-smoke" ? 2 : 1).FirstOrDefault();

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
static SweepSettings BaseSweep(bool neg) => new()
{
    Costs = new CostParameters(),
    Scales = new[] { 6331.0 / 2400.0 },
    Grid = neg
        ? Enumerable.Range(0, 49).Select(i => -4.0 + 0.25 * i).ToArray()
        : Enumerable.Range(0, 33).Select(i => 0.25 * i).ToArray(),
};

// Plumbing check of the countermeasure mode: replications 9000+, z = 1; the numbers are not valid results.
static SweepSettings SmokeSweep() => BaseSweep(neg: false) with
{
    TrainFirst = 9000,
    TrainReps = 2,
    TestFirst = 9100,
    TestReps = 3,
    Grid = new[] { 1.0 },
    Passes = 1,
};

static GateBSettings BaseGateB(bool neg) => new()
{
    Sweep = BaseSweep(neg) with { TrainFirst = 200, TestFirst = 2000 },
};

static UpstreamStudySettings UpstreamOnBase(UpstreamStudySettings s, bool bme) =>
    bme ? s with { Sweep = s.Sweep with { Costs = new CostParameters { UpstreamShortageMultiplier = 0 } } } : s;
