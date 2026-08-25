#!/usr/bin/env python3
"""Fail when a screen exists and nothing opens it.

Home was a tab. When the tabs went the tab strip went with them, and the screen
that had been the front door of the product - the week's three steps, the state
of each, the log of what had been done - stayed exactly where it was, compiling,
documented, drawn by the harness, and called by nothing. Help went on describing
it as the front door for a fortnight. Baselines went the same way in the same
week.

Nothing catches that. The compiler does not mind an unused public method, the
harness renders the screen happily because it calls it directly, and every
picture people review shows a screen that is in the product only in the sense
that it is in the binary.

So: a screen builder in a view or window file has to be reached. Either
something names it in full - `ProjectViews.Health`, `HomeView.Build`, including
a method group handed to a dispatch table - or its own file calls it, which is
how a launcher calls the pages it offers.

Only public static builders in files named for views and windows, because those
are the files where a screen can go missing. The widget vocabulary in Ui.cs is a
library and is meant to have parts nobody has called yet.
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
AREAS = ("CamelWorks.UI", "CamelWorks.Studio")

SCREENS = ("View.cs", "Views.cs", "Window.cs")

OWNER = re.compile(r"\b(?:public|internal)\s+static\s+(?:partial\s+)?class\s+(\w+)")
BUILDER = re.compile(r"\bpublic\s+static\s+(?:UIElement|string\?|void|Panel|Border)\s+([A-Z]\w*)\s*\(")


def scrub(src):
    """Blank comments and string bodies, keeping the length so line numbers hold.

    Without this the check passes on the very thing it was written for: a section
    banner reading "// Baselines" and a doc comment reading "Build the screen" are
    both bare mentions of the name, and both would clear a dead screen.
    """
    out, i, n = [], 0, len(src)
    while i < n:
        c = src[i]
        if c == '"':
            j = i + 1
            while j < n:
                if src[j] == '\\':
                    j += 2
                    continue
                if src[j] == '"':
                    break
                j += 1
            out.append('""')
            out.append('\n' * src.count('\n', i, j))
            i = j + 1
            continue
        if c == '/' and src[i + 1:i + 2] == '/':
            j = src.find('\n', i)
            j = n if j < 0 else j
            out.append(' ' * (j - i))
            i = j
            continue
        if c == '/' and src[i + 1:i + 2] == '*':
            j = src.find('*/', i)
            j = n if j < 0 else j + 2
            out.append(''.join(ch if ch == '\n' else ' ' for ch in src[i:j]))
            i = j
            continue
        out.append(c)
        i += 1
    return ''.join(out)


def sources():
    for area in AREAS:
        for folder, _, names in os.walk(os.path.join(ROOT, area)):
            for name in names:
                if name.endswith(".cs"):
                    yield os.path.join(folder, name)


def main():
    paths = sorted(sources())
    everywhere = "\n".join(scrub(open(p, encoding="utf-8").read()) for p in paths)

    findings = []

    for path in paths:
        name = os.path.basename(path)

        if not name.endswith(SCREENS):
            continue

        src = scrub(open(path, encoding="utf-8").read())
        owner = OWNER.search(src)

        if owner is None:
            continue

        for hit in BUILDER.finditer(src):
            screen = hit.group(1)
            line = src.count("\n", 0, hit.start()) + 1

            if re.search(r"\b" + owner.group(1) + r"\." + screen + r"\b", everywhere):
                continue

            # Called from inside its own file, without the type name in front.
            here = [use for use in re.finditer(r"(?<![\w.])" + screen + r"\b", src)
                    if use.start() != hit.start(1)]

            if here:
                continue

            findings.append((path, line, "%s.%s is a screen nothing opens"
                             % (owner.group(1), screen)))

    for path, line, message in sorted(findings):
        print("%s:%d  %s" % (os.path.relpath(path, ROOT), line, message))

    if findings:
        print("\n%d screen(s) in the binary and not in the product." % len(findings))
        return 1

    print("Every screen has something that opens it.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
