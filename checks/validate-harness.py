#!/usr/bin/env python3
"""Fail when the design harness draws a button no panel has.

CamelWorks.Studio/Panels.cs reconstructs the shipping panels so they can be rendered, looked at and
reviewed on a machine with no Navisworks on it. That only works while the reconstruction is true:
a harness that draws a button the product does not have is worse than no harness, because it is the
artefact reviewers are handed.

Captions are the cheapest thing to hold to account and the most telling - a renamed button is
exactly the drift nobody notices. A caption the product builds at run time ("Write 11,208
elements") cannot match a literal, so a harness caption passes when its first few characters open
a string literal somewhere in CamelWorks.UI.

Sheets.cs is not checked. Its pages are the vocabulary - a specimen sheet of every control - and
are deliberately not reconstructions of anything.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
HARNESS = ROOT / "CamelWorks.Studio" / "Panels.cs"
PANELS = ROOT / "CamelWorks.UI"

CALL = re.compile(r'Ui\.(?:Primary|Button|Danger|Quiet|Link|Runner)\(\s*"((?:[^"\\]|\\.)+)"')

#: The shortest opening that counts as a match. A caption the product builds at run time - "Write "
#: plus a count - is caught by the regex as the literal "Write ", and that literal is a prefix of
#: the harness's "Write 11,208 elements". Five characters is enough that "Read it" and "Read a BCF
#: file" still diverge. An exact match always counts, however short: "Add" is a whole caption.
STEM = 4


def main():
    if not HARNESS.is_file() or not PANELS.is_dir():
        print("cannot find the harness or the panels", file=sys.stderr)
        return 1

    shipped = set()

    for path in sorted(PANELS.rglob("*.cs")):
        shipped.update(CALL.findall(path.read_text(encoding="utf-8")))

    drifted = []
    checked = 0

    for number, line in enumerate(HARNESS.read_text(encoding="utf-8").splitlines(), 1):
        for caption in CALL.findall(line):
            checked += 1

            if caption in shipped:
                continue

            if any(len(one) >= STEM and caption.startswith(one) for one in shipped):
                continue

            drifted.append(f"CamelWorks.Studio/Panels.cs:{number}: the harness draws "
                           f'"{caption}" and no panel does')

    for line in drifted:
        print(line, file=sys.stderr)

    if drifted:
        print("", file=sys.stderr)
        print("Either the panel was renamed and the harness was not, or the harness invented a "
              "button the product should have and does not. Both are worth knowing; neither is "
              "something to leave in the pictures people review.", file=sys.stderr)
        return 1

    print(f"harness true: {checked} captions, every one of them a caption a panel has")
    return 0


if __name__ == "__main__":
    sys.exit(main())
