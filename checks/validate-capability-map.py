#!/usr/bin/env python3
"""Fail when an engine in Core has no surface named in docs/CAPABILITY_MAP.md.

A redesign is where features die quietly. This product lost sixteen of them in one
pass, and only two were noticed by eye: the clash matrix read out of a client's own
spreadsheet, and the WBS stamp. The rest - Freeze, the penetration schedule, the model
intake register, appearance transfer, DXF, the activity log, clash markers, the compare
stamp, compare clusters, similarity, the set-library generator, the sheet-sets reader,
the project lease strip, the value cleaner - went missing without a sound.

They went missing because the inventory everybody worked from was Tools.cs, and Tools.cs
is a list of *tools*, not of *capabilities*. Several shipped features were sub-panels
inside a tab and never had a tool entry at all, so a redesign that rebuilt every tool
faithfully still dropped them.

So the inventory is taken from the engines instead. Every public documented type in
CamelWorks.Core is a capability somebody paid for, and it must appear in the map with a
surface beside it. Adding an engine means naming its surface in the same commit.

Infrastructure is exempt, but only by name and only in ALLOWED below - a growing exempt
list is this check being talked out of its job, so each entry says why.

    python3 checks/validate-capability-map.py
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
CORE = ROOT / "CamelWorks.Core"
MAP = ROOT / "docs" / "CAPABILITY_MAP.md"

# Types that are how a surface works rather than something a coordinator opens.
ALLOWED = {
    # Seams. A surface talks to the host through these; none is a feature.
    "IModelSource", "IModelDocument", "IModelItem", "IModelWriteTransaction",
    "IViewSession", "IViewpointStore", "IClashSource", "IFileSystem", "IGraphHost",
    "ILayerResolver", "TraversalScopeKind", "StaleItemException", "OverrideLayer",
    "ClashSide", "ClashSideKind",
    # Storage plumbing. Every tool writes through it; no tool is it.
    "PhysicalFileSystem", "AtomicFile", "JournalEntry", "JournalFold", "JsonReader",
    "JsonValue", "VersionedDocument", "SectionDiff",
    # Primitives.
    "Hash", "IfcGuid", "Measure", "BcfGuid",
    # File-format writers' internal blocks - the report IS the surface, not its parts.
    "ReportBlock", "PageBreakBlock", "ImageBlock", "LegendBlock", "DisclosureBlock",
    "PngImage",
    # Exceptions.
    "SetExpressionTooComplexException",
}

TYPE = re.compile(
    r"///\s*<summary>\s*\n"                       # a documented type, which is
    r"(?:.*?\n)*?"                                # every type Core exposes: the doc
    r"\s*public (?:sealed |static |abstract |partial )*"
    r"(?:class|struct|enum|interface|record) (\w+)"
)


def engines():
    """Every public documented type in Core, with the file it lives in."""
    found = []
    for path in sorted(CORE.rglob("*.cs")):
        if "Testing" in path.parts:
            continue
        source = path.read_text(encoding="utf-8", errors="replace")
        for hit in TYPE.finditer(source):
            found.append((hit.group(1), path.relative_to(ROOT)))
    return found


def main():
    if not MAP.exists():
        print(f"missing {MAP.relative_to(ROOT)} - the capability map is not optional")
        return 1

    text = MAP.read_text(encoding="utf-8")
    # A name counts as mapped when the map mentions it in a code span, which is how
    # every row writes an engine. Prose mentions do not count: a surface has to be
    # named beside it, and the table is the only place that happens.
    mapped = set(re.findall(r"`([\w./+ `]+)`", text))
    names = set()
    for span in mapped:
        for part in re.split(r"[`+/\s]+", span):
            part = part.strip()
            if part:
                names.add(part.split(".")[-1])

    missing = []
    seen = set()
    for name, path in engines():
        if name in ALLOWED or name in seen:
            continue
        seen.add(name)
        if name not in names:
            missing.append((name, path))

    if missing:
        print(f"{len(missing)} engine(s) have no surface in docs/CAPABILITY_MAP.md:\n")
        for name, path in missing:
            print(f"  {name:34} {path}")
        print(
            "\nAdd a row naming the surface that reaches it. If it is plumbing rather\n"
            "than a capability, add it to ALLOWED in this file with the reason."
        )
        return 1

    print(f"capability map: {len(seen)} engines, every one with a surface")
    return 0


if __name__ == "__main__":
    sys.exit(main())
