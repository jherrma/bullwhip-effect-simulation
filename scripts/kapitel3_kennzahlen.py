"""Numbers of the base case that thesis chapter 3 (3.3, Validierung) cites.

Reads two files of a run folder:
- base-kpi.txt: BR4 (4-week buckets) and the cost components,
- countermeasure-base.txt: lead times, pipeline capital, ready and immediate fill rate (the same base case,
  base-kpi.txt does not print these).

Usage:
    python3 scripts/kapitel3_kennzahlen.py <run-folder>
"""

import argparse
import re
from pathlib import Path

from outcome_parser import FILL_RE, NUMBER, PLUS_MINUS, READY_RE, TOTAL_RE, Interval, german, interval_from_match, interval_text

BR_4WEEK_RE = re.compile(r"^\s*stage (\d): BR\(4-week\) =\s+" + NUMBER + PLUS_MINUS + NUMBER)
COST_SUM_RE = re.compile(
    r"^\s*sum:\s+holding\s+" + NUMBER + PLUS_MINUS + NUMBER
    + r"\s+\|\s+capital\s+" + NUMBER + PLUS_MINUS + NUMBER
    + r"\s+\|\s+shortage\s+" + NUMBER + PLUS_MINUS + NUMBER
    + r"\s+\|\s+order\s+" + NUMBER + PLUS_MINUS + NUMBER
)
PIPELINE_RE = re.compile(r"^\s*pipeline capital per stage: \[.*\] \| sum\s+" + NUMBER + PLUS_MINUS + NUMBER)
LEAD_RE = re.compile(r"^\s*stage (\d): E\[L\] = " + NUMBER + r", CV\(L\) = " + NUMBER)


def read_base(base_kpi: Path, countermeasure_base: Path) -> dict:
    """Costs and BR4 from base-kpi.txt; lead times, pipeline and service levels from countermeasure-base.txt."""
    found = _read_first_values(base_kpi)
    extra = _read_first_values(countermeasure_base)
    for key in ("lead", "pipeline", "ready", "fill", "z"):
        found[key] = extra[key]
    return found


def _read_first_values(path: Path) -> dict:
    """First occurrence of each quantity in a study file."""
    found: dict = {"br4": {}, "lead": {}, "cost_sum": None, "total": None, "pipeline": None,
                   "ready": None, "fill": None, "z": None}
    for line in path.read_text(encoding="utf-8").splitlines():
        if found["z"] is None and line.startswith("z per stage"):
            found["z"] = line.split("[", 1)[1].rstrip("]")
        if (m := BR_4WEEK_RE.match(line)) and int(m.group(1)) not in found["br4"]:
            found["br4"][int(m.group(1))] = interval_from_match(m, 2)
        if (m := LEAD_RE.match(line)) and int(m.group(1)) not in found["lead"]:
            found["lead"][int(m.group(1))] = (m.group(2), m.group(3))
        if found["cost_sum"] is None and (m := COST_SUM_RE.match(line)):
            found["cost_sum"] = {"holding": interval_from_match(m, 1), "capital": interval_from_match(m, 3),
                                 "shortage": interval_from_match(m, 5), "order": interval_from_match(m, 7)}
        if found["pipeline"] is None and (m := PIPELINE_RE.match(line)):
            found["pipeline"] = interval_from_match(m, 1)
        if found["total"] is None and (m := TOTAL_RE.match(line)):
            found["total"] = interval_from_match(m, 1)
        if found["fill"] is None and (m := FILL_RE.match(line)):
            found["fill"] = interval_from_match(m, 1)
        if found["ready"] is None and (m := READY_RE.match(line)):
            found["ready"] = interval_from_match(m, 1)
    return found


def report(values: dict) -> str:
    lines = ["# Kapitel 3: Kennzahlen des Basisfalls (base-kpi, R = 4)", ""]
    lines.append(f"Sicherheitsfaktoren z je Stufe: [{values['z']}]")
    lines.append("")
    lines.append("## Lieferzeit je Stufe")
    for stage, (mean, cv) in sorted(values["lead"].items()):
        lines.append(f"- Stufe {stage}: E[L] = {german(mean)}, CV(L) = {german(cv, 3)}")
    lines.append("")
    lines.append("## Bullwhip-Ratio BR4 (4-Wochen-Summen, Mittel ± 95 %-KI)")
    for stage, interval in sorted(values["br4"].items()):
        lines.append(f"- Stufe {stage}: {interval_text(interval)}")
    lines.append("")
    lines.append("## Kosten je Woche")
    for name, interval in values["cost_sum"].items():
        lines.append(f"- {name}: {interval_text(interval)}")
    lines.append(f"- Pipeline-Kapital (Teil von Kapital): {interval_text(values['pipeline'])}")
    lines.append(f"- Gesamt: {interval_text(values['total'])}")
    lines.append("")
    lines.append("## Servicegrade")
    lines.append(f"- Bereitschaftsgrad Händler: {interval_text(values['ready'], 4)}")
    lines.append(f"- Sofortige Füllrate: {interval_text(values['fill'], 4)}")
    return "\n".join(lines) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("run_folder", type=Path, help="folder with base-kpi.txt and countermeasure-base.txt")
    args = parser.parse_args()
    values = read_base(args.run_folder / "base-kpi.txt", args.run_folder / "countermeasure-base.txt")
    print(report(values), end="")


if __name__ == "__main__":
    main()
