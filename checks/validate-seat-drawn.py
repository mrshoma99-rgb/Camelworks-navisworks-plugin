#!/usr/bin/env python3
"""Fail when the render harness draws a shipping screen that takes a Session.

The harness runs on a machine with no Navisworks on it. A screen that takes a `Session` cannot be
built there at all — a Session's constructor takes an Autodesk document — so such a screen can only
ever be looked at on a licensed seat and described in words, which is how every layout fault on the
matrix and the clearance tab was found before `ISeat` existed.

So: every shipping surface the harness builds from the empty seat must declare `ISeat` as its first
parameter. A change back to `Session` fails here rather than at the next Windows render, and the
harness page silently disappearing is not one of the outcomes.

Three things this check has to survive, each of which defeated an earlier version of it:

- **The seat hoisted into a local.** `var seat = Seated(); Something.Draw(seat, ...)` is the same
  call and the first version could not see it. Locals assigned from `Seated()` are tracked and a
  call whose first argument is one of them counts.
- **A member declared in more than one place.** Taking the first `rglob` match makes the answer
  depend on the order the file system lists files, which is the defect class this whole check
  family exists to catch. Every declaration is collected and a disagreement is a failure.
- **A page quietly disappearing.** The count was printed and nothing compared it to anything, so
  deleting a page passed. `AT_LEAST` below is asserted.

    python3 checks/validate-seat-drawn.py
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
HARNESS = ROOT / "CamelWorks.Studio"
PANELS = ROOT / "CamelWorks.UI"

# How many shipping surfaces the harness draws from the empty seat, and how many pages it declares
# in all. Raise them when a page is added; a drop is a page that stopped being drawn, which is what
# these numbers are here to catch.
AT_LEAST = 19
PAGES_AT_LEAST = 40

# A local bound to the empty seat: `var seat = Seated();`
BOUND = re.compile(r"\bvar\s+(\w+)\s*=\s*Seated\(\)\s*;")

# A call the harness makes with the empty seat first: Type.Member(Seated() ... or Type.Member(seat ...
def calls(text):
    """Every (owner, member) the harness draws from the seat, with the seat as the first argument."""
    seats = set(BOUND.findall(text)) | {"Seated()"}
    made = []

    for owner, member, first in re.findall(
            r"\b([A-Z]\w*)\.([A-Z]\w*)\(\s*(\w+(?:\(\))?)", text):
        if first in seats:
            made.append((owner, member))

    return made


def declared(owner, member):
    """
    The first parameter type of that member.

    Returns (type, problem). The problem is set when the member is declared nowhere, or when two
    files declare it differently and the answer would otherwise depend on enumeration order.
    """
    wanted = re.compile(
        r"\b(?:public|internal)\s+static\s+[\w<>,\.\?\[\]]+\s+"
        + re.escape(member)
        + r"\s*\(\s*([\w<>\.\?]+)\s+\w+")

    found = {}

    for source in sorted(PANELS.rglob("*.cs")):
        if "/obj/" in source.as_posix() or "/bin/" in source.as_posix():
            continue

        text = source.read_text(encoding="utf-8")

        if re.search(r"\bclass\s+" + re.escape(owner) + r"\b", text) is None:
            continue

        for first in wanted.findall(text):
            found.setdefault(first, []).append(source.relative_to(ROOT).as_posix())

    if not found:
        return None, f"{owner}.{member} is drawn from a seat and is declared nowhere in CamelWorks.UI"

    if len(found) > 1:
        where = "; ".join(f"{first} in {', '.join(files)}" for first, files in sorted(found.items()))

        return None, (f"{owner}.{member} is declared more than once and the declarations disagree "
                      f"about the first parameter ({where}), so which one this check reads would "
                      f"depend on the order the files were listed")

    return next(iter(found)), None


def main() -> int:
    bad = []
    seen = 0

    for source in sorted(HARNESS.glob("*.cs")):
        text = source.read_text(encoding="utf-8")

        for owner, member in calls(text):
            seen += 1
            first, problem = declared(owner, member)

            if problem:
                bad.append(f"{source.name}: {problem}")
            elif first != "ISeat":
                bad.append(f"{source.name}: {owner}.{member} is drawn from a seat but takes "
                           f"{first}, so it cannot be built without Navisworks")

    pages = sum(len(re.findall(r'Page\(\s*"[a-z0-9-]+"', source.read_text(encoding="utf-8")))
                for source in sorted(HARNESS.glob("*.cs")))

    if pages < PAGES_AT_LEAST:
        bad.append(f"the harness declares {pages} pages and used to declare {PAGES_AT_LEAST}. "
                   f"Lower PAGES_AT_LEAST in this file only when a page was removed on purpose")

    if seen < AT_LEAST:
        bad.append(f"the harness draws {seen} shipping surfaces from a seat and used to draw "
                   f"{AT_LEAST}. A page that stopped being drawn is a screen nobody can look at; "
                   f"lower AT_LEAST in this file only when the page was removed on purpose")

    if bad:
        for line in bad:
            print(line, file=sys.stderr)

        print("\nA screen the harness draws must take ISeat. See the type remarks on ISeat.",
              file=sys.stderr)

        return 1

    print(f"seats drawn: {seen} harness call(s) over {pages} pages, every screen over ISeat")

    return 0


if __name__ == "__main__":
    sys.exit(main())
