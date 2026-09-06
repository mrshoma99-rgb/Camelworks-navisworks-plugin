#!/usr/bin/env python3
"""Fail when the render harness draws a shipping screen that takes a Session.

The harness runs on a machine with no Navisworks on it. A screen that takes a `Session` cannot be
built there at all — a Session's constructor takes an Autodesk document — so such a screen can only
ever be looked at on a licensed seat and described in words, which is how every layout fault on the
matrix and the clearance tab was found before `ISeat` existed.

So: every shipping surface the harness builds from `Seated()` must declare `ISeat` as its first
parameter. A change back to `Session` fails here rather than at the next Windows render, and the
harness page silently disappearing is not one of the outcomes.

    python3 checks/validate-seat-drawn.py
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
HARNESS = ROOT / "CamelWorks.Studio"
PANELS = ROOT / "CamelWorks.UI"

# A call the harness makes with the empty seat: Type.Member(Seated() ...
DRAWN = re.compile(r"\b([A-Z]\w*)\.([A-Z]\w*)\(\s*Seated\(\)")


def declared(owner, member):
    """The first parameter type of that member, or None when it is not declared anywhere."""
    wanted = re.compile(
        r"\b(?:public|internal)\s+static\s+[\w<>,\.\?\[\]]+\s+"
        + re.escape(member)
        + r"\s*\(\s*([\w<>\.\?]+)\s+\w+")

    for source in PANELS.rglob("*.cs"):
        if "/obj/" in source.as_posix() or "/bin/" in source.as_posix():
            continue

        text = source.read_text(encoding="utf-8")

        if re.search(r"\bclass\s+" + re.escape(owner) + r"\b", text) is None:
            continue

        found = wanted.search(text)

        if found:
            return found.group(1)

    return None


def main() -> int:
    bad = []
    seen = 0

    for source in HARNESS.glob("*.cs"):
        for owner, member in DRAWN.findall(source.read_text(encoding="utf-8")):
            seen += 1
            first = declared(owner, member)

            if first is None:
                bad.append(f"{source.name}: {owner}.{member} is drawn from a seat and is "
                           f"declared nowhere in CamelWorks.UI")
            elif first != "ISeat":
                bad.append(f"{source.name}: {owner}.{member} is drawn from a seat but takes "
                           f"{first}, so it cannot be built without Navisworks")

    if bad:
        for line in bad:
            print(line, file=sys.stderr)

        print("\nA screen the harness draws must take ISeat. See the type remarks on ISeat.",
              file=sys.stderr)

        return 1

    print(f"seats drawn: {seen} harness call(s), every screen over ISeat")

    return 0


if __name__ == "__main__":
    sys.exit(main())
