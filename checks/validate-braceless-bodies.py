#!/usr/bin/env python3
"""Fail when a braceless if, else or loop has a declaration for a body.

    if (plan.Models.Count > 0)
        var table = Ui.Table(...);      // CS1023, and the lines under it are not in the if at all

C# will not accept it - "embedded statement cannot be a declaration" - and the mistake is easy
to make while editing rather than writing: a one-statement body gets a second statement and a
local, the braces that were never needed are still not there, and the second statement silently
leaves the branch. It cost a CI cycle on a machine with no compiler, which is the only kind of
cycle this repository has.

Whole-line comments between the head and the body are skipped, because a comment explaining the
branch belongs above the thing it explains.

    python3 checks/validate-braceless-bodies.py
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent

# A head that ends without an opening brace: if (...), else if (...), foreach (...), else.
HEAD = re.compile(r"^\s*(?:\}\s*)?(?:else\s+if|if|for|foreach|while|using|lock|fixed)\b.*\)\s*$"
                  r"|^\s*(?:\}\s*)?else\s*$")

# A local declaration: var x = , Thing x = , IReadOnlyList<T>? x = .
DECL = re.compile(r"^\s*(?:var|[A-Z]\w*(?:<[^;]*>)?\??(?:\[\])?)\s+\w+\s*=")


def main():
    problems = []

    for folder in ("CamelWorks.UI", "CamelWorks.Nav", "CamelWorks.Nav.Clash", "CamelWorks.Core",
                   "CamelWorks.Studio"):
        if not (ROOT / folder).is_dir():
            continue

        for path in sorted((ROOT / folder).rglob("*.cs")):
            lines = path.read_text(encoding="utf-8").splitlines()

            for i, line in enumerate(lines):
                if not HEAD.match(line):
                    continue

                j = i + 1
                while j < len(lines) and (not lines[j].strip() or lines[j].lstrip().startswith("//")):
                    j += 1

                if j >= len(lines) or lines[j].lstrip().startswith("{"):
                    continue

                if DECL.match(lines[j]):
                    problems.append(f"{path.relative_to(ROOT)}:{j + 1}: "
                                    f"a declaration is the body of the braceless "
                                    f"\"{line.strip()[:40]}\" above it - add the braces")

    for problem in problems:
        print("FAIL: " + problem, file=sys.stderr)

    if problems:
        print(f"\n{len(problems)} braceless bod(y/ies) that will not compile.", file=sys.stderr)
        return 1

    print("braceless bodies: none of them a declaration")
    return 0


if __name__ == "__main__":
    sys.exit(main())
