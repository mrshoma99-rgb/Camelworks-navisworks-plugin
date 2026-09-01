#!/usr/bin/env python3
"""Fail when the design harness draws a button no panel has.

CamelWorks.Studio reconstructs the shipping panels so they can be rendered, looked at and reviewed
on a machine with no Navisworks on it. That only works while the reconstruction is true: a harness
that draws a button the product does not have is worse than no harness, because it is the artefact
reviewers are handed.

Captions are the cheapest thing to hold to account and the most telling - a renamed button is
exactly the drift nobody notices. A caption the product builds at run time is compared by its
literal parts: Ui.Runner("Found " + Count(n) + " to look at") contributes the
two fragments "Stamp " and " changes into the properties", and a harness caption passes when it
carries every fragment of some panel's caption, in order. Matching on the opening fragment alone
was not enough - the product has an "Apply " + count caption, so every invented caption beginning
"Apply " passed.

A builder marked "// harness: vocabulary" is skipped. Those pages are a specimen sheet of every
control rather than a reconstruction of a panel, and their captions are invented on purpose to put
the weights side by side.

A single line marked "// harness: data" is skipped too, for the narrower case: a row in a list the
product draws one per item, where the caption is the item's own name. There is no literal in the
product to match against, so a specimen row can only be invented, and the alternative is silencing
the whole builder over one line.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
HARNESS = [ROOT / "CamelWorks.Studio" / "Panels.cs", ROOT / "CamelWorks.Studio" / "Sheets.cs",
           ROOT / "CamelWorks.Studio" / "Dialogs.cs"]
PANELS = ROOT / "CamelWorks.UI"

CALL = re.compile(r'Ui\.(?:Primary|Button|Danger|Quiet|Link|Runner|RunParts|Commit)\(')
LITERAL = re.compile(r'"((?:[^"\\]|\\.)*)"')
BUILDER = re.compile(r'^\s*private static \S+ \w+\(')

MARKER = "// harness: vocabulary"
SPECIMEN = "// harness: data"


def captions(text):
    """Every caption in the file, as the ordered tuple of literal fragments that builds it."""
    found = []

    for call in CALL.finditer(text):
        depth = 1
        i = call.end()
        start = i
        quoted = False

        while i < len(text) and depth > 0:
            c = text[i]

            if quoted:
                if c == "\\":
                    i += 2
                    continue
                if c == '"':
                    quoted = False
            elif c == '"':
                quoted = True
            elif c in "([{":
                depth += 1
            elif c in ")]}":
                depth -= 1
                if depth == 0:
                    break
            elif c == "," and depth == 1:
                break

            i += 1

        expression = text[start:i]
        parts = tuple(LITERAL.findall(expression))

        if not parts:
            continue

        # Whether the product builds this caption at run time. A "+" outside the quotes is the
        # only way it can, and it is the difference between a caption that has to match exactly
        # and one whose literal parts have to be carried by whatever the harness wrote.
        joined = "+" in LITERAL.sub("", expression)

        found.append((text.count("\n", 0, call.start()) + 1, parts, joined))

    return found


def carries(harness, panel, joined):
    """Whether a harness caption matches a panel's.

    A panel caption the product writes whole has to match exactly - a button labelled "Save" is no
    evidence that a button labelled "Save the deck" exists. One the product builds at run time is
    matched by its literal fragments, in order, because the parts between them are values the
    harness has to invent.
    """
    whole = "".join(harness)

    if not joined:
        return whole == "".join(panel)

    at = 0

    for fragment in panel:
        found = whole.find(fragment, at)
        if found < 0:
            return False

        at = found + len(fragment)

    return True


def main():
    if not PANELS.is_dir() or not all(path.is_file() for path in HARNESS):
        print("cannot find the harness or the panels", file=sys.stderr)
        return 1

    shipped = []

    for path in sorted(PANELS.rglob("*.cs")):
        shipped.extend((parts, joined) for _, parts, joined in captions(path.read_text(encoding="utf-8")))

    drifted = []
    checked = 0
    skipped = 0

    for path in HARNESS:
        # The marker sits above the builder it applies to, so it is held until the declaration
        # arrives and then owns that builder until the next one starts.
        pending = False
        vocabulary = False

        text = path.read_text(encoding="utf-8")
        lines = text.splitlines()
        quiet = set()

        for number, line in enumerate(lines, 1):
            if MARKER in line:
                pending = True
                continue

            if SPECIMEN in line:
                quiet.add(number)

            if BUILDER.match(line):
                vocabulary = pending
                pending = False

            if vocabulary:
                quiet.add(number)

        for number, parts, _ in captions(text):
            if number in quiet:
                skipped += 1
                continue

            checked += 1

            if any(carries(parts, one, joined) for one, joined in shipped):
                continue

            drifted.append(f"{path.name}:{number}: the harness draws "
                           f'"{"".join(parts)}" and no panel does')

    for line in drifted:
        print(line, file=sys.stderr)

    if drifted:
        print("", file=sys.stderr)
        print("Either the panel was renamed and the harness was not, or the harness invented a "
              "button the product should have and does not. Both are worth knowing; neither is "
              "something to leave in the pictures people review.", file=sys.stderr)
        return 1

    print(f"harness true: {checked} captions held to a panel's, {skipped} on the vocabulary sheet")
    return 0


if __name__ == "__main__":
    sys.exit(main())
