#!/usr/bin/env python3
"""Fail when an underscore identifier is used in a file that never declares it.

There is no C# compiler on the machine these checks run on, so a mistyped field name is invisible
here and costs a full CI round trip to find. This catches the one way it actually happens: a
search-and-replace whose search string is a suffix of a longer identifier.

    _status.Children.Add(Ui.Diff(diff));      replacing "status.Children.Add(Ui.Diff(diff));"
    _into.Children.Add(Ui.Diff(diff));        leaves this

Both halves of that are valid C# in isolation, both read fine in a diff, and the file still balances.
Only the compiler knows, and the compiler is twenty minutes away.

The rule is deliberately narrow. It only looks at names beginning with an underscore, because those
are fields by this codebase's convention and are declared in the same file they are used in. It says
nothing about locals, parameters or anything inherited - a real name resolver is a compiler, and
this is a lint that catches one specific and repeated mistake.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TREES = ("CamelWorks.UI", "CamelWorks.Studio")

USED = re.compile(r"\b(_\w+)\b")

# A discard: `(_, __) => ...`. Never a declaration and never a mistake.
DISCARD = re.compile(r"^_+$")

# A declaration keyword, then the name, then something that ends it. DOTALL, so a generic or tuple
# type spread over two lines does not hide the name it introduces.
# Built by concatenation rather than str.format: the pattern contains a {} character class of its
# own, and format would read it as a field.
DECLARES_BEFORE = (r"(?:private|protected|internal|public|readonly|const|static|var|out|ref|foreach\s*\()"
                   r"[^;{}]*?\b")
DECLARES_AFTER = r"\b\s*[;=,)\]]"


def main():
    problems = 0
    checked = 0

    for tree in TREES:
        for path in sorted((ROOT / tree).rglob("*.cs")):
            source = path.read_text(encoding="utf-8")
            checked += 1

            for name in sorted(set(USED.findall(source))):
                if DISCARD.match(name):
                    continue

                if re.search(DECLARES_BEFORE + re.escape(name) + DECLARES_AFTER, source, re.S):
                    continue

                line = next((n for n, text in enumerate(source.splitlines(), 1)
                             if re.search(r"\b" + re.escape(name) + r"\b", text)), 0)

                print(f"FAIL: {path.relative_to(ROOT)}:{line}: {name} is used and never declared here")
                problems = 1

    if problems:
        print()
        print("Usually a replacement that matched inside a longer name. Check the edit that "
              "introduced it rather than declaring the field.")
        return 1

    print(f"fields resolve: {checked} files, every underscore name declared where it is used")
    return 0


if __name__ == "__main__":
    sys.exit(main())
