#!/usr/bin/env python3
"""Fail when a site writes issues and saves a file on its own.

An issue lives in the project file and names finding ids; the findings live in the sidecar
beside it. They were saved through two calls with two failure paths, and several sites made
one write without the other - so an attach that saved the project file but not the sidecar
left an issue naming a finding that is nowhere.

The rule: an act that touches the issue register must not call `Findings.Save()` or
`Store.Save()`. It saves through the one door - `WorkSave.Save` in Core, `Session.SaveWork`
in the UI - which refuses before writing anything, writes the sidecar first, and writes the
project file only if that landed.

An act is a lambda where there is one and a method otherwise, because a screen builds
several buttons in one method and each button is its own write.

A settings-only save is untouched by this. An act that never mentions the issue register has
no findings side to dangle, and `Store.Save()` there is the right call.

    python3 checks/validate-one-save-door.py
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
LOOKED_AT = [ROOT / "CamelWorks.UI", ROOT / "CamelWorks.Core", ROOT / "CamelWorks.Nav"]

# What counts as touching the issue register: the section itself, or the two entry points
# every raise and attach in the product goes through.
ISSUES = re.compile(r"IssuesSection|IssueViews\.(?:Write|Raise)\(")

# The saves that must not appear beside it.
BARE = re.compile(r"\b(?:Findings|Store)\.Save\(\)")

# A block opened by one of these is a statement, not a method.
STATEMENT = re.compile(
    r"\b(?:if|else|for|foreach|while|do|switch|case|using|lock|try|catch|finally|fixed|"
    r"unchecked|checked|get|set|add|remove)\b\s*$"
)

# What the line opening a method body looks like: a name, its parameters, and nothing after.
METHOD = re.compile(r"\)\s*(?::\s*this\([^)]*\)\s*)?$")


def masked(source):
    """The source with comments and string bodies blanked, so braces in them do not count."""
    out = []
    at = 0
    length = len(source)

    while at < length:
        two = source[at:at + 2]

        if two == "//":
            end = source.find("\n", at)
            end = length if end < 0 else end
            out.append(" " * (end - at))
            at = end
        elif two == "/*":
            end = source.find("*/", at + 2)
            end = length if end < 0 else end + 2
            out.append("".join(c if c == "\n" else " " for c in source[at:end]))
            at = end
        elif source[at] in "\"'":
            quote = source[at]
            verbatim = at > 0 and source[at - 1] == "@" and quote == "\""
            end = at + 1
            while end < length:
                if not verbatim and source[end] == "\\":
                    end += 2
                    continue
                if source[end] == quote:
                    if verbatim and source[end:end + 2] == quote * 2:
                        end += 2
                        continue
                    end += 1
                    break
                if not verbatim and source[end] == "\n":
                    break
                end += 1
            out.append("".join(c if c == "\n" else " " for c in source[at:end]))
            at = end
        else:
            out.append(source[at])
            at += 1

    return "".join(out)


def blocks(source):
    """Every brace pair in the file, innermost last, as (open, close) offsets."""
    seen = []
    stack = []

    for at, character in enumerate(source):
        if character == "{":
            stack.append(at)
        elif character == "}" and stack:
            seen.append((stack.pop(), at))

    return seen


def site_around(source, blank, at):
    """The act this offset belongs to: the lambda holding it, or the method.

    A lambda rather than the whole method, because a screen builds several buttons in one
    method and each one is its own act. "Settle it" saving a finding beside a separate
    "Raise an issue" button is not two halves of one write.
    """
    holding = [pair for pair in blocks(blank) if pair[0] < at < pair[1]]
    holding.sort(key=lambda pair: pair[1] - pair[0])

    for opens, closes in holding:
        head = blank[:opens].rstrip()

        if head.endswith("=>") or (METHOD.search(head) and not STATEMENT.search(head)):
            return source[opens:closes], source[:opens].count("\n") + 1

    return None


def files():
    for root in LOOKED_AT:
        if root.is_dir():
            for path in sorted(root.rglob("*.cs")):
                if "obj" not in path.parts and "bin" not in path.parts:
                    yield path


def main():
    problems = []
    checked = 0

    for path in files():
        source = path.read_text(encoding="utf-8", errors="replace")

        if "IssuesSection" not in source and "IssueViews." not in source:
            continue

        blank = masked(source)

        for hit in BARE.finditer(blank):
            checked += 1
            found = site_around(source, blank, hit.start())

            if found is None:
                continue

            body, line = found

            if not ISSUES.search(masked(body)):
                continue

            problems.append(
                f"{path.relative_to(ROOT)}:{source[:hit.start()].count(chr(10)) + 1}: "
                f"{hit.group(0)} in the act opening at line {line}, which also writes the "
                f"issue register")

    if problems:
        print("saves outside the one door:\n")
        for problem in problems:
            print("  " + problem)
        print(
            "\nAn issue names finding ids and the findings are in the sidecar beside the project\n"
            "file. Save both through Session.SaveWork (WorkSave.Save in Core): it refuses before\n"
            "writing anything, writes the sidecar first, and writes the project file only if that\n"
            "landed. A save with no findings side is not this check's business.")
        return 1

    print(f"one save door: {checked} bare save(s) looked at, none beside an issue write")
    return 0


if __name__ == "__main__":
    sys.exit(main())
