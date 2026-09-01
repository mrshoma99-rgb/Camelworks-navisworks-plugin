#!/usr/bin/env python3
"""A control kept in a field must be freed before it is put somewhere else.

The user's report, three times now, most recently on switching test:

    Something went wrong: Specified element is already the logical child of
    another element. Disconnect it first.

A screen keeps a control in a field when it has to outlive a redraw - a search box that must not
lose what was typed, a header whose tri-state is redrawn on its own. Every redraw then builds a
fresh panel and puts the kept control into it. Clearing the old panel's children does not detach
the control from the row that held it, and neither does throwing the old panel away, so the second
draw hands one element to a second parent and WPF refuses.

It is invisible in review: the line reads exactly like every other Add, and the first draw works.
It only appears on the second one, which in a screen with a rail is "click another test".

Ui.Loose takes an element off whatever is holding it. This checks that a field-held control goes
through it - and the check exists because the fix keeps being written correctly in one place and
forgotten in the next.

    python3 checks/validate-kept-controls-loose.py
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
UI = ROOT / "CamelWorks.UI"

# Every way of giving one element to a parent: added to a panel's children, or made the content
# or the child of something that holds one. All three re-parent, and all three throw on the second.
ADD = re.compile(
    r"\.Children\.(?:Add|Insert)\s*\(\s*(?:[^,()]+,\s*)?(_\w+)\s*\)"
    r"|\.(?:Content|Child)\s*=\s*(_\w+)\s*[;,]")

# Fields that are not controls at all - a list of them, a count - cannot be re-parented.
# The trailing ? matters: a field declared "private UIElement? _write;" is every bit as
# re-parentable as one without it, and leaving it out is how this check first missed one.
DECLARED = re.compile(r"^\s*(?:private|internal|protected)[^=;()]*?\b(\w+)\??\s+(_\w+)\s*[=;]", re.M)

# What WPF will refuse to re-parent. Anything else named with an underscore is not an element.
ELEMENTS = (
    "UIElement", "FrameworkElement", "Panel", "StackPanel", "WrapPanel", "DockPanel", "Grid",
    "Border", "ContentControl", "ItemsControl", "ScrollViewer", "TextBlock", "TextBox",
    "ComboBox", "CheckBox", "Button", "ListView", "ListBox", "Image", "Canvas", "Expander",
    "BoardList", "ProgressBar",
)


def kept(text):
    """The fields in this file that hold something WPF can refuse to re-parent."""
    found = {}

    for match in DECLARED.finditer(text):
        kind, name = match.group(1), match.group(2)
        if kind in ELEMENTS:
            found[name] = kind

    return found


def main():
    bad = []

    for path in sorted(UI.rglob("*.cs")):
        text = path.read_text(encoding="utf-8", errors="replace")
        controls = kept(text)

        if not controls:
            continue

        for i, line in enumerate(text.splitlines()):
            for match in ADD.finditer(line):
                name = match.group(1) or match.group(2)

                if name not in controls:
                    continue

                if "Ui.Loose(" in line:
                    continue

                bad.append((path.relative_to(ROOT), i + 1, name, controls[name], line.strip()))

    if not bad:
        print("every control kept in a field is freed before it is put somewhere else")
        return 0

    print("Kept controls added to a panel without being freed first:\n")

    for path, line, name, kind, text in bad:
        print("  {}:{}  {} ({})".format(path, line, name, kind))
        print("    {}".format(text))

    print("\nThe first draw works and the second one throws \"already the logical child of another")
    print("element\". Wrap it: Children.Add(Ui.Loose(" + bad[0][2] + ")).")

    return 1


if __name__ == "__main__":
    sys.exit(main())
