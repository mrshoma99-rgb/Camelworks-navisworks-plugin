#!/usr/bin/env python3
"""Fail when a class implementing a Core seam interface is missing one of its members.

Adding a member to an interface breaks every implementer, and the implementers live in three
assemblies two of which cannot be compiled on Linux. Without this the break is invisible here and
arrives as five simultaneous CS0535 failures in CI -- which is exactly what happened when
IModelSource grew its four intake fields.

Name-level only: it checks that every member name the interface declares appears as a member of
each implementer. Signatures, accessibility and return types remain the compiler's job, and the
Windows jobs remain the authority.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SEAMS = ROOT / "CamelWorks.Core" / "Abstractions"

# An interface member: a type, a name, then '{' for a property or '(' for a method.
MEMBER = re.compile(
    r"^\s*(?!//)"
    r"[A-Za-z_][\w<>,\[\]\.\?\s]*?"
    r"\b([A-Za-z_]\w*)\s*(\{|\()"
)

# A class member. Needs its own pattern because an implementation is very often expression-bodied
# -- "public bool IsEmpty => _items.Count == 0;" has neither a brace nor a bracket after the name,
# and a checker that missed those would report every arrow property in the codebase as absent. End
# of line counts too, for the properties whose opening brace is on the next line.
IMPLEMENTED = re.compile(
    r"^\s*(?:public|internal|protected|private)\s+"
    r"[\w<>,\[\]\.\?\s]*?"
    r"\b([A-Za-z_]\w*)\s*(\{|\(|=>|;|=|$)"
)

INTERFACE = re.compile(r"^\s*public\s+interface\s+(I[A-Za-z_]\w*)")
IMPLEMENTS = re.compile(r"^\s*(?:public|internal|private|protected)?\s*(?:sealed\s+)?class\s+(\w+)\s*:\s*(.+)$")


def interfaces():
    """Every seam interface, as {name: {member names}}."""
    found = {}

    for path in sorted(SEAMS.glob("*.cs")):
        lines = path.read_text(encoding="utf-8").splitlines()
        name = None
        depth = 0
        members = set()

        for line in lines:
            if name is None:
                match = INTERFACE.match(line)
                if match:
                    name = match.group(1)
                    depth = 0
                    members = set()
                continue

            depth += line.count("{") - line.count("}")

            if depth <= 0 and "}" in line:
                found[name] = members
                name = None
                continue

            member = MEMBER.match(line)
            if member and member.group(1) not in ("get", "set", "if", "return", "new"):
                members.add(member.group(1))

        if name is not None:
            found[name] = members

    return found


def bodies(path, wanted):
    """Every class in a file that names one of `wanted`, as (class name, interface, member names)."""
    text = path.read_text(encoding="utf-8")
    lines = text.splitlines()
    out = []

    for i, line in enumerate(lines):
        match = IMPLEMENTS.match(line)
        if not match:
            continue

        bases = [b.strip() for b in match.group(2).split(",")]
        named = [b for b in bases if b in wanted]
        if not named:
            continue

        # Walk the class body by brace depth, collecting member names.
        depth = 0
        started = False
        members = set()

        for body in lines[i:]:
            depth += body.count("{") - body.count("}")
            if body.count("{"):
                started = True

            member = IMPLEMENTED.match(body)
            if member and member.group(1) not in ("get", "set", "if", "return", "new", "class"):
                members.add(member.group(1))

            if started and depth <= 0:
                break

        for interface in named:
            out.append((match.group(1), interface, members))

    return out


def main():
    if not SEAMS.is_dir():
        print(f"no such directory: {SEAMS}", file=sys.stderr)
        return 1

    seams = interfaces()
    if not seams:
        print("no seam interfaces found", file=sys.stderr)
        return 1

    problems = []
    checked = 0

    for path in sorted(ROOT.rglob("*.cs")):
        if any(part in ("bin", "obj") for part in path.parts):
            continue

        for name, interface, members in bodies(path, seams):
            checked += 1
            missing = sorted(seams[interface] - members)

            if missing:
                problems.append(
                    f"{path.relative_to(ROOT)}: {name} implements {interface} "
                    f"but has no {', '.join(missing)}"
                )

    for problem in problems:
        print(problem, file=sys.stderr)

    if problems:
        print(f"\n{len(problems)} implementer(s) out of step with a seam interface.", file=sys.stderr)
        return 1

    print(f"seams intact: {checked} implementations of {len(seams)} interfaces, none missing a member")
    return 0


if __name__ == "__main__":
    sys.exit(main())
