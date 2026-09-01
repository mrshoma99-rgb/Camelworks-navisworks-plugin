#!/usr/bin/env python3
"""The clash board's rows must be built only when they are on screen.

The user's report: "performance is a huge issue".

The board drew every result it was showing as a grid of a couple of dozen elements — all of them,
whether on screen or three thousand rows below it — into a stack panel. A test with five thousand
results is well over a hundred thousand WPF elements, rebuilt from scratch by a keystroke in the
search box, a tick on one row, or any single mouse-move of a column drag.

Two things make it not do that, and both are easy to undo without noticing:

  1. The rows go into a BoardList, which realises only what is visible and recycles containers.
  2. Nothing vertically scrolling wraps it. A virtualising list inside a vertical ScrollViewer is
     handed unbounded height, realises every row to fill it, and virtualises nothing — the code
     looks right and the freeze is back.

Neither has a symptom a reader would notice: the board still works, it is just slow again on the
size of job nobody tests with. So they are checked.

    python3 checks/validate-board-virtualised.py
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
BOARD = ROOT / "CamelWorks.UI" / "Views" / "ClashManageViews.cs"
LIST = ROOT / "CamelWorks.UI" / "Views" / "BoardList.cs"
PANE = ROOT / "CamelWorks.UI" / "Views" / "ClashManageViews.cs"


def fail(message):
    print("FAIL: " + message)
    return 1


def main():
    problems = 0

    board = BOARD.read_text(encoding="utf-8", errors="replace")
    lister = LIST.read_text(encoding="utf-8", errors="replace")

    if "new BoardList(" not in board:
        problems |= fail("the clash board no longer builds a BoardList, so every row it shows "
                         "exists whether or not anybody can see it")

    for setting, what in (
        (r"SetIsVirtualizing\(this,\s*true\)", "virtualising"),
        (r"VirtualizationMode\.Recycling", "recycling its containers"),
        (r"CanContentScrollProperty,\s*true", "scrolling by item, which is what lets it virtualise"),
    ):
        if not re.search(setting, lister):
            problems |= fail("BoardList has stopped " + what)

    # The board's host must not scroll vertically: that hands the list unbounded height.
    host = re.search(r"private readonly (\w+) _board\b", board)

    if host is None:
        problems |= fail("the clash board has no _board host to check")
    elif host.group(1) == "ScrollViewer":
        problems |= fail("_board is a ScrollViewer, so the row list is given unbounded height "
                         "and realises every row to fill it")

    if re.search(r"Content = _board,", board):
        # Only a problem when what holds it scrolls.
        for match in re.finditer(r"new ScrollViewer\s*\{[^}]*Content = _board", board, re.S):
            problems |= fail("_board sits inside a ScrollViewer at offset "
                             + str(match.start()) + "; a vertically scrolling parent switches the "
                             "virtualisation off")

    if problems:
        print("\nThe board shows one screenful of rows at a time on purpose. Building all of them,")
        print("or putting the list somewhere with no height of its own, is the freeze coming back.")
        return 1

    print("the clash board builds only the rows it is showing")
    return 0


if __name__ == "__main__":
    sys.exit(main())
