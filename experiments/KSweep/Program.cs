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
string mode = bme ? baseName : raw is "gate-b" or "upstream" or "larger-beta" or "single-stage" or "k-factor" ? raw : "k-sweep";
string? output = args.Skip(mode == "k-sweep" ? 0 : mode == "k-factor" ? 2 : 1).FirstOrDefault();

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
    case "k-factor":
        KFactorStudy.Run(double.Parse(args[1], CultureInfo.InvariantCulture), Say);
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

// With "-bme" the study runs on the default cost parameters, i.e. the BME base case (DECISIONS C38, C39).
static GateBSettings GateBOnBase(GateBSettings s, bool bme) =>
    bme ? s with { Sweep = s.Sweep with { Costs = new CostParameters() } } : s;

static UpstreamStudySettings UpstreamOnBase(UpstreamStudySettings s, bool bme) =>
    bme ? s with { Sweep = s.Sweep with { Costs = new CostParameters() } } : s;
