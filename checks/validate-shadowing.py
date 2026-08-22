#!/usr/bin/env python3
"""Fail when a host-assembly member name shadows a Core type that file imports.

C# name lookup finds a member before a type, so a private method called Measure inside a class
that also uses the Measure type makes the type unreachable in that whole class -- and the compile
error lands on the line that used the type, a long way from the declaration that broke it.

That is exactly the shape of failure this environment cannot see: Core builds on Linux, the host
assemblies do not, and the error only appears on four Windows jobs at once.

Only members declared directly in a type are checked, against the public type names Core exports
from the namespaces the file has a using for. The Windows jobs remain the authority.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CORE = ROOT / "CamelWorks.Core"
HOSTS = ["CamelWorks.Nav", "CamelWorks.Nav.Clash", "CamelWorks.UI"]
ALLOWED = ROOT / "checks" / "allowed-shadowing.txt"

NAMESPACE = re.compile(r"^\s*namespace\s+([\w\.]+)")
TYPE = re.compile(
    r"^\s*public\s+(?:(?:sealed|static|abstract|partial|readonly|ref|unsafe)\s+)*"
    r"(?:class|struct|interface|enum|record)\s+([A-Za-z_]\w*)"
)
USING = re.compile(r"^\s*using\s+([\w\.]+)\s*;")

# A member declaration: an access modifier, then a type, then the name, then ( or { or => or ;.
MEMBER = re.compile(
    r"^\s*(?:public|internal|protected|private)\s+"
    r"(?:(?:static|readonly|const|virtual|override|abstract|sealed|async|new|partial|extern|unsafe)\s+)*"
    r"[\w<>,\[\]\.\?\s]*?"
    r"\b([A-Za-z_]\w*)\s*(?:\(|\{|=>|;|=)"
)


def core_types():
    """Public Core type names, by the namespace that declares them."""
    types = {}

    for path in sorted(CORE.rglob("*.cs")):
        if any(part in ("bin", "obj") for part in path.parts):
            continue

        namespace = None

        for line in path.read_text(encoding="utf-8").splitlines():
            found = NAMESPACE.match(line)
            if found:
                namespace = found.group(1)
                continue

            declared = TYPE.match(line)
            if declared and namespace:
                types.setdefault(namespace, set()).add(declared.group(1))

    return types


def allowed():
    """The ones that are fine as they are; see the file for why."""
    if not ALLOWED.is_file():
        return set()

    return {
        line.strip()
        for line in ALLOWED.read_text(encoding="utf-8").splitlines()
        if line.strip() and not line.startswith("#")
    }


def main():
    types = core_types()
    excused = allowed()

    if not types:
        print("no Core types found", file=sys.stderr)
        return 1

    problems = []
    checked = 0

    for host in HOSTS:
        root = ROOT / host
        if not root.is_dir():
            continue

        for path in sorted(root.rglob("*.cs")):
            if any(part in ("bin", "obj") for part in path.parts):
                continue

            lines = path.read_text(encoding="utf-8").splitlines()

            visible = set()
            for line in lines:
                using = USING.match(line)
                if using and using.group(1) in types:
                    visible |= types[using.group(1)]

            if not visible:
                continue

            for number, line in enumerate(lines, 1):
                member = MEMBER.match(line)
                if not member:
                    continue

                checked += 1
                name = member.group(1)

                if name in visible and f"{path.relative_to(ROOT)}:{name}" not in excused:
                    problems.append(
                        f"{path.relative_to(ROOT)}:{number}: member '{name}' shadows the Core type "
                        f"of the same name, which this file imports"
                    )

    for problem in problems:
        print(problem, file=sys.stderr)

    if problems:
        print(f"\n{len(problems)} member(s) shadow a Core type.", file=sys.stderr)
        return 1

    print(f"no shadowing: {checked} host members checked against Core's type names")
    return 0


if __name__ == "__main__":
    sys.exit(main())
