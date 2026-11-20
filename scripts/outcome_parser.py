"""Parse the text outputs of experiments/KSweep (countermeasure, base-kpi and order-interval studies).

Standard library only. Numbers are kept as Decimal, exactly as printed in the study files, so that
rounding for the thesis (half up, to two places) does not depend on binary floating point.
"""

import re
from dataclasses import dataclass, field
from decimal import ROUND_HALF_UP, Decimal
from pathlib import Path

NUMBER = r"(-?\d+(?:\.\d+)?)"
PLUS_MINUS = r"\s*±\s*"

TOTAL_RE = re.compile(r"^\s*TOTAL cost/week:\s+" + NUMBER + PLUS_MINUS + NUMBER)
BR4_RE = re.compile(r"^\s*stage (\d): BR4\s+" + NUMBER + PLUS_MINUS + NUMBER)
BR4_LIST_RE = re.compile(r"^\s*BR4 per stage: \[(.*)\]")
LIST_ITEM_RE = re.compile(NUMBER + PLUS_MINUS + NUMBER)
READY_RE = re.compile(r"^\s*ready rate:\s+" + NUMBER + PLUS_MINUS + NUMBER)
FILL_RE = re.compile(r"^\s*immediate fill rate:\s+" + NUMBER + PLUS_MINUS + NUMBER)
TOTAL_SAVING_RE = re.compile(r"^\s*total saving \(R\* at f vs base\):\s+" + NUMBER + PLUS_MINUS + NUMBER)


@dataclass(frozen=True)
class Interval:
    """A mean and its 95 % half-width, as printed in the study file."""

    value: Decimal
    half_width: Decimal


@dataclass
class Block:
    """One result block of a study: a variant at its own safety factors z.

    diff_to_base is the paired cost difference to the base case (negative = cheaper). For order-interval blocks
    it is the 'total saving' line, which is also a cost difference, not a saving.
    """

    label: str
    total: Interval | None = None
    diff_to_base: Interval | None = None
    ready_rate: Interval | None = None
    br4: dict[int, Interval] = field(default_factory=dict)


def interval_from_match(match: re.Match, first_group: int) -> Interval:
    return Interval(Decimal(match.group(first_group)), Decimal(match.group(first_group + 1)))


def parse_blocks(path: Path) -> list[Block]:
    """Split a study file at '== label' headers and collect the values of each block.

    Lines after 'difference to base' (and the TOTAL after 'Interaction:') are paired differences; they are
    not the block's own total or BR4.
    """
    blocks: list[Block] = []
    current: Block | None = None
    in_difference = False
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("== "):
            current = Block(label=line[3:].strip())
            blocks.append(current)
            in_difference = False
            continue
        if line.startswith("Interaction:"):
            current = Block(label="Interaction (paired, each at own z)")
            blocks.append(current)
            in_difference = True
            continue
        if current is None:
            continue
        if "difference to base" in line:
            in_difference = True
            continue
        _collect(current, line, in_difference)
    return blocks


def _collect(block: Block, line: str, in_difference: bool) -> None:
    saving = TOTAL_SAVING_RE.match(line)
    if saving:
        block.diff_to_base = interval_from_match(saving, 1)
        return
    total = TOTAL_RE.match(line)
    if total:
        if in_difference:
            if block.diff_to_base is None:
                block.diff_to_base = interval_from_match(total, 1)
        elif block.total is None:
            block.total = interval_from_match(total, 1)
        return
    if in_difference:
        return
    br4 = BR4_RE.match(line)
    if br4:
        block.br4[int(br4.group(1))] = interval_from_match(br4, 2)
        return
    br4_list = BR4_LIST_RE.match(line)
    if br4_list:
        items = [interval_from_match(m, 1) for m in LIST_ITEM_RE.finditer(br4_list.group(1))]
        block.br4 = dict(enumerate(items, start=1))
        return
    ready = READY_RE.match(line)
    if ready and block.ready_rate is None:
        block.ready_rate = interval_from_match(ready, 1)


def german(value: Decimal | float | str, digits: int = 2) -> str:
    """Round half up to the given places and write a decimal comma, as in the thesis. Never prints '-0,00'."""
    number = value if isinstance(value, Decimal) else Decimal(str(value))
    quantum = Decimal(1).scaleb(-digits)
    rounded = number.quantize(quantum, rounding=ROUND_HALF_UP)
    if rounded == 0:
        rounded = abs(rounded)
    return f"{rounded:.{digits}f}".replace(".", ",")


def interval_text(interval: Interval | None, digits: int = 2) -> str:
    if interval is None:
        return "–"
    return f"{german(interval.value, digits)} ± {german(interval.half_width, digits)}"
