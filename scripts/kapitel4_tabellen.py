"""Tables of thesis chapter 4 (Gegenmaßnahmen unter allen Nachfragemustern) from a run folder.

For each demand setting and each countermeasure file it prints one row per result block:
own cost at the block's own z, paired difference to the base case (95 %-KI), the implied weekly saving
(= minus the paired difference, the break-even ceiling for implementation cost, chapter 4 note O3),
BR4 of stage 4 and the ready rate.

Usage:
    python3 scripts/kapitel4_tabellen.py <run-folder> [--out file.md]
"""

import argparse
from pathlib import Path

from outcome_parser import Interval, interval_text, parse_blocks

DEMANDS = [
    ("stabil", ""),
    ("saisonal", "seasonal_"),
    ("Trend", "trend_"),
    ("Sprung", "jump_"),
    ("Aktion", "promotion_"),
    ("AR(1) rho = -0,3", "rho-m0.3_"),
    ("AR(1) rho = 0,3", "rho-0.3_"),
    ("AR(1) rho = 0,6", "rho-0.6_"),
]
MEASURE_FILES = [
    "countermeasure-base.txt",
    "countermeasure-is.txt",
    "countermeasure-lt.txt",
    "countermeasure-lt-dose.txt",
    "countermeasure-is-lt-parts.txt",
    "countermeasure-maw.txt",
    "countermeasure-ri.txt",
]


def saving_text(label: str, difference: Interval | None) -> str:
    """Weekly saving = minus the paired cost difference. Interaction rows are not savings and show '–'."""
    if difference is None or label.startswith("Interaction"):
        return "–"
    return interval_text(Interval(-difference.value, difference.half_width))


def demand_section(run_folder: Path, title: str, prefix: str) -> list[str]:
    lines = [f"## {title}", ""]
    lines.append("| Datei | Block | Kosten/Woche | Δ gepaart zur Basis | Ersparnis je Woche | BR4 Stufe 4 | Bereitschaft |")
    lines.append("|---|---|---|---|---|---|---|")
    for name in MEASURE_FILES:
        path = run_folder / f"{prefix}{name}"
        if not path.exists():
            lines.append(f"| {prefix}{name} | fehlt | | | | | |")
            continue
        seen_labels: set[str] = set()
        for block in parse_blocks(path):
            if block.label in seen_labels:
                continue  # a study file may repeat a header; print each variant once
            seen_labels.add(block.label)
            br4 = block.br4.get(4)
            lines.append(
                f"| {prefix}{name} | {block.label} | {interval_text(block.total)} | "
                f"{interval_text(block.diff_to_base)} | {saving_text(block.label, block.diff_to_base)} | "
                f"{interval_text(br4, 2)} | {interval_text(block.ready_rate, 4)} |"
            )
    lines.append("")
    return lines


def build_report(run_folder: Path) -> str:
    lines = ["# Kapitel 4: Gegenmaßnahmen je Nachfragemuster", ""]
    lines.append(f"Quelle: `{run_folder}`. Alle Zahlen Mittel ± 95-%-Konfidenzintervall (Holdout-Replikationen 1000–1199).")
    lines.append("Ersparnis je Woche = −Δ; sie ist die Obergrenze für Umsetzungskosten je Woche (Notiz O3).")
    lines.append("")
    for title, prefix in DEMANDS:
        lines.extend(demand_section(run_folder, title, prefix))
    return "\n".join(lines) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("run_folder", type=Path)
    parser.add_argument("--out", type=Path, help="write the report to this file instead of stdout")
    args = parser.parse_args()
    text = build_report(args.run_folder)
    if args.out:
        args.out.write_text(text, encoding="utf-8")
    else:
        print(text, end="")


if __name__ == "__main__":
    main()
