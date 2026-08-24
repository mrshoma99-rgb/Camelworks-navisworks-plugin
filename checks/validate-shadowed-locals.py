#!/usr/bin/env python3
"""Catch a local declared twice where one scope encloses the other.

C# forbids it outright: a local in a nested block may not take the name of one already in scope
(CS0136), and using the outer one earlier in the same block after the inner is declared is
CS0841. Both are errors on every job at once and invisible without a compiler, which the Linux
job does not have for the UI and this repository does not have at all for a quick local check.

The mistake it is really written for: a variable added at the top of a long method whose name is
already taken by an `out var` twenty lines down. Nothing reads wrong at either site.

Scopes are tracked by brace depth, so two declarations in sibling blocks — the ordinary and legal
case — are left alone. Only a declaration whose scope path is a prefix of another's is a finding.

Three things declare into a scope of their own rather than the block they sit in, and each cost
this check a run of false positives before it knew:

  * a `foreach`, `for` or `using` header - which is why two loops over `var child` in one method
    are legal, and a `var child` above them is not;
  * an expression body, so two `=>` members may both read `out var total`;
  * an `out var` in an `if`, `while` or `switch` condition, whose scope is that statement rather
    than the block around it;
  * a lambda body, which the compiler lets shadow the local it is being assigned to - the shape
    `var read = Runner(job => { var read = ...; })` is all over this repository and builds.

What is left is the one that really breaks the build: an `out var` in an ordinary statement,
taking a name something else in the method already has.
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# `var x =` and `out var x`, which scope to the block they sit in.
DECLARE = re.compile(r"(?<![.\w])(?:out\s+var|var)\s+([a-z_]\w*)\s*(?=[=)]|\bin\b)")

# A loop or using header, which scopes to itself. Two of them side by side may share a name.
HEADER = re.compile(r"\b(?:foreach|for|using)\s*\(\s*(?:var|[\w.<>\[\]?]+)\s+([a-z_]\w*)\s*(?=[=)]|\bin\b)")

# A condition, whose out variables scope to the statement rather than the block around it.
CONDITION = re.compile(r"\b(?:if|while|switch)\s*\(")

STRINGS = re.compile(r'"(?:[^"\\\n]|\\.)*"|\'(?:[^\'\\\n]|\\.)*\'|//[^\n]*')


def sources():
    for base, dirs, files in os.walk(ROOT):
        dirs[:] = [d for d in dirs if d not in (".git", "bin", "obj")]
        for name in files:
            if name.endswith(".cs"):
                yield os.path.join(base, name)


def blank(line):
    """The line with string literals and comments blanked, so text never reads as code."""
    return STRINGS.sub(lambda m: " " * len(m.group(0)), line)


def scan(path):
    """Every (name, line, scope path) the file declares, walking braces for the scope."""
    with open(path, encoding="utf-8") as handle:
        lines = handle.read().splitlines()

    found = []
    path_stack = []          # one counter per open brace, so a scope has a unique path
    depth_counter = [0]
    minted = 0               # synthetic scopes: loop headers, expression bodies, if-conditions
    arrow = False            # an expression body carried over from the line above
    block = False            # inside /* */

    lambda_open = False      # the brace about to be pushed opens a lambda body

    for n, raw in enumerate(lines, 1):
        line = raw

        if block:
            end = line.find("*/")
            if end < 0:
                continue
            line = " " * (end + 2) + line[end + 2:]
            block = False

        start = line.find("/*")
        if start >= 0:
            line = line[:start]
            block = True

        line = blank(line)

        # Where each declaration sits, so a brace earlier on the same line has already moved the
        # scope by the time the declaration is recorded. `if (a > b) { var t = a; ... }` three
        # times over is three scopes, not one.
        marks = []

        for match in HEADER.finditer(line):
            marks.append((match.start(), match.group(1), "header"))

        without = HEADER.sub(lambda m: " " * len(m.group(0)), line)

        for match in DECLARE.finditer(without):
            marks.append((match.start(), match.group(1), "local"))

        marks.sort()
        at = 0

        for column, name, kind in marks:
            while at < column:
                ch = line[at]
                if ch == "{":
                    depth_counter[-1] += 1
                    path_stack.append(("lambda", depth_counter[-1])
                                      if lambda_open or "=>" in line[:at]
                                      else depth_counter[-1])
                    depth_counter.append(0)
                    lambda_open = False
                elif ch == "}" and path_stack:
                    path_stack.pop()
                    depth_counter.pop()
                at += 1

            here = tuple(path_stack)

            before = line[:column]

            # A loop header, an expression body, and an out variable in a condition each declare
            # into a scope of their own.
            own = (kind == "header"
                   or arrow
                   or "=>" in before
                   or CONDITION.search(before) is not None)

            if own:
                minted += 1
                here += (-minted,)

            found.append((name, n, here))

        # An expression body whose `=>` is at the end of one line and whose body is on the next.
        stripped = line.strip()
        if "=>" in line and not stripped.endswith(";"):
            arrow = True
        elif stripped.endswith(";"):
            arrow = False

        # A lambda whose `=>` ends one line and whose brace opens the next.
        if stripped.endswith("=>"):
            lambda_open = True
        elif stripped:
            lambda_open = lambda_open and stripped == "{"

        while at < len(line):
            ch = line[at]
            if ch == "{":
                depth_counter[-1] += 1
                path_stack.append(("lambda", depth_counter[-1])
                                  if lambda_open or "=>" in line[:at]
                                  else depth_counter[-1])
                depth_counter.append(0)
                lambda_open = False
            elif ch == "}" and path_stack:
                path_stack.pop()
                depth_counter.pop()
            at += 1

    return found


def main():
    bad = []
    files = 0
    declared = 0

    for path in sorted(sources()):
        files += 1
        found = scan(path)
        declared += len(found)

        by_name = {}
        for name, line, scope in found:
            by_name.setdefault(name, []).append((line, scope))

        for name, sites in by_name.items():
            if len(sites) < 2:
                continue

            for i, (line_a, scope_a) in enumerate(sites):
                for line_b, scope_b in sites[i + 1:]:
                    short, long_ = (scope_a, scope_b) if len(scope_a) <= len(scope_b) \
                        else (scope_b, scope_a)

                    # Siblings are fine; only one scope enclosing the other is an error.
                    if long_[:len(short)] != short:
                        continue

                    # And a lambda body between them is not an enclosing scope for this purpose.
                    if any(isinstance(seg, tuple) for seg in long_[len(short):]):
                        continue

                    bad.append((os.path.relpath(path, ROOT), name, min(line_a, line_b),
                                max(line_a, line_b)))

    for rel, name, first, second in sorted(bad):
        print("%s:%d: \"%s\" is declared again at line %d, inside a scope the first one covers."
              % (rel, first, name, second))

    if bad:
        print()
        print("%d shadowed local(s). Every build job would fail on these." % len(bad))
        return 1

    print("%d locals across %d files: none shadows another" % (declared, files))
    return 0


if __name__ == "__main__":
    sys.exit(main())
