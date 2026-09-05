#!/usr/bin/env python3
"""A header is described once, and the frame is told nothing.

The matrix's corner, its band across the top and its band down the side are one frame: they have
to agree to the pixel or the headers stop sitting over the cells they name. Every way that has
gone wrong here has gone wrong the same way - one box described in two places, both descriptions
perfectly good arithmetic, and only a picture showing which of them is right.

Twice now. A column header reserved six pixels of padding over its words and four under them, and
the formula that worked out how tall the band should be did not know they existed, so every name
in every column was cut off by exactly ten pixels. Then a strip for the folders was added, two of
the four places that summed the frame learned about it and two did not, and the frame disagreed
with itself by exactly the band - row names clipped off the side, turned column names sliced, the
corner a third of its size.

Neither is possible now, and this keeps it that way:

  * The headers are built from ColumnShell() and RowShell(), which are the only places that say
    what a header is made of. HeaderHigh and HeaderWide measure those same shells. Padding named
    for the shell has no business anywhere else - that is the first bug, exactly.

  * Nothing hands the corner or the bands a size. The frame is a Grid with an auto row and an
    auto column, so it is as big as the bands turn out to be, worked out by the layout. An
    assignment putting a number back on one of them is the second bug, exactly.
"""
import re
import sys
from pathlib import Path

SOURCE = Path("CamelWorks.UI/Views/MatrixWindow.cs")

# The parts the frame is made of. Two of them added together is somebody working the frame's
# size out for themselves again, which is the arithmetic the auto tracks were brought in to end.
PARTS = ("IndexWide", "HeaderWide", "FolderWide", "IndexHigh", "HeaderHigh", "FolderHigh")

# The frozen panes. Given a size, they stop following the headers and start disagreeing with them.
TOLD = (
    r"_band\s*\.\s*Height\s*=",
    r"_side\s*\.\s*Width\s*=",
    r"_corner\s*\.\s*(Width|Height)\s*=",
    r"corner\s*\.\s*(Width|Height)\s*=",
)

# The column header's own padding. It belongs to the shell that draws it and to nothing else:
# read anywhere else, it is a second opinion about how tall a header is.
PADDING = ("HeadPadTop", "HeadPadFoot")

SHELL = "private Border ColumnShell"


def main() -> int:
    if not SOURCE.exists():
        print(f"{SOURCE}: not found", file=sys.stderr)
        return 1

    lines = SOURCE.read_text(encoding="utf-8").splitlines()

    summed = []
    told = []
    padded = []

    # Which lines are inside ColumnShell, where the padding is allowed to be named.
    shell = set()

    for number, line in enumerate(lines, 1):
        if SHELL in line:
            depth = 0
            seen = False

            for after in range(number - 1, len(lines)):
                shell.add(after + 1)
                depth += lines[after].count("{") - lines[after].count("}")

                if lines[after].count("{"):
                    seen = True

                if seen and depth <= 0:
                    break

    for number, line in enumerate(lines, 1):
        said = line.split("//")[0]

        if said.strip().startswith("///"):
            continue

        for left in PARTS:
            if any(re.search(rf"\b{left}\s*\+\s*{right}\b", said) for right in PARTS):
                summed.append((number, line.strip()))
                break

        for shape in TOLD:
            if re.search(shape, said):
                told.append((number, line.strip()))
                break

        if number not in shell:
            for pad in PADDING:
                if re.search(rf"\b{pad}\b", said):
                    padded.append((number, line.strip()))
                    break

    def report(bad, title, fix):
        if not bad:
            return False

        print(title)

        for number, said in bad:
            print(f"  {SOURCE}:{number}: {said}")

        print()
        print(fix)
        print()

        return True

    wrong = report(
        summed,
        "The frozen frame's size is being worked out by hand:",
        "Nothing needs to. The frame is a Grid with an auto row and an auto column, so it is\n"
        "already as big as the bands it holds - and a sum written beside that is a second\n"
        "opinion that can differ from it without anything noticing.",
    )

    wrong |= report(
        told,
        "The corner or one of the bands is being told how big to be:",
        "Let it follow. The headers decide the frame, the auto tracks pass that on, and the\n"
        "corner's contents sit in a Follows panel so they cannot decide it instead. A size\n"
        "asserted here is the frame disagreeing with the headers again.",
    )

    wrong |= report(
        padded,
        "The column header's padding is named outside the shell that draws it:",
        "ColumnShell() is the one place that says what a column header is made of, and\n"
        "HeaderHigh measures it. Reading its padding somewhere else is how the band came out\n"
        "ten pixels shorter than the header standing in it, with every name cut off.",
    )

    return 1 if wrong else 0


if __name__ == "__main__":
    raise SystemExit(main())
