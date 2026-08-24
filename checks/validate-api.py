#!/usr/bin/env python3
"""Fail when a call names a member a repository type does not have.

`Ui.Row(...)` and `IfcGuid.FromText(...)` both read perfectly and neither exists. There is no
compiler on this side of the fence, so both went out in a push and came back twenty minutes later
as a Windows build failure.

Both ends of these calls are in the repository, so this needs no type inference: a static class or
an enum is a closed set of names, and a call through one either names a member it has or does not.

Three things keep it honest, and each cost it a run of false positives:

  * a name declared in more than one namespace is dropped, because CamelWorks.Nav's `Host` and
    CamelWorks.UI's `Host` are not the same word;
  * a call is only checked when the calling project could actually see the declaration - the same
    project, or CamelWorks.Core, which everything references;
  * the return type may be a tuple, or a list of them, which the first version of the declaration
    pattern could not read at all.

Anything whose declaration this cannot see - a local, a field, a namespace, a type from the
framework - is left entirely alone. That includes a member reached through an instance, so
`session.Model.Title` is beyond it: knowing what `Model` is takes the type inference this
deliberately does without. It covers the half where the mistake is easiest to make, which is a
static helper somebody remembers having written.
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

STATIC_CLASS = re.compile(
    r"^\s*(?:public|internal)\s+(?:sealed\s+)?static\s+(?:partial\s+)?class\s+(\w+)", re.M)

ENUM = re.compile(r"^\s*(?:public|internal)\s+enum\s+(\w+)", re.M)

# Members a static class exposes: methods, properties, fields, consts, and nested types.
MEMBER = re.compile(
    r"^\s*(?:public|internal|protected)\s+"
    r"(?:(?:static|const|readonly|sealed|abstract|partial|new|extern|unsafe|override|virtual|async)\s+)*"
    r"(?:(?:class|struct|interface|enum|delegate|record)\s+)?"
    r"[\w.<>?\[\](),\s]*?\b(\w+)\s*(?:\(|\{|=>|=|;)", re.M)

ENUM_MEMBER = re.compile(r"^\s*(\w+)\s*(?:=[^,\n]*)?\s*,?\s*$", re.M)

STRINGS = re.compile(r'"(?:[^"\\\n]|\\.)*"|//[^\n]*')

NAMESPACE = re.compile(r"^\s*namespace\s+([\w.]+)", re.M)

ANY_TYPE = re.compile(
    r"^\s*(?:public|internal)\s+(?:(?:sealed|static|abstract|partial|readonly|ref|unsafe)\s+)*"
    r"(?:class|struct|interface|enum|record)\s+(\w+)", re.M)


def sources():
    for base, dirs, files in os.walk(ROOT):
        dirs[:] = [d for d in dirs if d not in (".git", "bin", "obj")]
        for name in files:
            if name.endswith(".cs"):
                yield os.path.join(base, name)


def body(text, start):
    """The braces-balanced body of the declaration beginning at `start`."""
    open_at = text.find("{", start)
    if open_at < 0:
        return ""

    depth = 0
    for i in range(open_at, len(text)):
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
            if depth == 0:
                return text[open_at + 1:i]
    return text[open_at + 1:]


def main():
    paths = sorted(sources())
    texts = {}

    for path in paths:
        with open(path, encoding="utf-8") as handle:
            texts[path] = handle.read()

    known = {}     # type name -> set of member names
    home = {}      # type name -> set of (project, namespace) it was declared in
    elsewhere = {} # every type name this repository declares, any kind, -> namespaces

    for path, text in texts.items():
        project = os.path.relpath(path, ROOT).split(os.sep)[0]
        space = NAMESPACE.search(text)
        space = space.group(1) if space else ""

        for other in ANY_TYPE.finditer(text):
            elsewhere.setdefault(other.group(1), set()).add(space)

        for pattern, member_pattern in ((STATIC_CLASS, MEMBER), (ENUM, ENUM_MEMBER)):
            for found in pattern.finditer(text):
                name = found.group(1)
                inside = body(text, found.end())

                known.setdefault(name, set()).update(member_pattern.findall(inside))
                home.setdefault(name, set()).add(project)

    # CamelWorks.Nav's Host and CamelWorks.UI's Host are not the same word, and this cannot tell
    # which one a call reached.
    for name, spaces in elsewhere.items():
        if len(spaces) > 1:
            known.pop(name, None)

    if not known:
        print("nothing to check")
        return 0

    call = re.compile(r"(?<![.\w])(" + "|".join(sorted(known)) + r")\.(\w+)")

    bad = []

    for path in paths:
        project = os.path.relpath(path, ROOT).split(os.sep)[0]
        lines = STRINGS.sub(lambda m: " " * len(m.group(0)), texts[path]).splitlines()

        for n, line in enumerate(lines, 1):
            for owner, member in call.findall(line):
                if member in known[owner]:
                    continue

                # Only where the calling project could actually see the declaration: its own, or
                # CamelWorks.Core, which every project references.
                if not home[owner] & {project, "CamelWorks.Core"}:
                    continue

                bad.append((os.path.relpath(path, ROOT), n, owner, member, line.strip()))

    for rel, n, owner, member, line in bad:
        print("%s:%d: %s has no member \"%s\"." % (rel, n, owner, member))
        print("    " + line)

    if bad:
        print()
        print("%d call(s) naming a member that is not there." % len(bad))
        return 1

    print("%d static types and enums, every member named through them declared" % len(known))
    return 0


if __name__ == "__main__":
    sys.exit(main())
