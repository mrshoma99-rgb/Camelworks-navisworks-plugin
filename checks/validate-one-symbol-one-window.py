#!/usr/bin/env python3
"""A symbol that opens a window must open a window that says which symbol opened it.

The user's report, twice: "the issue manager have all of it's option buttons open the same dialogue
window. another pattern that supposedly was fixed."

The shape of the fault is always the same. A screen has several things that are set up rather than
worked - narrow the register, what the bulk verb writes - and each gets a symbol on the action row.
Every symbol calls one helper, the helper hands the whole list of sections to Ui.Configure, and the
window that opens is identical whichever symbol was pressed: same title, same list, same section
showing. Nothing on the screen answers "what did I just press", so the row reads as one button
drawn several times.

It was fixed once, in the clash manager, by having its helper look up the one section the symbol
named. That fix was a call site, so the issue manager - whose helper only re-ordered the list -
kept the old behaviour and the report came back.

So this is the mechanism. A method that calls Ui.Configure with more than one section may be
called from one place only: the single press that means "open all of the setup for this screen".
The moment a second press reaches it, each press must name its own section.

    python3 checks/validate-one-symbol-one-window.py
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
UI = ROOT / "CamelWorks.UI"

CALL = re.compile(r"Ui\.Configure\s*\(")
METHOD = re.compile(
    r"^\s*(?:(?:public|private|internal|protected|static|sealed|override|async)\s+)+"
    r"[\w<>\[\],?\s]+\s+(\w+)\s*\(")

# What makes a call more than one section: a list or array spread into the params slot, or several
# pages written out. A single page - named at the call site or held in a variable the caller just
# chose - is one section, which is a window titled with the symbol that opened it.
MULTI = re.compile(r"\.ToArray\s*\(\s*\)|new\s+Ui\.Page\[|new\[\]")


def call_text(text, at):
    """The whole Ui.Configure(...) call from its opening paren to the matching close."""
    open_at = text.index("(", at)
    depth = 0

    for i in range(open_at, len(text)):
        if text[i] == "(":
            depth += 1
        elif text[i] == ")":
            depth -= 1
            if depth == 0:
                return text[at:i + 1]

    return text[at:]


def methods(lines):
    """Where each method starts and what it is called, by line index."""
    found = []

    for i, line in enumerate(lines):
        match = METHOD.match(line)
        if match and "=>" not in line:
            found.append((i, match.group(1)))

    return found


def enclosing(index, starts):
    here = (0, None)

    for start in starts:
        if start[0] <= index:
            here = start
        else:
            break

    return here


def presses(text, name, declared_at):
    """How many places call this method, not counting where it is declared."""
    hits = 0

    for match in re.finditer(r"(?<![.\w])" + re.escape(name) + r"\s*\(", text):
        line = text.count("\n", 0, match.start())
        if line == declared_at:
            continue
        hits += 1

    return hits


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

        # The whole call, however many lines it runs to.
        at = text.index("Ui.Configure", sum(len(l) + 1 for l in lines[:i]))
        whole = call_text(text, at)

        if not MULTI.search(whole) and whole.count("new Ui.Page(") <= 1:
            continue

        start, name = enclosing(i, starts)

        if name is None:
            continue

        if presses(text, name, start) <= 1:
            continue

        found.append((i + 1, name, line.strip()))

    return found


def main():
    bad = []

    for path in sorted(UI.rglob("*.cs")):
        for line, name, text in check(path):
            bad.append((path.relative_to(ROOT), line, name, text))

    if not bad:
        print("every symbol that opens a window opens a window of its own")
        return 0

    print("Several presses opening one window:\n")

    for path, line, name, text in bad:
        print("  {}:{}  in {}()".format(path, line, name))
        print("    {}".format(text))

    print("\nEach press must hand Ui.Configure the one section it names, so the window that opens")
    print("is titled with it and shows it. A window that is the same whichever symbol opened it")
    print("makes the row read as one button drawn several times.")

    return 1


if __name__ == "__main__":
    sys.exit(main())
