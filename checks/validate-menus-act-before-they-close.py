#!/usr/bin/env python3
"""A menu must not close before the option somebody pressed has acted.

The user's reports, in one afternoon: "the drop down set status list opens but doesn't actually
change the status", "dissolve this group doesn't actually work", "rename this test doesn't work
too", "move into a folder, all it does is change the selected test", "all the filtering options of
the clash results don't work", "all the control options of the clash tests don't work".

One cause. The popup that carries every menu and every dropdown in the product closed itself from
a *Preview* handler:

    card.PreviewMouseLeftButtonUp += (s, e) => popup.IsOpen = false;

Preview tunnels. It runs on the way *down*, before the press reaches the option - and closing a
Popup pulls its child out of the visual tree, so the option's own bubbling handler was never
raised. Every menu opened, highlighted, closed on click, and did nothing.

It read as correct: the comment beside it said the card's handler ran after the option's own, which
is true of the bubbling event and the exact opposite of the tunnelling one. Nothing failed, nothing
threw, and six separate verbs were reported as broken before the shape was visible.

So: no handler in the UI may close a popup from a Preview event. The bubbling form runs after the
option, which is the order this needs.

    python3 checks/validate-menus-act-before-they-close.py
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
UI = ROOT / "CamelWorks.UI"

# A tunnelling mouse handler, and a close of a popup. Either alone is fine; the two in one
# statement is the fault.
PREVIEW = re.compile(r"Preview(?:Mouse|Touch|Stylus)\w*(?:Down|Up)\b")
CLOSES = re.compile(r"IsOpen\s*=\s*false")


def main():
    bad = []

    for path in sorted(UI.rglob("*.cs")):
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()

        for number, line in enumerate(lines, 1):
            if "///" in line or line.strip().startswith("//"):
                continue

            if not PREVIEW.search(line):
                continue

            # The close may be on the same line or in the two that follow it, which is how a
            # lambda body normally lands.
            window = " ".join(lines[number - 1:number + 2])

            if CLOSES.search(window):
                bad.append((str(path.relative_to(ROOT)), number, line.strip()))

    if not bad:
        print("menus act before they close: no popup is shut from a tunnelling handler")
        return 0

    print("A menu that closes before its option has acted:\n")

    for name, number, text in bad:
        print("  {}:{}  {}".format(name, number, text))

    print("\nPreview handlers tunnel - they run before the press reaches the option. Closing a")
    print("Popup removes its child from the visual tree, so the option's own handler never runs")
    print("and the press does nothing at all, silently.")
    print("Use the bubbling event instead, which runs after the option:")
    print("  card.AddHandler(UIElement.MouseLeftButtonUpEvent,")
    print("                  new MouseButtonEventHandler((s, e) => popup.IsOpen = false),")
    print("                  handledEventsToo: true);")

    return 1


if __name__ == "__main__":
    sys.exit(main())
