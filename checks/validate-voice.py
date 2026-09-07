#!/usr/bin/env python3
"""User-facing text and comments must read like AEC software, not like prose.

CLAUDE.md sets the register: plain, professional, technical English. This checks the two
things that are mechanically detectable.

1. Runtime strings must not narrate. Labels, tooltips and messages describe what a control
   does or what happened. They do not refer to an unnamed person, recount what the product
   used to do, or appeal to how things are generally done.

   Nor do they judge on the reader's behalf ("categories not worth reporting") or name the
   control instead of the thing ("find the walkable surfaces in it"). Both were reported from
   a licensed seat, one screen at a time, four times. A label says what the field holds.

2. Comments must not open with a bolded aphorism. `<b>...</b>` at the head of a comment is a
   rhetorical setup; a comment states the constraint, the reason for a non-obvious choice, or
   the failure mode being guarded against.

The rewrite passes are complete, so this applies to every file under ROOTS and any hit
fails the check.

    python3 checks/validate-voice.py
"""
import pathlib
import re
import sys

ROOTS = ["CamelWorks.UI", "CamelWorks.Core", "CamelWorks.Nav", "CamelWorks.Nav.Clash",
         "CamelWorks.Studio"]

LITERAL = re.compile(r'"((?:[^"\\]|\\.)*)"')

# Markers of the narrative register. Each is a phrase that does not belong in a control's
# label or a message a user reads while working.
IN_STRINGS = [
    (re.compile(r'\b(somebody|nobody|anybody|everybody)\b', re.I),
     "refers to an unnamed person"),
    # "used to be/have/do" only. "used to cut concrete" is ordinary technical English.
    (re.compile(r'\bused to (be|have|has|do|show|say|mean|sit|read|take|go|open|carry|draw)\b',
                re.I),
     "recounts previous behaviour"),
    (re.compile(r'\b(for ever|an afternoon|a fortnight|on a Friday)\b', re.I),
     "anecdotal"),
    (re.compile(r'\bthe way (every|any)\b', re.I), "appeals to universal practice"),
    (re.compile(r'\bis not\b[^.]{0,30}\bit is\b', re.I), "rhetorical contrast"),
    (re.compile(r'\bthat is a real answer\b', re.I), "rhetorical"),

    # Judging for the reader. A field says what it holds; whether a value is worth anything is
    # the coordinator's call, not a label's.
    (re.compile(r'\b(not )?worth (reporting|having|looking|knowing|the)\b', re.I),
     "judges for the reader - say what the field holds"),

    # Naming the control rather than the thing. "Find the walkable surfaces in it" describes
    # what pressing does; the label has to say what the field is.
    (re.compile(r'^(find|look for|search for) (the|a|an) .{3,}\b(in|on) (it|them|this)\b', re.I),
     "describes the control rather than what the field holds"),
]

# A skin key, glyph path, format string or identifier is not copy.
NOT_COPY = re.compile(r'^[A-Za-z0-9_.:/\\{}#*|<>=;,()+\[\]-]*$')

BOLD_OPENER = re.compile(r'///?\s*(<summary>\s*)?\s*<b>')


def files():
    for root in ROOTS:
        base = pathlib.Path(root)

        if not base.exists():
            continue

        for path in sorted(base.rglob("*.cs")):
            if "/obj/" in str(path) or "/bin/" in str(path):
                continue

            yield path


def main() -> int:
    bad = []

    for path in files():
        into = bad

        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            stripped = line.strip()

            if stripped.startswith(("//", "///")):
                if BOLD_OPENER.search(stripped):
                    into.append((path, number, "comment opens with a bolded aphorism",
                                 stripped[:90]))

                continue

            for found in LITERAL.finditer(line):
                said = found.group(1)

                if len(said) < 8 or NOT_COPY.match(said) or " " not in said:
                    continue

                for pattern, why in IN_STRINGS:
                    if pattern.search(said):
                        into.append((path, number, why, said[:90]))
                        break

    if not bad:
        return 0

    print(f"{len(bad)} place(s) do not read like AEC software:")
    print()

    for path, number, why, said in bad[:60]:
        print(f"  {path}:{number}  {why}")
        print(f"      {said}")

    if len(bad) > 60:
        print()
        print(f"  ... and {len(bad) - 60} more.")

    print()
    print("See the writing style section of CLAUDE.md.")

    return 1


if __name__ == "__main__":
    raise SystemExit(main())
