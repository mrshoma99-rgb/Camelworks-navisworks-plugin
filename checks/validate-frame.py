#!/usr/bin/env python3
"""The frozen frame is one sum, in one place.

The matrix's corner, its band across the top and its band down the side are one frame: they have
to agree to the pixel or the headers stop sitting over the cells they name. Four places worked
that sum out, and when a strip for the folders was added, two of them learned about it and two
were built once with the old sum - so the frame disagreed with itself by exactly the band, the
row names were clipped off the side, and the turned column names were sliced.

Nothing in a compiler can see two correct-looking sums that differ. This can: the parts of the
frame may be added up in exactly one place, and everywhere else says FrameWide or FrameHigh.
"""
import re
import sys
from pathlib import Path

SOURCE = Path("CamelWorks.UI/Views/MatrixWindow.cs")

# The parts the frame is made of. Any of these added to another, outside the one property that
# is allowed to, is a second opinion about how big the frame is.
PARTS = ("IndexWide", "HeaderWide", "FolderWide", "IndexHigh", "HeaderHigh", "FolderHigh")

ALLOWED = ("private double FrameWide", "private double FrameHigh")

def main() -> int:
    if not SOURCE.exists():
        print(f"{SOURCE}: not found", file=sys.stderr)
        return 1

    bad = []

    for number, line in enumerate(SOURCE.read_text(encoding="utf-8").splitlines(), 1):
        said = line.split("//")[0]

        if any(one in said for one in ALLOWED):
            continue

        # Two of the frame's parts added together is the sum being worked out again.
        for left in PARTS:
            for right in PARTS:
                if re.search(rf"\b{left}\s*\+\s*{right}\b", said):
                    bad.append((number, line.strip()))
                    break
            else:
                continue
            break

    if bad:
        print("The frozen frame is added up somewhere other than FrameWide/FrameHigh:")
        for number, said in bad:
            print(f"  {SOURCE}:{number}: {said}")
        print()
        print("Say FrameWide or FrameHigh instead. The frame's parts are summed in one place so")
        print("that adding one cannot leave the corner, the top band or the side band behind.")
        return 1

    return 0

if __name__ == "__main__":
    raise SystemExit(main())
