#!/usr/bin/env python3
"""Hold the line on tables nobody can pick rows in.

The complaint, in the user's words: "all tables have no multiselect rows options, or select all or
deselect all options". It was not a handful of screens. At the time this was written the product
had fifty-five tables and two of them had a bar.

Fixing fifty-three in one pass, on a machine with no C# compiler, is how a redesign turns into a
week of red builds. So this is a ratchet instead: it records how many bare tables each file has
today and fails when a file grows another one. The number can go down and never up, and a file
that reaches zero is struck off, so the debt is visible, finite, and cannot quietly grow back
while the screens around it are rewritten.

What counts as bare is deliberately crude - tables in the file, minus the calls that give a table
its verbs: Ui.Picking, which draws the bar under it, and Ui.Menu, which hangs the same verbs off
right-click. A ratchet does not need to know which table is which; it needs to be impossible to
satisfy by accident. Giving one more table its verbs is the only way to move a number down.

    python3 checks/validate-table-picking.py            # check
    python3 checks/validate-table-picking.py --record   # take the baseline down to what is true
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
UI = ROOT / "CamelWorks.UI"
BASELINE = pathlib.Path(__file__).resolve().parent / "tables-without-picking.txt"

TABLE = re.compile(r"\bUi\.Table\(")

# Two ways a table gets its verbs. The bar came first; the right-click menu arrived with the
# clash bench, where twelve verbs under two tables would have been two walls of buttons.
PICKING = re.compile(r"\bUi\.Picking\(|\.Menu\(")


def bare():
    """How many tables in each file have no picking bar, worst first."""
    counts = {}

    for path in sorted(UI.rglob("*.cs")):
        source = path.read_text(encoding="utf-8", errors="replace")

        tables = len(TABLE.findall(source))
        if tables == 0:
            continue

        left = tables - len(PICKING.findall(source))
        if left > 0:
            counts[str(path.relative_to(ROOT)).replace("\\", "/")] = left

    return counts


def recorded():
    """The baseline, or an empty one when there is not a file yet."""
    if not BASELINE.exists():
        return {}

    found = {}
    for line in BASELINE.read_text(encoding="utf-8").splitlines():
        line = line.split("#", 1)[0].strip()
        if not line:
            continue
        name, _, count = line.rpartition(" ")
        found[name.strip()] = int(count)
    return found


def write(counts):
    lines = [
        "# Tables with no picking bar, per file. A ratchet: these numbers may fall, never rise.",
        "# Wrap a table in Ui.Picking, then run checks/validate-table-picking.py --record.",
        "",
    ]
    lines += [f"{name} {counts[name]}" for name in sorted(counts)]
    BASELINE.write_text("\n".join(lines) + "\n", encoding="utf-8")


def main():
    counts = bare()

    if "--record" in sys.argv:
        write(counts)
        print(f"recorded: {sum(counts.values())} bare tables across {len(counts)} files")
        return 0

    was = recorded()
    problems = 0

    for name, count in sorted(counts.items()):
        before = was.get(name)

        if before is None:
            print(f"FAIL: {name} has {count} table(s) with no picking bar, and is not in the baseline")
            problems += 1
        elif count > before:
            print(f"FAIL: {name} went from {before} bare table(s) to {count}")
            problems += 1

    for name, before in sorted(was.items()):
        now = counts.get(name, 0)
        if now < before:
            print(f"FAIL: {name} is down to {now} bare table(s) from {before} - "
                  f"run --record so the ratchet holds there")
            problems += 1

    if problems:
        print("\nEvery table should let somebody pick rows and act on the count. Where one truly "
              "cannot\n- a single-row summary, a read-only preview - wrap it anyway or say why in "
              "the baseline.")
        return 1

    total = sum(counts.values())
    print(f"table picking: {total} bare table(s) left, none new")
    return 0


if __name__ == "__main__":
    sys.exit(main())
