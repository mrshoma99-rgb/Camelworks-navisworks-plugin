#!/usr/bin/env python3
"""Having selected the elements, act on the selection - not on the keys again.

Resolving an element key that is not an instance GUID means looking it up in an index of every
geometry leaf in the federation, and building that index is a full walk. So a verb pays for the
walk once, on its first key. What it must not do is pay again in the same press.

That is what this sequence does:

    _session.Model.Select(keys);        // resolves every key
    _session.View.ZoomTo(keys, 1.0);    // resolves every key, again

and this one pays three times:

    _session.View.Isolate(keys);
    _session.Model.Select(keys);
    _session.View.ZoomTo(keys, 1.0);

The host is already holding what Select just put in the current selection, so the framing and the
isolate can act on that: IsolateSelection() and ZoomToSelection() take no keys and look nothing up.
It was reported as Focus and Isolate being slow, on three separate screens, and each one was the
same three lines.

So: a screen that calls Model.Select(keys) - or selects through a check's ICheckGeometry, which
does the same thing with the producer's own handles - may not also hand the same keys to
View.ZoomTo or View.Isolate. A screen that never selects is left alone: there the keys are the only
handle it has.

The seam is named here for the same reason it is named in validate-verbs-dont-search.py. Selecting
through ICheckGeometry.Select is still a selection, and a screen that framed the keys again
afterwards would resolve every one of them a second time.

    python3 checks/validate-select-then-act-on-selection.py
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
UI = ROOT / "CamelWorks.UI"

SELECTS = re.compile(r"\.Model\.Select\s*\(|\.Geometry[?]?\.Select\s*\("
                     r"|SelectByLocator\s*\(")
AGAIN = re.compile(r"\.View\.(ZoomTo|Isolate)\s*\(")

# How far after a Select the second resolution still counts as the same press. Generous: the
# reported ones were one and two lines apart.
WINDOW = 6

# Screens not yet moved over, recorded rather than pretended about. The list may only shrink.
KNOWN = ROOT / "checks" / "keys-resolved-twice.txt"


def listed():
    if not KNOWN.exists():
        return []

    return [line.strip() for line in KNOWN.read_text(encoding="utf-8").splitlines()
            if line.strip() and not line.strip().startswith("#")]


def main():
    allowed = listed()
    bad = []
    seen = []

    for path in sorted(UI.rglob("*.cs")):
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
        name = str(path.relative_to(ROOT))

        for number, line in enumerate(lines, 1):
            if "///" in line or line.strip().startswith("//"):
                continue

            if not SELECTS.search(line):
                continue

            for ahead in lines[number:number + WINDOW]:
                if "///" in ahead or ahead.strip().startswith("//"):
                    continue

                hit = AGAIN.search(ahead)

                if hit:
                    seen.append(name)

                    if name not in allowed:
                        bad.append((name, number, hit.group(1)))
                    break

    stale = [name for name in allowed if name not in seen]

    if not bad and not stale:
        print("verbs resolve once: {} screen(s) recorded as owed".format(len(allowed)))
        return 0

    if bad:
        print("Keys resolved twice in one press:\n")

        for name, number, what in bad:
            print("  {}:{}  Model.Select, then View.{} on the same keys".format(name, number, what))

        print("\nSelect resolves every key; handing the same keys to ZoomTo or Isolate resolves")
        print("them all over again, and the first resolution of a key that is not an instance GUID")
        print("indexes the whole federation. Act on what Select just selected instead:")
        print("  _session.Model.Select(keys);")
        print("  _session.View.IsolateSelection();")
        print("  _session.View.ZoomToSelection(margin);")
        print("If a screen genuinely cannot yet, add it to checks/keys-resolved-twice.txt and say why.")

    if stale:
        print("\nListed in checks/keys-resolved-twice.txt but no longer resolving twice:\n")

        for name in stale:
            print("  " + name)

        print("\nRemove the line. The list may only shrink, or it stops meaning anything.")

    return 1


if __name__ == "__main__":
    sys.exit(main())
