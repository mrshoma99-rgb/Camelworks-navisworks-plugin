#!/usr/bin/env python3
"""Find a panel that is filled and never put on screen.

The Labels tool shipped like this. Its window built a `_tags` panel, drew a
heading, a recipe, a run button and a table of results into it on every read -
and never added it to anything. The ribbon button opened a window with four
sections on it and no labels, and every check passed, because from the code's
point of view nothing was wrong: the field existed, it was written to, the
methods that wrote to it were called.

What gives it away is that every mention of the name is a member access. A panel
that reaches a screen is handed to something at least once - `Ui.Stack(_tags)`,
`Children.Add(_tags)`, `Content = _tags`, `return _tags`. One whose every
appearance is `_tags.Something` is a panel nobody can see.

With one exception, which is the whole reason the first draft of this missed the
bug it was written for. `Ui.Runner(caption, output, work)` takes a panel to write
its progress and its result into, and that is not placing it - `_tags` was handed
to a Runner as its output and was still on no screen. A mention in that slot does
not count.

Narrow otherwise. Only fields whose declared type is a control that holds
children, and a single bare mention anywhere else is enough to clear it - because
this cannot tell every bare mention that places a panel from one that does not,
and would rather miss than shout.
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

HOLDERS = ("StackPanel", "Panel", "Border", "Grid", "WrapPanel", "DockPanel",
           "ContentControl", "ScrollViewer", "ListView", "Canvas")

FIELD = re.compile(
    r"\bprivate\s+(?:readonly\s+)?(?:static\s+)?(?:readonly\s+)?(" + "|".join(HOLDERS) + r")\??\s+"
    r"(_\w+)\s*=")


def scrub(src):
    """Blank comments and string bodies, so prose cannot look like code."""
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


def spans(text, start):
    """From the index of the opening bracket, the text inside it and the index past it."""
    depth, i, n = 0, start, len(text)
    while i < n:
        c = text[i]
        if c in '([{':
            depth += 1
        elif c in ')]}':
            depth -= 1
            if depth == 0:
                return text[start + 1:i], i + 1
        i += 1
    return None, n


def split_top(text):
    """Split on the commas that are not inside anything."""
    out, depth, cur = [], 0, []
    for c in text:
        if c in '([{':
            depth += 1
        elif c in ')]}':
            depth -= 1
        if c == ',' and depth == 0:
            out.append(''.join(cur).strip())
            cur = []
            continue
        cur.append(c)
    out.append(''.join(cur).strip())
    return out


def sinks(src):
    """The character ranges of panels handed to a Runner as its output."""
    out = []
    for hit in re.finditer(r"\bUi\.Runner\s*\(", src):
        args, _ = spans(src, hit.end() - 1)
        if args is None:
            continue
        parts = split_top(args)
        if len(parts) < 2:
            continue
        at = hit.end() + args.index(parts[1], len(parts[0]))
        out.append((parts[1], at))
    return out


def main():
    findings = []

    for area in ("CamelWorks.UI", "CamelWorks.Studio"):
        for folder, _, names in os.walk(os.path.join(ROOT, area)):
            for name in names:
                if not name.endswith(".cs"):
                    continue

                path = os.path.join(folder, name)
                src = scrub(open(path, encoding="utf-8").read())
                written = {at for name, at in sinks(src)}

                for hit in FIELD.finditer(src):
                    kind, field = hit.group(1), hit.group(2)
                    line = src.count("\n", 0, hit.start()) + 1

                    placed = False

                    for use in re.finditer(r"(?<![\w.])" + re.escape(field) + r"\b", src):
                        if use.start() == hit.start(1) or use.start() < hit.end():
                            continue
                        after = src[use.end():use.end() + 1]
                        if after == ".":
                            continue
                        if use.start() in written:
                            continue
                        placed = True
                        break

                    if not placed:
                        findings.append((path, line, "%s %s is written to and never placed on a "
                                                     "screen" % (kind, field)))

    for path, line, message in sorted(findings):
        print("%s:%d  %s" % (os.path.relpath(path, ROOT), line, message))

    if findings:
        print("\n%d panel(s) nobody can see." % len(findings))
        return 1

    print("Every panel field reaches a screen.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
