#!/usr/bin/env python3
"""Hold the line on collapsible sections.

Said three times, the last with three exclamation marks: "enough with these collabsable tabs",
"three different collabsables tabs inside a dialouge window !!!". The law that came out of it is
one fold per surface - the tuning band - holding what somebody can leave alone, and never a
required input.

At the time this was written there were thirty-five Ui.Section calls across twelve files.

A file is not a surface: several screens can live in one file, so a count per file is a proxy
rather than the law itself. It is still the useful proxy, because the way this goes wrong is one
screen growing a second and third fold, and that always shows up as its file's number rising. The
one thing this must do is make that impossible to do quietly.

Same ratchet as the table bars: the numbers fall and never rise.

    python3 checks/validate-one-fold.py            # check
    python3 checks/validate-one-fold.py --record   # take it down to what is true
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
UI = ROOT / "CamelWorks.UI"
BASELINE = pathlib.Path(__file__).resolve().parent / "folds.txt"

SECTION = re.compile(r"\bUi\.Section\(")


def folds():
    counts = {}
    for path in sorted(UI.rglob("*.cs")):
        found = len(SECTION.findall(path.read_text(encoding="utf-8", errors="replace")))
        if found:
            counts[str(path.relative_to(ROOT)).replace("\\", "/")] = found
    return counts


def recorded():
    if not BASELINE.exists():
        return {}
    found = {}
    for line in BASELINE.read_text(encoding="utf-8").splitlines():
        line = line.split("#", 1)[0].strip()
        if line:
            name, _, count = line.rpartition(" ")
            found[name.strip()] = int(count)
    return found


def main():
    counts = folds()

    if "--record" in sys.argv:
        BASELINE.write_text(
            "# Collapsible sections per file. A ratchet: these may fall, never rise.\n"
            "# One fold per surface is the law; fold three into one, then --record.\n\n"
            + "\n".join(f"{n} {counts[n]}" for n in sorted(counts)) + "\n",
            encoding="utf-8")
        print(f"recorded: {sum(counts.values())} fold(s) across {len(counts)} files")
        return 0

    was = recorded()
    problems = 0

    for name, count in sorted(counts.items()):
        before = was.get(name)
        if before is None:
            print(f"FAIL: {name} has {count} fold(s) and is not in the baseline")
            problems += 1
        elif count > before:
            print(f"FAIL: {name} went from {before} fold(s) to {count}")
            problems += 1

    for name, before in sorted(was.items()):
        now = counts.get(name, 0)
        if now < before:
            print(f"FAIL: {name} is down to {now} fold(s) from {before} - run --record")
            problems += 1

    if problems:
        print("\nOne fold per surface, holding what can be left alone. Three stacked folds are "
              "three\nquestions asked before the job is done.")
        return 1

    print(f"folds: {sum(counts.values())} left, none new")
    return 0


if __name__ == "__main__":
    sys.exit(main())
