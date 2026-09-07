#!/usr/bin/env python3
"""Describing a clash test must never resolve one of its sides.

The user's report: "why do i get multiple searching windows when i click read results, what is
it searching?"

Nothing the user asked for. A clash test's side is usually a saved *search* set, and a search set
does not hold elements - it holds criteria. Asking the host what such a selection contains makes
it run the search, over the whole federation, with its own "Searching" window over the top. Per
side, per test, on the one press where somebody is waiting to see their clashes. And it was for
two lines of text on a rail saying which sets a test is between.

The host will answer that question without searching. A selection source is a pointer, and
ResolveSelectionSource hands back the saved item it points at - a lookup in the saved-set tree,
whether or not the set is a search. Where a side is not sources at all, ExplicitSelection is what
was statically picked, with nothing to resolve.

So the clash source may not call the resolving forms. It never needs them: every element it hands
upward comes off a result the engine is already holding.

**Why a check.** The costly call and the cheap one differ by a word, both compile, and the
difference is invisible on the developer's two-file test model and ruinous on a real federation.
That is exactly the kind of mistake that comes back.

    python3 checks/validate-reads-dont-resolve-selections.py
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent

# The clash source: it describes and reads tests, and reaches elements only through the engine's
# own handles. Nothing it does is worth a search.
WATCHED = [ROOT / "CamelWorks.Nav.Clash" / "NavClashSource.cs"]

# Calls that make the host resolve a selection - which runs the search behind a search set.
RESOLVES = re.compile(
    r"\.GetSelectedItems\s*\(" 
    r"|\.FindAll\s*\("
    r"|\.FindFirst\s*\("
    r"|\.TryGetSelectedItems\s*\("
    r"|\.CreateSelectionSource\s*\([^)]*\)\s*\)?\s*;?\s*$"
)

# CreateSelectionSource is fine when a side is being *written* - the source is the thing stored.
# It is only the wholesale sourcing of every set in the document that was a problem, and that is
# what the name below caught.
#
# `sources.Add` is the same writing path with more than one set on a side: the collection is
# built up rather than in one initialiser. Still the sets named on one side of one test, never
# every set in the document.
ALLOWED_LINE = re.compile(
    r"selection\.Selection\.CopyFrom"
    r"|sources\s*=\s*new SelectionSourceCollection"
    r"|sources\.Add\s*\(")


def main():
    bad = []

    for path in WATCHED:
        if not path.exists():
            print("{} is not there any more. Update this check.".format(path))
            return 1

        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            code = line.split("//", 1)[0]

            if "///" in line or not RESOLVES.search(code):
                continue

            if ALLOWED_LINE.search(code):
                continue

            bad.append((str(path.relative_to(ROOT)), number, line.strip()))

    if not bad:
        print("clash reads resolve nothing: {} file(s) describe tests without running a search"
              .format(len(WATCHED)))
        return 0

    print("A clash read that makes the host resolve a selection:\n")

    for name, number, text in bad:
        print("  {}:{}  {}".format(name, number, text))

    print("\nA clash test's side is usually a saved search set, and resolving one runs the search")
    print("over the whole federation with the host's own \"Searching\" window up - per side, per")
    print("test, on the press where somebody is waiting to see their clashes.")
    print("To name the set, hand the source to DocumentSelectionSets.ResolveSelectionSource, which")
    print("is a lookup. To read what was statically picked, use Selection.ExplicitSelection.")
    print("To reach an element, go through the result the engine is already holding.")

    return 1


if __name__ == "__main__":
    sys.exit(main())
