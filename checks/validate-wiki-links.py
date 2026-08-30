#!/usr/bin/env python3
"""Every link between wiki pages names a page that exists, and a heading that exists.

A GitHub wiki resolves `[text](Page-Name)` against the page names, and `#anchor` against that
page's headings, slugged. Neither is checked by anything at push time: a renamed page leaves
dead links behind on every page that pointed at it, and nobody finds out until a reader does.
A link that names nothing does not even render as an error — it renders as an invitation to
create the page, which reads as the documentation being unfinished.

The pages live in `wiki/` and are copied into the wiki repository as they are (see
docs/wiki-publishing.md), so this checks them where they are reviewed rather than after.
"""

import glob
import os
import re
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WIKI = os.path.join(HERE, "wiki")


def slug(heading):
    """GitHub's own heading slug: lowercased, punctuation dropped, spaces to dashes."""
    return re.sub(r"[^a-z0-9 -]", "", heading.strip().lower()).replace(" ", "-")


def headings(path):
    with open(path, encoding="utf-8") as handle:
        return [slug(h) for h in re.findall(r"^#{1,6} (.+)$", handle.read(), re.M)]


def main():
    if not os.path.isdir(WIKI):
        print("no wiki/ directory - nothing to check")
        return 0

    files = sorted(glob.glob(os.path.join(WIKI, "*.md")))
    pages = {os.path.splitext(os.path.basename(f))[0] for f in files}
    problems = []
    links = 0

    for path in files:
        name = os.path.basename(path)

        with open(path, encoding="utf-8") as handle:
            body = handle.read()

        for match in re.finditer(r"\]\(([^)\s]+)\)", body):
            target = match.group(1)

            if target.startswith(("http://", "https://", "mailto:")):
                continue

            links += 1
            page, _, anchor = target.partition("#")

            if page and page not in pages:
                problems.append(name + ': links to "' + page + '", which is not a page')
                continue

            if not anchor:
                continue

            on = os.path.join(WIKI, (page or os.path.splitext(name)[0]) + ".md")

            if anchor not in headings(on):
                problems.append(name + ': links to "' + target
                                + '", and that page has no such heading')

    for problem in problems:
        print("FAIL: " + problem)

    if problems:
        print()
        print("Fix the link, or add the page it names.")
        return 1

    print("wiki links resolve: " + str(links) + " links across " + str(len(files)) + " pages")
    return 0


if __name__ == "__main__":
    sys.exit(main())
