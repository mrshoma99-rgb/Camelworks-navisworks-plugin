#!/usr/bin/env python3
"""Fail when a public type in CamelWorks.Core takes a name WPF or Navisworks already uses.

Core compiles on Linux against neither of them, so the collision is invisible here and shows up
as CS0104 in four Windows jobs at once — CamelWorks.UI imports System.Windows and
Autodesk.Navisworks.Api, and a bare name that means two things is an error at the point of use,
not at the point of declaration.

The deny-list is advisory; see checks/reserved-type-names.txt for what is in it and why. The
Windows jobs remain the authority.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CORE = ROOT / "CamelWorks.Core"
LIST = ROOT / "checks" / "reserved-type-names.txt"

DECLARATION = re.compile(
    r"^\s*public\s+(?:(?:sealed|static|abstract|partial|readonly|ref|unsafe)\s+)*"
    r"(class|struct|interface|enum|record)\s+([A-Za-z_][A-Za-z0-9_]*)"
)


def reserved():
    names = set()
    for line in LIST.read_text(encoding="utf-8").splitlines():
        line = line.strip()
        if line and not line.startswith("#"):
            names.add(line)
    return names


def main():
    if not CORE.is_dir():
        print(f"no such directory: {CORE}", file=sys.stderr)
        return 1

    banned = reserved()
    clashes = []
    declared = 0

    for path in sorted(CORE.rglob("*.cs")):
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            found = DECLARATION.match(line)
            if not found:
                continue

            declared += 1
            name = found.group(2)

            if name in banned:
                rel = path.relative_to(ROOT)
                clashes.append(f"{rel}:{number}: public {found.group(1)} {name} — "
                               f"{name} is already a WPF or Navisworks type, so CamelWorks.UI "
                               f"cannot name it without qualifying it")

    if clashes:
        print("core type names collide:", file=sys.stderr)
        for clash in clashes:
            print("  " + clash, file=sys.stderr)
        return 1

    print(f"core names clear: {declared} public types, none shadowing "
          f"{len(banned)} reserved names")
    return 0


if __name__ == "__main__":
    sys.exit(main())
