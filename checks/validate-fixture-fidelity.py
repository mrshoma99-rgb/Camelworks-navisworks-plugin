#!/usr/bin/env python3
"""Fail when the real reader fills a field and the fake one never does.

The design harness draws the product's own screens over a fake source. Where that fake leaves a
field null which a licensed seat fills, the screen is photographed without it — and the picture is
then reviewed as though it were the product.

That is not hypothetical. `NavClashSource` set `NameA` and `NameB` on every result and
`FakeClashSource` never did, so the Manage board's "First item" and "Second item" columns — the two
facts a clash board exists to state — were empty in every render ever taken of it. The columns were
sized, ordered and judged against a board with its subject missing, and the review that came out of
those pictures recorded the emptiness as a product defect. `CategoryA` and `CategoryB` went the same
way, one field along, into the filter facets.

So: for every settable property on a type both readers produce, if the real reader assigns it, the
fake must assign it too. What the fake puts there is its own business; that it puts something there
is this check's.

The pairs are declared below rather than discovered, because "the real reader" is a judgement about
which file speaks for the host and not something a regular expression can tell.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# (what the type is called, where it is declared, who reads it for real, who fakes it)
PAIRS = [
    (
        "ClashResultInfo",
        "CamelWorks.Core/Abstractions/IClashSource.cs",
        ["CamelWorks.Nav.Clash/NavClashSource.cs", "CamelWorks.Core/Checks/FileCheck.cs"],
        "CamelWorks.Core/Testing/FakeClashSource.cs",
    ),
]

# A property the fake has no business carrying: it is the host's own bookkeeping, and the fake
# stands in for the host rather than reporting one.
EXCUSED = {
    "ClashResultInfo": set(),
}


def settable(text, kind):
    """Every settable property declared on one type."""
    start = re.search(r"\bclass\s+%s\b" % re.escape(kind), text)

    if start is None:
        return []

    # To the end of the type: the next class declaration at the same indent, or the file's end.
    rest = text[start.end():]
    stop = re.search(r"\n    (?:public|internal)\s+(?:sealed\s+|abstract\s+|static\s+)*class\s", rest)

    if stop is not None:
        rest = rest[: stop.start()]

    return re.findall(r"public\s+[\w?<>,\[\]. ]+?\s+(\w+)\s*\{\s*get;\s*set;\s*\}", rest)


def assigns(text, name):
    """Whether a file ever writes to a property of that name."""
    return re.search(r"(?<![\w.])%s\s*=(?!=)" % re.escape(name), text) is not None


def main():
    faults = []

    for kind, declared, real, fake in PAIRS:
        home = ROOT / declared

        if not home.exists():
            faults.append("%s: %s is gone, so this check is stale" % (kind, declared))
            continue

        names = settable(home.read_text(encoding="utf-8"), kind)

        if not names:
            faults.append("%s: no settable properties found in %s, so this check is stale"
                          % (kind, declared))
            continue

        faked = ROOT / fake

        if not faked.exists():
            faults.append("%s: %s is gone, so this check is stale" % (kind, fake))
            continue

        fake_text = faked.read_text(encoding="utf-8")

        for reader in real:
            here = ROOT / reader

            # A reader that is not in this checkout — Nav.Clash needs Navisworks — is skipped
            # rather than reported: its absence is the build, not a fault.
            if not here.exists():
                continue

            real_text = here.read_text(encoding="utf-8")

            for name in names:
                if name in EXCUSED.get(kind, set()):
                    continue

                if not assigns(real_text, name):
                    continue

                if assigns(fake_text, name):
                    continue

                faults.append(
                    "%s.%s is set by %s and never by %s, so every render draws it empty"
                    % (kind, name, reader, fake))

    if faults:
        print("The fake source does not carry what the real one does:")
        print()

        for fault in sorted(set(faults)):
            print("  " + fault)

        print()
        print("A screen photographed without a field it has on a licensed seat is a screen")
        print("nobody has actually reviewed. Give the fake something to put there.")

        return 1

    print("fixtures faithful: every field the real readers fill, the fake fills too")

    return 0


if __name__ == "__main__":
    sys.exit(main())
