#!/usr/bin/env python3
"""Fail when a progress pump is gated behind an item counter.

Job.Step() already rate-limits its dispatcher pump to 90ms internally. A call site that guards it
with `count % N == 0` replaces that time limit with an item limit, and the two are not the same
thing: on a real federation reading an element's bounds is expensive, so 2,000 elements is many
seconds of wall clock. The pump is the only thing keeping the host alive and the Stop button
clickable, so a count gate is a freeze with a progress bar drawn on it.

The stopwatch check that Step() does per call costs nothing. There is no reason to gate it, and no
test can catch one — Job lives in the WPF assembly, which does not run on the build machine.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
UI = ROOT / "CamelWorks.UI"

COUNTS = re.compile(r"%\s*\d+\s*==\s*0")
PUMPS = re.compile(r"\b\w*[Jj]ob\.(?:Step|Say)\b")


def main():
    problems = 0

    for path in sorted(UI.rglob("*.cs")):
        lines = path.read_text(encoding="utf-8").splitlines()

        for n, line in enumerate(lines, 1):
            if not COUNTS.search(line):
                continue

            # The gate and the pump on one line, or the gate left open at the end of one and the
            # pump on the next. Anything further apart is not this pattern.
            after = lines[n] if n < len(lines) else ""
            gated = PUMPS.search(line) or (line.rstrip().endswith("0") and PUMPS.search(after))

            if gated:
                rel = path.relative_to(ROOT)
                print(f"FAIL: {rel}:{n}: a progress pump behind a count gate")
                print(f"      {line.strip()}")
                if not PUMPS.search(line):
                    print(f"      {after.strip()}")
                print("      Step() rate-limits itself to 90ms. Call it every iteration.")
                problems = 1

    if problems:
        return 1

    calls = sum(len(re.findall(r"\b\w*[Jj]ob\.Step\b", p.read_text(encoding="utf-8")))
                for p in UI.rglob("*.cs"))
    print(f"pump ungated: {calls} Step calls, none behind a counter")
    return 0


if __name__ == "__main__":
    sys.exit(main())
