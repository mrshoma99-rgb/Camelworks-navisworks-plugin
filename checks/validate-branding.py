#!/usr/bin/env python3
"""Fail when a company other than BIMCamel is named anywhere in the published tree.

CamelWorks was assembled by harvesting two of the owner's private codebases. One of them shipped
under its author's employer's identity — in namespaces, plug-in ids, package manifests, an About
dialog, an %AppData% folder and a marker string written into customer model files. The owner's
instruction is that none of it survives: the only company identity in the product is BIMCamel /
bimcamel.com.

That is easy to satisfy on the day and easy to lose later, because the leak is rarely in the code.
It comes back in a planning document, a commit message quoted into a README, or a comment that
explains where something was harvested from. So it is checked rather than remembered.

The predecessor's own product names are covered too. They are not a company, but the instruction
was also not to keep the old names, and a feature called by its old name in a document is how the
old name gets back into a ribbon label.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# Word-boundaried on purpose: "kvi" unanchored matches workbookViewId in the XLSX writer, and a
# check that cries wolf on generated OOXML is a check somebody switches off.
FORBIDDEN = [
    (r"\bkvi\b", "the predecessor's company initials"),
    (r"\bkvi[_-]tools\b", "the predecessor's product name"),
    (r"kanadevia", "a company that is not BIMCamel"),
    (r"\binova\b", "a company that is not BIMCamel"),
    (r"\bbettersets\b", "the predecessor's name for Set Builder"),
]

SKIP_DIRS = {".git", "bin", "obj", "node_modules", ".vs"}
SKIP_SUFFIXES = {".png", ".jpg", ".jpeg", ".gif", ".ico", ".zip", ".dll", ".exe", ".pdb", ".nupkg"}

# This file names the forbidden words in order to look for them.
SKIP_FILES = {Path(__file__).name}


def main():
    patterns = [(re.compile(p, re.IGNORECASE), why) for p, why in FORBIDDEN]
    hits = []
    scanned = 0

    for path in sorted(ROOT.rglob("*")):
        if not path.is_file():
            continue
        if any(part in SKIP_DIRS for part in path.relative_to(ROOT).parts):
            continue
        if path.suffix.lower() in SKIP_SUFFIXES or path.name in SKIP_FILES:
            continue

        try:
            text = path.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue

        scanned += 1

        for number, line in enumerate(text.splitlines(), 1):
            for pattern, why in patterns:
                found = pattern.search(line)
                if found:
                    rel = path.relative_to(ROOT)
                    hits.append(f"{rel}:{number}: \"{found.group(0)}\" — {why}")

    if hits:
        print("the published tree names something other than BIMCamel:", file=sys.stderr)
        for hit in hits:
            print("  " + hit, file=sys.stderr)
        print("\nThe only company identity in this product is BIMCamel / bimcamel.com.", file=sys.stderr)
        return 1

    print(f"branding clean: {scanned} files, only BIMCamel named")
    return 0


if __name__ == "__main__":
    sys.exit(main())
