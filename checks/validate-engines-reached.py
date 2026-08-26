#!/usr/bin/env python3
"""Fail when an engine in Core is called only by its own tests.

The capability map says every engine has a surface. It checks that a row exists, and a row is a
sentence somebody wrote. ProjectLease had a row - "Every panel: a blue strip when someone else
holds the project" - and no panel had one, and no code anywhere called it. Two people could edit
the same project settings and the second save won silently, which is the exact bug the lease was
written to stop. SectionDiff, which turns "the file changed" into "they changed the clash matrix",
sat beside it in the same state.

Both passed every check in this directory, including the capability map's own, because a promise
in a document and a call site in a program are different things and only one of them ships.

So this looks for the call site. A public documented `static class` in Core is an engine: it
cannot be reached through a `var`, so if its name is not written anywhere it is not called. When
the only file naming it is CamelWorks.Core.Tests, what exists is a tested feature nobody can use.

Comments do not count, and that is the whole difficulty. The first version of this check passed
SetFamily, because SetLibraryWindow names it in a doc comment saying it had no caller - a sentence
about the absence, mistaken for the presence. So every source is read with its comments stripped:
what remains is code, and code is the only thing that calls anything.

Infrastructure is exempt by name in ALLOWED, and each entry says why. A growing exempt list is
this check being talked out of its job.

    python3 checks/validate-engines-reached.py
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent

PRODUCT = ("CamelWorks.Core", "CamelWorks.UI", "CamelWorks.Nav", "CamelWorks.Nav.Clash")
TESTS = "CamelWorks.Core.Tests"

# A documented public static class: something with entry points and no instances.
ENGINE = re.compile(r"///\s*<summary>\s*\n(?:.*?\n)*?\s*public static class (\w+)")

# Engines Core calls on its own behalf, that no surface should ever name directly.
ALLOWED = {
    # The journal folds itself when the store loads it; a tool folding a journal by hand is the
    # bug, not the feature.
    "JournalFold",
}


BLOCK = re.compile(r"/\*.*?\*/", re.DOTALL)
LINE = re.compile(r"^\s*//.*$", re.MULTILINE)


def code(text):
    """The source with its comments taken out.

    Whole-line comments only, plus block comments. A trailing `// note` after real code is left
    alone: cutting at the first `//` on a line would also cut the middle out of every URL and
    every path in a string literal, and a type name mentioned in a trailing note is rare enough
    to be worth the false pass.
    """
    return LINE.sub("", BLOCK.sub("", text))


def sources(*folders):
    """Every source in these folders, twice: as written, and with the comments taken out."""
    return {p: (p.read_text(encoding="utf-8"), code(p.read_text(encoding="utf-8")))
            for folder in folders if (ROOT / folder).is_dir()
            for p in sorted((ROOT / folder).rglob("*.cs"))}


def main():
    product = sources(*PRODUCT)
    tests = sources(TESTS)

    # Found in the source as written, because an engine is declared under its doc comment.
    engines = {}
    for path, (written, _) in product.items():
        if not str(path).startswith(str(ROOT / "CamelWorks.Core")):
            continue
        for match in ENGINE.finditer(written):
            engines.setdefault(match.group(1), path)

    problems = 0

    for engine, home in sorted(engines.items()):
        if engine in ALLOWED:
            continue

        word = re.compile(r"\b" + engine + r"\b")

        # Its own file counts only when it uses itself more than the once it takes to declare it:
        # a helper the class beside it calls is reached, a class nobody names is not.
        # Looked for in the code, because only code calls anything.
        if any(word.search(text) for path, (_, text) in product.items() if path != home):
            continue
        if len(word.findall(product[home][1])) > 1:
            continue

        where = "tested, and called by nothing" if any(word.search(t) for _, t in tests.values()) \
                else "called by nothing at all"

        print(f"FAIL: {engine} ({home.relative_to(ROOT)}) is {where}. "
              f"Give it a surface, or say in ALLOWED why it does not need one.", file=sys.stderr)
        problems = 1

    if problems:
        return 1

    print(f"engines reached: {len(engines)} static engines in Core, "
          f"every one of them called by something that is not a test")
    return 0


if __name__ == "__main__":
    sys.exit(main())
