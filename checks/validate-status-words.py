#!/usr/bin/env python3
"""Fail when CamelWorks.UI spells a finding status as a string literal.

A status is FindingStatus. The screens used to hold it as a string from the host read all the way
to the board, with the five words written out at eighty-odd sites: tallies, filters, sort keys,
chip option lists, column headings, clipboard cells. Two of those literal sets disagreed - one
ranked an unrecognised word last and one dropped it from the tally entirely - so the same result
could be counted one way and ordered another, and neither the compiler nor a test could see it.

Two rules, both mechanical:

  * no bare "New" / "Active" / "Reviewed" / "Approved" / "Resolved" string literal in
    CamelWorks.UI, outside the allow-list below;
  * no call to FindingStatusLattice.ToName outside FindingStatusWords, which is the one place a
    status becomes text in CamelWorks.UI.

The allow-list is two sites, both by name and by what is on the line, so it cannot quietly grow or
drift onto a different line:

  * Views/Ui.cs - the words appear inside a doc comment, as prose about a board;
  * Views/MatrixWindow.cs - MatrixChange.Added => "New" is the matrix's word for a change kind,
    not a finding status.

CamelWorks.Core is not in scope: FindingStatusLattice and ClashStatusMap are where the words are
allowed to exist, and ClashStatusMap.Read is the one reader of a word from a host or a file.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
UI = ROOT / "CamelWorks.UI"

WORDS = ("New", "Active", "Reviewed", "Approved", "Resolved")

LITERAL = re.compile(r'"(' + "|".join(WORDS) + r')"')

NAMING = "FindingStatusWords.cs"
TONAME = re.compile(r"\bFindingStatusLattice\s*\.\s*ToName\b")

# Path relative to CamelWorks.UI, and the text the allowed line must still contain.
ALLOWED = {
    ("Views/Ui.cs", 'A board that prints "12 new" over a list'),
    ("Views/MatrixWindow.cs", 'MatrixChange.Added => "New"'),
}


def main():
    if not UI.is_dir():
        print(f"no such directory: {UI}", file=sys.stderr)
        return 1

    problems = []
    allowed_seen = set()
    counted = 0

    for path in sorted(UI.rglob("*.cs")):
        where = path.relative_to(UI).as_posix()

        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if LITERAL.search(line):
                counted += 1

                excuse = next((a for a in ALLOWED if a[0] == where and a[1] in line), None)

                if excuse is None:
                    problems.append(f"{where}:{number}: status word as a string literal - "
                                    f"use FindingStatus and FindingStatusWords.Of")
                else:
                    allowed_seen.add(excuse)

            if TONAME.search(line) and path.name != NAMING:
                problems.append(f"{where}:{number}: FindingStatusLattice.ToName - "
                                f"CamelWorks.UI names a status through FindingStatusWords.Of")

    for excuse in sorted(ALLOWED - allowed_seen):
        problems.append(f"{excuse[0]}: the allow-listed line is gone - "
                        f"drop it from ALLOWED in {Path(__file__).name}")

    for problem in problems:
        print(problem, file=sys.stderr)

    if problems:
        return 1

    print(f"status words clear: {counted} literals in CamelWorks.UI, all {len(ALLOWED)} allow-listed")
    return 0


if __name__ == "__main__":
    sys.exit(main())
