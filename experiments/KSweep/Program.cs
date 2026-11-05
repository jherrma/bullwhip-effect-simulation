using System.Globalization;
using KSweep;

// K sweep of the order-interval comparison (DECISIONS C28), Gate B (DECISIONS A4, C30) or one of the upstream shortage studies.
// Prints the report and, if a path is given, writes it there.
// Usage: dotnet run -c Release --project experiments/KSweep -- [output-file]
//        dotnet run -c Release --project experiments/KSweep -- gate-b [output-file]
//        dotnet run -c Release --project experiments/KSweep -- upstream [output-file]       (DECISIONS C31)
//        dotnet run -c Release --project experiments/KSweep -- larger-beta [output-file]    (DECISIONS C32)
//        dotnet run -c Release --project experiments/KSweep -- single-stage [output-file]   (DECISIONS C33)
//        dotnet run -c Release --project experiments/KSweep -- k-factor <scale> [output-file]   (DECISIONS C37)

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
var lines = new List<string>();

void Say(string line)
{
    Console.WriteLine(line);
    lines.Add(line);
}

string mode = args.Length > 0 && args[0] is "gate-b" or "upstream" or "larger-beta" or "single-stage" or "k-factor" ? args[0] : "k-sweep";
string? output = args.Skip(mode == "k-sweep" ? 0 : mode == "k-factor" ? 2 : 1).FirstOrDefault();

switch (mode)
{
    case "upstream":
        new UpstreamStudyRunner(new UpstreamStudySettings()).Run(Say);
        break;
    case "larger-beta":
        new UpstreamStudyRunner(UpstreamStudySettings.LargerBeta()).Run(Say);
        break;
    case "single-stage":
        new SingleStageStudyRunner(new SingleStageStudySettings()).Run(Say);
        break;
    case "k-factor":
        KFactorStudy.Run(double.Parse(args[1], CultureInfo.InvariantCulture), Say);
        break;
    case "gate-b":
        new GateBRunner(new GateBSettings()).Run(Say);
        break;
    default:
        new KSweepRunner(new SweepSettings()).Run(Say);
        break;
}

if (output != null)
{
    File.WriteAllLines(output, lines);
}
