#!/usr/bin/env python3
"""Every modal this product shows must be owned by the window it came from.

The user's report: "opening the filter dialogue in the issues panel then closing it still crashes
Navisworks, same behaviour that we supposedly fixed before."

The mechanism is in HostWindow's own doc comment. A modal with no owner disables every top-level
window on the thread - the host's native frame among them - and re-enables them from the owner it
does not have. What somebody sees is a dialog they closed and a Navisworks that never comes back.

It was fixed once, for the WPF windows, by routing them through HostWindow.Own. It came back
because the fix was a rule rather than a mechanism: twenty file dialogs were written afterwards,
each calling ShowDialog() with no owner, and nothing objected. A rule that lives in somebody's head
holds until the next person writes the twenty-first.

So this is the mechanism. A ShowDialog anywhere in the UI must either go through Ui.Asked - the one
door for file dialogs, which owns them - or sit in a method that has already called HostWindow.Own
on the window it is about to show.

A message box is the same family and was missed the first time round: MessageBox.Show with no owner
disables the host's frame in exactly the same way, and the boxes that say why a tool would not open
are shown precisely when there is no screen up to own them. They go through Ui.Told, which owns
them, or they name their owner in the call.

    python3 checks/validate-owned-dialogs.py
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
UI = ROOT / "CamelWorks.UI"

# The one door for a file dialog, and the file it lives in.
DOOR = "Asked"
DOOR_FILE = "Ui.cs"

CALL = re.compile(r"\.ShowDialog\s*\(|(?<![.\w])MessageBox\.Show\s*\(")

# A message box that names an owner as its first argument is owned, whatever else it does. Both
# families: a WPF Window and a WinForms IWin32Window.
NAMES_OWNER = re.compile(r"MessageBox\.Show\s*\(\s*(?:owner|frame|this|HostWindow\.|Shell\.HostWindow\.)")

# The one door for a message box, and the method that is allowed to show an unowned one because it
# is the fallback for a host that will not say where its frame is.
BOXES = ("Told", "Warn")
OWNED = re.compile(r"HostWindow\.Own\s*\(")
METHOD = re.compile(r"^\s*(?:(?:public|private|internal|protected|static|sealed|override|async)\s+)+[\w<>\[\],?\s]+\s+(\w+)\s*\(")


def methods(lines):
    """Where each method starts, by line index. Crude and good enough: a ShowDialog is judged
    against the nearest method header above it, which is the scope its Own would have to be in."""
    starts = []

    for i, line in enumerate(lines):
        if METHOD.match(line) and "=>" not in line:
            starts.append(i)

    return starts


def scope_of(index, starts):
    here = 0

    for start in starts:
        if start <= index:
            here = start
        else:
            break

    return here


def check(path):
    text = path.read_text(encoding="utf-8", errors="replace")

    if not CALL.search(text):
        return []

    lines = text.splitlines()
    starts = methods(lines)
    found = []

    for i, line in enumerate(lines):
        if not CALL.search(line):
            continue

        # The door itself is where the owning happens.
        if path.name == DOOR_FILE and DOOR in line:
            continue

        start = scope_of(i, starts)
        scope = "\n".join(lines[start:i + 1])

        if path.name == DOOR_FILE and re.search(r"\b" + DOOR + r"\b", scope):
            continue

        if OWNED.search(scope):
            continue

        if NAMES_OWNER.search(line):
            continue

        # The last-resort boxes themselves: each tries an owner first and falls back only when
        # the host will not name its frame at all.
        header = lines[start] if start < len(lines) else ""
        if any(re.search(r"\b" + name + r"\s*\(", header) for name in BOXES):
            continue

        found.append((i + 1, line.strip()))

    return found


def main():
    bad = []

    for path in sorted(UI.rglob("*.cs")):
        for line, text in check(path):
            bad.append((path.relative_to(ROOT), line, text))

    if not bad:
        print("every dialog is owned by the window it came from")
        return 0

    print("Dialogs shown with no owner:\n")

    for path, line, text in bad:
        print("  {}:{}".format(path, line))
        print("    {}".format(text))

    print("\nAn unowned modal disables the host's frame and re-enables it from an owner it does")
    print("not have, so closing it leaves Navisworks dead. Use Ui.AskToSave / Ui.AskToOpen for a")
    print("file dialog, Ui.Told for a message, or call HostWindow.Own(window) before showing a")
    print("window of your own.")

    return 1


if __name__ == "__main__":
    sys.exit(main())
