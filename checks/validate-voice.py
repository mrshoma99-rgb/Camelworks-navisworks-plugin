#!/usr/bin/env python3
"""User-facing text and comments must read like AEC software, not like prose.

CLAUDE.md sets the register: plain, professional, technical English. This checks the two
things that are mechanically detectable.

1. Runtime strings must not narrate. Labels, tooltips and messages describe what a control
   does or what happened. They do not refer to an unnamed person, recount what the product
   used to do, or appeal to how things are generally done.

2. Comments must not open with a bolded aphorism. `<b>...</b>` at the head of a comment is a
   rhetorical setup; a comment states the constraint, the reason for a non-obvious choice, or
   the failure mode being guarded against.

The rewrite is being done in passes. `CLEANED` lists the files a pass has been through;
those fail the check. Everything else is counted and reported as a backlog so the size of
the remaining work is visible without blocking the build. Add paths to `CLEANED` as passes
land, until the list is the whole tree and it can be replaced with a blanket rule.

    python3 checks/validate-voice.py
"""
import pathlib
import re
import sys

ROOTS = ["CamelWorks.UI", "CamelWorks.Core", "CamelWorks.Nav", "CamelWorks.Nav.Clash",
         "CamelWorks.Studio"]

# Files a rewrite pass has been through. These must stay clean.
CLEANED = (
    "CamelWorks.UI/Views/MatrixWindow.cs",
    "CamelWorks.UI/Views/Ui.cs",
    "CamelWorks.UI/Views/Parts.cs",
    "CamelWorks.Core/Clash/MatrixBoard.cs",
    "CamelWorks.Nav/NavDocument.cs",
    "CamelWorks.Core/Abstractions/IViewpointStore.cs",
    "CamelWorks.Nav.Clash/NavClashSource.cs",
    "CamelWorks.Core/Report/XlsxWriter.cs",
    "CamelWorks.UI/Views/ProjectViews.cs",
    "CamelWorks.Nav/NavViewSession.cs",
    "CamelWorks.Core/Clash/ClashBcf.cs",
    "CamelWorks.UI/Views/CompareViews.cs",
    "CamelWorks.Core/Project/TeamRoster.cs",
    "CamelWorks.Core/Findings/Finding.cs",
    "CamelWorks.Core/Compare/ModelCompare.cs",
    "CamelWorks.Core/Clash/MatrixSanity.cs",
    "CamelWorks.UI/Views/TeamWindow.cs",
    "CamelWorks.UI/Views/Skin.cs",
    "CamelWorks.UI/Views/Pointer.cs",
    "CamelWorks.UI/Views/Pickers.cs",
    "CamelWorks.UI/Views/MatrixLook.cs",
    "CamelWorks.UI/Views/MatrixKey.cs",
    "CamelWorks.UI/Views/IdsWindow.cs",
    "CamelWorks.UI/Views/HelpWindow.cs",
    "CamelWorks.UI/Views/ClearanceViews.cs",
    "CamelWorks.UI/Shell/HostWindow.cs",
    "CamelWorks.UI/Views/ClashManageViews.cs",
    "CamelWorks.UI/Views/MatrixReadWindow.cs",
    "CamelWorks.Core/Clash/MatrixNamePattern.cs",
    "CamelWorks.Core/Clash/MatrixFromNames.cs",
)

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
    backlog = []

    for path in files():
        into = bad if str(path).replace("\\", "/") in CLEANED else backlog

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

    if backlog:
        files_left = len({str(one[0]) for one in backlog})

        print(f"Backlog: {len(backlog)} place(s) in {files_left} file(s) not yet rewritten.")
        print("These do not fail the check. Add each file to CLEANED as it is done.")
        print()

    if not bad:
        return 0

    print(f"{len(bad)} place(s) in cleaned files do not read like AEC software:")
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
