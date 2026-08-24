#!/usr/bin/env python3
"""Fail when a public member of a public type carries no doc comment at all (CS1591).

The projects that ship are built with GenerateDocumentationFile and TreatWarningsAsErrors, so an
undocumented public member is not a nit - it fails every job at once, ten minutes after the push.
There is no .NET SDK on the machine that writes these files.

It cost exactly that. A struct's constructor kept its `/// <summary>Create a path.</summary>` right
up until two backing fields were inserted between them, and the comment ended up documenting a
private field instead. Nothing about that reads wrong; the compiler had to say so.

Deliberately narrow, so it has no false positives to argue with:

  * Only the projects that generate a documentation file.
  * Only members declared directly inside a top-level type - namespace, then type, then member.
    A nested private class's members are not part of the public surface and the compiler does not
    ask about them, and telling them apart needs more than brace counting.
  * Only members that say `public` or `protected`. `internal` and `private` are nobody's business.
  * An attribute line between the comment and the declaration is fine, which is what the compiler
    thinks too.

Enum members are not checked here. They need comments as much as anything else does, but they
carry no access modifier, so telling one from a line of an initialiser is not brace counting -
and every enum in this repository already documents its members.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PROJECTS = ["CamelWorks.Core", "CamelWorks.Nav", "CamelWorks.Nav.Clash"]

DOC = re.compile(r"^\s*///")
ATTRIBUTE = re.compile(r"^\s*\[")
WANTED = re.compile(r"^\s*(?:public|protected)\s")

# Not a member: an accessor body, or a `public` that is part of something already documented above.
SKIP = re.compile(r"^\s*(?:public|protected)\s+(?:override\s+)?(?:get|set)\b")


def documents(project):
    """Whether the project asks the compiler for a documentation file, which is what turns on CS1591."""
    csproj = ROOT / project / (project + ".csproj")

    return csproj.is_file() and "GenerateDocumentationFile" in csproj.read_text(encoding="utf-8")


def main():
    problems = []
    checked = 0

    for project in PROJECTS:
        if not documents(project):
            continue

        for path in sorted((ROOT / project).rglob("*.cs")):
            if any(part in ("bin", "obj") for part in path.parts):
                continue

            lines = path.read_text(encoding="utf-8").splitlines()
            depth = 0

            for number, line in enumerate(lines, 1):
                # Depth BEFORE this line's own braces: a member sits at namespace + type = 2.
                here = depth

                stripped = re.sub(r'"(?:[^"\\]|\\.)*"', '""', line)
                stripped = re.sub(r"'(?:[^'\\]|\\.)'", "' '", stripped)

                depth += stripped.count("{") - stripped.count("}")

                if here != 2 or not WANTED.match(line) or SKIP.match(line):
                    continue

                checked += 1

                # Walk back over attributes and blank lines to the comment that should be there.
                at = number - 2

                while at >= 0 and (not lines[at].strip() or ATTRIBUTE.match(lines[at])):
                    at -= 1

                if at >= 0 and DOC.match(lines[at]):
                    continue

                problems.append(f"{path.relative_to(ROOT)}:{number}: "
                                f"{line.strip()[:80]} has no doc comment (CS1591)")

    if problems:
        print("Public members with nothing said about them:", file=sys.stderr)

        for problem in problems:
            print("  " + problem, file=sys.stderr)

        return 1

    print(f"docs complete: {checked} public members, every one of them documented")
    return 0


if __name__ == "__main__":
    sys.exit(main())
