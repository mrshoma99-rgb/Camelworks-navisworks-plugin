#!/usr/bin/env python3
"""Fail on the XML-doc mistakes only the compiler can see.

CamelWorks.Core is built with GenerateDocumentationFile and TreatWarningsAsErrors, so a doc
comment that names a parameter which does not exist is not a nit — it fails four Windows jobs and
the Linux job at once, ten minutes after the push. There is no .NET SDK on the machine that writes
these files, so the mistake is otherwise unfindable locally.

Two rules, matching two real compiler errors:

  CS1734  <param> or <paramref> names something that is not a parameter of the member it is on
          (including any of them at all on a member that takes no parameters, such as a const).

  CS1573  A member documents SOME of its parameters and not others. Documenting none is fine —
          that is CS1591's business and the compiler is happy with an undocumented parameter list
          as long as it is consistently undocumented.

Both are checked only where the compiler checks them: members that carry a doc comment at all.
The Windows and Linux jobs remain the authority; this only moves the discovery ten minutes earlier
and onto the machine that made the mistake.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PROJECTS = ["CamelWorks.Core", "CamelWorks.Nav", "CamelWorks.Nav.Clash", "CamelWorks.UI"]

DOC = re.compile(r"^\s*///")
PARAM_TAG = re.compile(r'<param\s+name\s*=\s*"([^"]*)"')
PARAMREF_TAG = re.compile(r'<paramref\s+name\s*=\s*"([^"]*)"')

# A declaration worth checking: it has an access modifier and is not a using/comment.
DECLARATION = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)?(?:public|internal|protected|private)\b")

# Words that can appear where a parameter name goes but are not one. "scoped" is deliberately NOT
# here: it is a C# 11 modifier this codebase does not use, and treating it as one turns a parameter
# genuinely called "scoped" into its own type name.
MODIFIERS = {"ref", "out", "in", "params", "this", "readonly"}


def trim(signature):
    """The declaration up to its body, so a call in an expression body is not read as a parameter list."""
    depth = 0

    for i, c in enumerate(signature):
        if c in "([<{":
            depth += 1
        elif c in ")]>}":
            depth -= 1
        elif depth == 0:
            if c == ";":
                return signature[:i]
            if signature.startswith("=>", i):
                return signature[:i]

    return signature


def groups(text, opener, closer):
    """Every balanced top-level opener/closer pair, as the text between them."""
    found = []
    depth = 0
    start = -1

    for i, c in enumerate(text):
        if c == opener:
            if depth == 0:
                start = i
            depth += 1
        elif c == closer and depth > 0:
            depth -= 1
            if depth == 0:
                found.append(text[start + 1:i])

    return found


def names(inner):
    """The parameter names out of one parameter list's text."""
    if not inner.strip():
        return []

    parts = []
    depth = 0
    current = ""

    for c in inner:
        if c in "<([{":
            depth += 1
        elif c in ">)]}":
            depth -= 1

        if c == "," and depth == 0:
            parts.append(current)
            current = ""
        else:
            current += c

    parts.append(current)

    found = []

    for part in parts:
        # Strip a default value, then take the last identifier before it.
        head = part.split("=")[0].strip()
        words = [w for w in re.findall(r"[A-Za-z_]\w*", head) if w not in MODIFIERS]
        if words:
            found.append(words[-1])

    return found


def parameters(signature):
    """
    The parameter names of a declaration, or None when it declares no parameter list.

    The LAST top-level paren group, not the first: a method returning a named tuple —
    `public static (string? Path, string Where) Locate(string? documentPath)` — has two, and the
    first one is the return type. Reading that one reports every real parameter as undocumented
    and every doc tag as naming nothing, which is a checker nobody would keep.
    """
    body = trim(signature)

    parens = groups(body, "(", ")")
    if parens:
        return names(parens[-1])

    # An indexer has no parentheses at all, and its arguments are documented with <param>.
    if "this[" in body:
        brackets = groups(body, "[", "]")
        if brackets:
            return names(brackets[-1])

    return None


def gather(path):
    """(line number, doc text, declaration) for every documented declaration in the file."""
    lines = path.read_text(encoding="utf-8").splitlines()
    found = []
    at = 0

    while at < len(lines):
        if not DOC.match(lines[at]):
            at += 1
            continue

        first = at
        doc = []

        while at < len(lines) and DOC.match(lines[at]):
            doc.append(lines[at])
            at += 1

        # Skip attributes between the comment and the declaration.
        while at < len(lines) and lines[at].strip().startswith("["):
            at += 1

        if at >= len(lines) or not DECLARATION.match(lines[at]):
            continue

        # Accumulate until the parameter list closes, or the declaration plainly has none.
        signature = lines[at]
        depth = signature.count("(") - signature.count(")")
        cursor = at

        while depth > 0 and cursor + 1 < len(lines):
            cursor += 1
            signature += " " + lines[cursor].strip()
            depth += lines[cursor].count("(") - lines[cursor].count(")")

        found.append((first + 1, "\n".join(doc), signature))
        at = cursor + 1

    return found


def main():
    problems = []
    checked = 0

    for project in PROJECTS:
        folder = ROOT / project

        if not folder.is_dir():
            continue

        for path in sorted(folder.rglob("*.cs")):
            if any(part in ("bin", "obj") for part in path.parts):
                continue

            for number, doc, signature in gather(path):
                checked += 1

                documented = PARAM_TAG.findall(doc)
                referenced = PARAMREF_TAG.findall(doc)

                if not documented and not referenced:
                    continue

                actual = parameters(signature)
                rel = path.relative_to(ROOT)

                if actual is None:
                    problems.append(
                        f"{rel}:{number}: this member takes no parameters, so "
                        f"{', '.join(sorted(set(documented + referenced)))} cannot be one (CS1734)")
                    continue

                for name in sorted(set(documented + referenced)):
                    if name not in actual:
                        problems.append(
                            f"{rel}:{number}: no parameter called \"{name}\" — it takes "
                            f"({', '.join(actual) or 'nothing'}) (CS1734)")

                # CS1573 only fires once at least one parameter IS documented.
                if documented:
                    for name in actual:
                        if name not in documented:
                            problems.append(
                                f"{rel}:{number}: parameter \"{name}\" has no <param> tag while "
                                f"others do (CS1573)")

    if problems:
        print("XML documentation does not match the code:", file=sys.stderr)
        for problem in problems:
            print("  " + problem, file=sys.stderr)
        return 1

    print(f"docs consistent: {checked} documented declarations, every param tag names a real parameter")
    return 0


if __name__ == "__main__":
    sys.exit(main())
