#!/usr/bin/env python3
"""Fail on a skin key that is defined twice, or asked for and never defined.

The skin is a ResourceDictionary the whole product looks things up in by name, and both ways of
getting a name wrong are silent:

  A duplicate x:Key makes the dictionary throw while it is being built. Every control in the
  product then comes up in Windows' own styling, nothing says why, and the failure looks exactly
  like a design decision. This cost a full CI round trip once: a FontFamily and a Style both
  called "Mono".

  A key that is asked for and does not exist is quieter still. SetResourceReference against a
  missing key leaves the property at its default, so one heading is the wrong size, or one card
  has no border, and nothing at all is logged.

Neither is a compiler error. The XAML compiles either way, and the C# is passing strings.

What is checked:

  * every x:Key in the sheet is unique
  * every key the C# asks for by literal - Wearing, Tint, SetResourceReference, Skin.Of, Ui.Glyph
    and the tone banners - is defined, either in the sheet or in Skin.cs's palette table
  * every {DynamicResource}/{StaticResource} inside the sheet resolves within it

Keys built at run time (Fam. plus a workspace id) cannot be checked from here and are listed as
prefixes instead, so a workspace added without a colour still fails.
"""

import collections
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SHEET = ROOT / "CamelWorks.UI" / "Themes" / "SkinSheet.xaml"
PALETTE = ROOT / "CamelWorks.UI" / "Views" / "Skin.cs"
SOURCES = ["CamelWorks.UI", "CamelWorks.Studio"]

# Composed at run time from a workspace id. The colour for each one is proved by validate-ribbon
# and by icons.py, which both fail on a workspace with no entry.
PREFIXES = ("Fam.",)

KEY = re.compile(r'x:Key="([^"]+)"')
LOOKUP = re.compile(r'\{(?:Dynamic|Static)Resource\s+([^{}\s]+)\s*\}')

# How the C# asks for one. Each of these takes the key as a plain string, which is exactly why
# none of them can be checked by the compiler.
ASKS = [
    re.compile(r'\.Wearing\(\s*"([^"]+)"\s*\)'),
    re.compile(r'\.Tint\([^,()]+,\s*"([^"]+)"\s*\)'),
    re.compile(r'SetResourceReference\([^,()]*(?:\([^()]*\))?[^,()]*,\s*"([^"]+)"\s*\)'),
    re.compile(r'Skin\.Of\(\s*"([^"]+)"\s*\)'),
    re.compile(r'Glyph\([^,]+,\s*[^,]+,\s*"([^"]+)"'),
    re.compile(r'Banner\([^;]*?"([^"]+)",\s*"([^"]+)",\s*"([^"]+)"\s*\)'),
]


def defined():
    """Every key the skin offers: the sheet's own, plus the palette Skin.cs writes into it."""
    sheet = SHEET.read_text(encoding="utf-8")
    keys = KEY.findall(sheet)

    palette = PALETTE.read_text(encoding="utf-8")

    # The palette table is rows of ("Key", "#light", "#dark"); the gradients have five columns.
    for row in re.findall(r'\(\s*"([A-Za-z.]+)"\s*,\s*"#', palette):
        keys.append(row)

    return sheet, keys


def main():
    problems = 0

    if not SHEET.exists():
        print(f"FAIL: {SHEET.relative_to(ROOT)} is missing", file=sys.stderr)
        return 1

    sheet, keys = defined()

    repeated = sorted(k for k, n in collections.Counter(KEY.findall(sheet)).items() if n > 1)

    if repeated:
        print("FAIL: defined more than once in the skin, which makes the whole dictionary throw "
              "while it is built: " + ", ".join(repeated), file=sys.stderr)
        problems = 1

    known = set(keys)

    # Inside the sheet itself. {x:Type Button} and friends are type keys, not names.
    for name in sorted(set(LOOKUP.findall(sheet))):
        if name.startswith("{") or name.startswith("x:") or name in known:
            continue

        print(f"FAIL: the skin looks up {name!r}, which nothing defines", file=sys.stderr)
        problems = 1

    asked = collections.defaultdict(list)

    for project in SOURCES:
        for path in sorted((ROOT / project).rglob("*.cs")):
            text = path.read_text(encoding="utf-8")

            for pattern in ASKS:
                for found in pattern.findall(text):
                    for name in (found if isinstance(found, tuple) else (found,)):
                        asked[name].append(path.relative_to(ROOT))

    for name in sorted(asked):
        if name in known or name.startswith(PREFIXES):
            continue

        where = ", ".join(sorted({str(p) for p in asked[name]}))
        print(f"FAIL: {where} asks for skin key {name!r}, which nothing defines", file=sys.stderr)
        problems = 1

    if problems:
        return 1

    print(f"skin consistent: {len(set(keys))} keys, none repeated, "
          f"{len(asked)} asked for by name and all defined")
    return 0


if __name__ == "__main__":
    sys.exit(main())
