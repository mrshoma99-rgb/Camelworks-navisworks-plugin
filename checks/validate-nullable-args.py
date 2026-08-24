#!/usr/bin/env python3
"""Catch a nullable string handed straight to a method that will not take one.

The compiler catches this too, but only on Windows, and only after a push. The
pattern is always the same shape: a type says `string? Problem`, a guard says
`if (!thing.IsUsable)`, and the branch hands `thing.Problem` to something that
declared a plain `string`. The guard is true, but it is one indirection away
from the null check, and flow analysis does not follow it.

The fix is always the same too: read the nullable member into a local, test the
local, pass the local.

Deliberately narrow, because a check that cries wolf is worse than no check.
Three filters, each of which cost a false positive to learn:

  * the member has to be declared `string?` somewhere and plain `string`
    nowhere, so a name two types share (`Text`, `Value`, `Scope`) is dropped
    rather than guessed at;
  * the sink has to be a method the repo itself declares taking a plain
    `string`;
  * a null test of that same member on this line or the three above it is the
    narrowing the compiler follows, so it is not a finding.

What is left is the shape that actually breaks the build: the guard tests one
member and the branch passes another.
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# `public string? Refusal =>` / `internal string? Problem { get; }` / `string? Problem;`
NULLABLE_MEMBER = re.compile(
    r"\b(?:public|internal|protected|private)\s+(?:static\s+)?(?:readonly\s+)?string\?\s+"
    r"([A-Z]\w*)\s*(?:=>|\{|;|=)")

# The same shape without the question mark. A name that appears both ways belongs
# to two different types and this check cannot tell which one a call reached.
PLAIN_MEMBER = re.compile(
    r"\b(?:public|internal|protected|private)\s+(?:static\s+)?(?:readonly\s+|const\s+)?string\s+"
    r"([A-Z]\w*)\s*(?:=>|\{|;|=)")

# A method whose every `string` parameter is non-nullable, so any of them is a trap.
TAKES_PLAIN_STRING = re.compile(
    r"\b(?:public|internal)\s+static\s+[\w.<>?\[\]]+\s+([A-Z]\w*)\s*\(\s*string\s+\w+\s*[,)]")


def sources():
    for base, dirs, files in os.walk(ROOT):
        dirs[:] = [d for d in dirs if d not in (".git", "bin", "obj")]
        for name in files:
            if name.endswith(".cs"):
                yield os.path.join(base, name)


def main():
    paths = list(sources())
    members, plain, sinks = set(), set(), set()

    for path in paths:
        with open(path, encoding="utf-8") as handle:
            text = handle.read()
        members.update(NULLABLE_MEMBER.findall(text))
        plain.update(PLAIN_MEMBER.findall(text))
        sinks.update(TAKES_PLAIN_STRING.findall(text))

    members -= plain

    if not members or not sinks:
        print("nothing to compare")
        return 0

    # Ui.Problem(thing.Refusal) — a sink, a dotted path, a nullable member, nothing after.
    call = re.compile(
        r"\b(" + "|".join(sorted(sinks)) + r")\(\s*[\w.\[\]]*\.(" + "|".join(sorted(members)) + r")\s*\)")

    bad = []

    for path in sorted(paths):
        with open(path, encoding="utf-8") as handle:
            lines = handle.read().splitlines()

        for n, line in enumerate(lines, 1):
            if "??" in line:
                continue
            for sink, member in call.findall(line):
                near = " ".join(lines[max(0, n - 4):n])
                if re.search(r"\." + member + r"\s*(?:!=|==)\s*null", near):
                    continue
                if re.search(r"\bis\s+(?:not\s+)?null\b", near) and member in near:
                    continue
                bad.append((os.path.relpath(path, ROOT), n, sink, member, line.strip()))

    for path, n, sink, member, line in bad:
        print("%s:%d: %s is nullable, %s() is not. Read it into a local first." % (
            path, n, member, sink))
        print("    " + line)

    if bad:
        print()
        print("%d nullable argument(s). Windows would have failed the build on these." % len(bad))
        return 1

    print("%d unambiguously nullable member(s) against %d plain-string sink(s) across "
          "%d files: nothing handed over unguarded" % (len(members), len(sinks), len(paths)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
