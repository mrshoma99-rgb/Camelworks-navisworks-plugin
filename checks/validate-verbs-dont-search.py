#!/usr/bin/env python3
"""A screen whose row verbs resolve element keys must build the index in its own job.

The user's report, three times: "clicking focus on any clash still freezez the whole thing".

Resolving an element key that is not an instance GUID means looking it up in an index of every
geometry leaf in the federation. Built lazily, that walk lands inside whichever click handler
happens to be first - minutes of managed work on the UI thread with no progress and no way out,
which is a Navisworks that has stopped answering.

The answer is not a progress bar on the walk. It is that the walk should already have happened:
the long job the screen starts anyway - reading a board, running a check - has a progress bar and
a Stop, and it is the place to pay.

**Why this is a check and not a comment.** It was written that way once, in the commit that first
fixed this. A later commit rewrote the same method for an unrelated feature and deleted the call
along with the method holding it. Nothing objected, and the freeze came back exactly as before.
A fix that exists only as a call site lasts until the next person refactors around it.

So: a file whose verbs resolve keys - Model.Select, View.Isolate, View.ZoomTo, SelectByLocator, or
a check's ICheckGeometry.Locate - must also call Index somewhere. Better still is not to resolve at
all, which is why a file that reaches the model only through the clash engine's own handles is not
asked for one.

**The seam counts too.** The clash board reaches its elements through ICheckGeometry: Select hands
back what the producer is already holding and resolves nothing, and Locate returns the keys and
paths a caller then has to resolve. A check pass that moved those calls behind the seam without
this line would leave the check passing by finding nothing, which is worse than failing.

    python3 checks/validate-verbs-dont-search.py
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
UI = ROOT / "CamelWorks.UI"

# Reaching the model with keys, which is what can cost a walk. The last two are the check seam:
# SelectByLocator resolves whatever the locator could not answer directly, and ICheckGeometry.Locate
# is what hands a screen the keys to resolve.
RESOLVES = re.compile(r"\.Model\.Select\s*\(|\.View\.Isolate\s*\(|\.View\.ZoomTo\s*\("
                      r"|SelectByLocator\s*\(|\.Geometry[?]?\.Locate\s*\(")

# Paying for it where there is a progress bar and a Stop: the one shared door.
BUILDS = re.compile(r"Ui\.Ready\s*\(")

# A screen with no long job of its own has nowhere to build one.
HAS_JOB = re.compile(r"\bJob\s+job\b|Ui\.Runner\s*\(")

# The ones not done yet, recorded rather than pretended about.
KNOWN = ROOT / "checks" / "lazy-key-resolution.txt"


def known():
    listed = []

    for line in KNOWN.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            listed.append(line)

    return listed


def main():
    listed = known()
    bad = []
    resolving = []

    for path in sorted(UI.rglob("*.cs")):
        text = path.read_text(encoding="utf-8", errors="replace")

        if not RESOLVES.search(text):
            continue

        name = str(path.relative_to(ROOT))
        resolving.append(name)

        if not HAS_JOB.search(text):
            continue

        if BUILDS.search(text) or name in listed:
            continue

        lines = text.splitlines()
        where = next((i + 1 for i, l in enumerate(lines) if RESOLVES.search(l)), 0)

        bad.append((name, where))

    # A line that no longer resolves anything is a line that has been done, and leaving it here
    # would let the next one hide behind it.
    stale = [name for name in listed if name not in resolving]

    if not bad and not stale:
        print("{} screens resolve keys; {} pay in their own job, {} recorded as owed"
              .format(len(resolving), len(resolving) - len(listed), len(listed)))
        return 0

    if bad:
        print("Row verbs that can walk the federation inside a click:\n")

        for name, line in bad:
            print("  {}:{}".format(name, line))

        print("\nResolving a key that is not an instance GUID builds an index of every geometry")
        print("leaf in the federation. Inside a press that is a host that has stopped answering.")
        print("Call Ui.Ready(job, keys) from the screen's own read or check job - or better, reach")
        print("the elements through handles the host is already holding and never resolve at all.")
        print("If it genuinely cannot be done yet, add the file to checks/lazy-key-resolution.txt")
        print("and say why.")

    if stale:
        print("\nListed in checks/lazy-key-resolution.txt but no longer resolving keys:\n")

        for name in stale:
            print("  " + name)

        print("\nRemove the line. The list may only shrink, or it stops meaning anything.")

    return 1


if __name__ == "__main__":
    sys.exit(main())
