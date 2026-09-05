#!/usr/bin/env python3
"""The pages a render is told to draw have to exist.

A render costs Windows minutes at twice the rate and is asked for to settle one screen, so the
workflow names the pages it wants rather than drawing all of them. Which introduces exactly one
new way to waste a run: a page id that does not match anything.

Nothing would say so. The harness would draw nothing, the job would go green, the orphan branch
would be force-pushed with no pictures on it, and whoever asked would go looking for a fault in a
screen that was never photographed. The harness logs NO SUCH PAGE for it, but a log inside a
Windows job is not where anybody looks first.

So: every id named as a default - in the workflow's dispatch input and in the harness's own
fallback - must be a page the harness actually has, and the two defaults must agree with each
other. A page renamed in the Studio is then a failed check here rather than an empty render.
"""
import re
import sys
from pathlib import Path

STUDIO = Path("CamelWorks.Studio")
FLOW = Path(".github/workflows/ui-shots.yml")
PROGRAM = STUDIO / "Program.cs"


def pages():
    """Every page id the harness can draw, taken from the sheets themselves."""
    found = set()

    for source in STUDIO.glob("*.cs"):
        said = source.read_text(encoding="utf-8")

        # Panels and Dialogs go through Page("id", ...); Sheets writes Id = "id" outright.
        found.update(re.findall(r'Page\(\s*"([a-z0-9-]+)"', said))
        found.update(re.findall(r'Id\s*=\s*"([a-z0-9-]+)"', said))

    return found


def named(text, what):
    """The comma-separated ids out of one default, or None when it cannot be found."""
    if what == "workflow":
        found = re.search(r'^\s*default:\s*"([^"]*)"', text, re.MULTILINE)
    else:
        found = re.search(r'private const string Only\s*=\s*"([^"]*)"', text)

    if not found:
        return None

    return [one.strip() for one in found.group(1).split(",") if one.strip()]


def main() -> int:
    for path in (PROGRAM, FLOW):
        if not path.exists():
            print(f"{path}: not found", file=sys.stderr)
            return 1

    drawable = pages()

    if not drawable:
        print(f"{STUDIO}: no page ids found at all — has Page(...) been renamed?", file=sys.stderr)
        return 1

    flow = named(FLOW.read_text(encoding="utf-8"), "workflow")
    harness = named(PROGRAM.read_text(encoding="utf-8"), "harness")

    bad = []

    if flow is None:
        bad.append(f"{FLOW}: the dispatch input has no default to check")

    if harness is None:
        bad.append(f"{PROGRAM}: no `private const string Only` to check")

    for where, ids in ((FLOW, flow), (PROGRAM, harness)):
        for one in ids or []:
            if one.lower() == "all":
                continue

            if one not in drawable:
                bad.append(f"{where}: names \"{one}\", which is not a page the harness draws")

    # Two defaults that disagree is a run that draws something other than what the workflow says
    # it draws, which is worse than either being wrong on its own.
    if flow is not None and harness is not None and sorted(flow) != sorted(harness):
        bad.append(f"{FLOW} defaults to {','.join(flow)} and {PROGRAM} to {','.join(harness)}")

    if bad:
        print("The render is pointed at pages that are not there:")

        for said in bad:
            print(f"  {said}")

        print()
        print("A page id that matches nothing draws nothing, and a run that drew nothing goes")
        print("green and force-pushes an empty branch. The ids are:")

        for one in sorted(drawable):
            print(f"  {one}")

        return 1

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
