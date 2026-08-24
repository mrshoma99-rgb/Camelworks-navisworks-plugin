#!/usr/bin/env python3
"""Fail when a file uses a type this repository declares without importing where it lives.

There is no C# compiler on the machine these checks run on, so a missing `using` is invisible here
and costs a CI round trip. It is also the most mechanical mistake there is: write a test against a
type, forget that its namespace is not the one the rest of the file uses, push.

The rule only knows about types THIS repository declares. That is what keeps it honest — it has a
real index to check against rather than a guess, and it says nothing at all about the BCL, WPF or
the Navisworks API, where a wrong answer would be noise.

Two things stop it crying wolf, both learned from a first draft that produced fifty-three false
positives on a clean tree:

  * Only type POSITIONS count - `new X`, `X.Member`, `<X>`, `(X)`, `is X`, `as X`, `typeof(X)`.
    A parameter, a property or a string that happens to share a name with a type is not a use of it.
  * A name the file itself declares as a member wins. Ui.Finding is a method, and it has nothing to
    do with the Finding type over in Core.Findings.

Doc comments and string literals are stripped before any of it, because prose mentioning a type is
not code using one.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

DECLARES = re.compile(r'^\s*(?:public|internal)\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+)*'
                      r'(?:class|struct|enum|interface|record)\s+(\w+)', re.M)

NAMESPACE = re.compile(r'^\s*namespace\s+([\w.]+)', re.M)
USING = re.compile(r'^\s*using\s+(?:\w+\s*=\s*)?([\w.]+)\s*;', re.M)
MEMBER = re.compile(r'(?:public|private|internal|protected)[\w\s<>,?\[\]]*?\b(\w+)\s*(?:\(|=>|\{\s*get)')

# Somewhere a name can only be a type.
POSITIONS = [
    re.compile(r'\bnew\s+([A-Z]\w+)'),
    re.compile(r'<\s*([A-Z]\w+)[\s,>]'),
    re.compile(r'\(\s*([A-Z]\w+)\s*\)'),
    re.compile(r'\bis\s+([A-Z]\w+)\b'),
    re.compile(r'\bas\s+([A-Z]\w+)\b'),
    re.compile(r'\btypeof\(\s*([A-Z]\w+)\s*\)'),
    re.compile(r'\b([A-Z]\w+)\.\w+'),
]

# Already written out in full, so no using is needed for it.
QUALIFIED = re.compile(r'\b[\w.]+\.([A-Z]\w+)\b')


def sources():
    for path in sorted(ROOT.rglob("*.cs")):
        if "/obj/" in str(path) or "/bin/" in str(path):
            continue
        yield path


def code(text):
    """The file with its usings, doc comments and string literals taken out."""
    text = re.sub(r'^\s*using[^\n]*\n', '', text, flags=re.M)
    text = re.sub(r'///[^\n]*', '', text)
    return re.sub(r'"(?:[^"\\]|\\.)*"', '""', text)


def main():
    home = {}

    for path in sources():
        text = path.read_text(encoding="utf-8")
        namespace = NAMESPACE.search(text)
        if not namespace:
            continue

        for name in DECLARES.findall(text):
            home.setdefault(name, set()).add(namespace.group(1))

    problems = 0
    checked = 0

    for path in sources():
        text = path.read_text(encoding="utf-8")
        namespace = NAMESPACE.search(text)
        if not namespace:
            continue

        checked += 1
        mine = namespace.group(1)

        # A file sees what it imports, its own namespace, and every ancestor of it.
        visible = set(USING.findall(text)) | {mine}
        visible |= {mine.rsplit(".", n)[0] for n in range(1, mine.count(".") + 1)}

        body = code(text)
        declared_here = set(MEMBER.findall(body))

        # Names in type position, gathered once per file rather than by trying every known type
        # against every file - which is 219 x 506 regex searches and takes minutes.
        used = set()
        for pattern in POSITIONS:
            used.update(pattern.findall(body))

        qualified = set(QUALIFIED.findall(body))

        for name in sorted(used):
            namespaces = home.get(name)

            if namespaces is None or namespaces & visible:
                continue

            if name in declared_here or name in qualified:
                continue

            line = next((n for n, text in enumerate(body.splitlines(), 1)
                         if re.search(r'\b' + re.escape(name) + r'\b', text)), 0)

            print(f"FAIL: {path.relative_to(ROOT)}:{line}: {name} is used but "
                  f"{' or '.join(sorted(namespaces))} is not imported")
            problems = 1

    if problems:
        return 1

    print(f"usings resolve: {checked} files against {len(home)} declared types")
    return 0


if __name__ == "__main__":
    sys.exit(main())
