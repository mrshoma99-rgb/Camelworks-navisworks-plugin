#!/usr/bin/env python3
"""A disabled control must not paint a plate behind itself.

The user's report, twice: "the grey out control buttons actually look like they are clicked not
grey out. they have a grey rectangle around them, i think the symbol should just he grey."

Every button style in this sheet draws a filled plate to mean hover, and a stronger one to mean
pressed. So a style that fills the plate to mean *disabled* is saying the loudest thing it can say
about a control nobody can press: a row of unavailable verbs reads as a row of held-down buttons.
Off is the symbol fading, not the background arriving.

It was fixed once, and it came back the same day - because the fix was made by finding a style that
had the fault rather than by asking which styles had it. Two of them still did, including the one
every row symbol in the product uses.

So this is the mechanism. Inside an IsEnabled=False trigger, a Background setter may only be
Transparent. Fade the content instead - Opacity on the plate, or Faint on the foreground - the way
every style here that got it right already does.

    python3 checks/validate-disabled-not-pressed.py
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
SHEETS = sorted((ROOT / "CamelWorks.UI" / "Themes").rglob("*.xaml"))

STYLE = re.compile(r'<Style x:Key="(\w+)"')
OFF = re.compile(r'Property="IsEnabled"\s+Value="False"')
END = re.compile(r"</Trigger>|/>\s*</Trigger>")
PAINTS = re.compile(r'Property="Background"\s+Value="(?!Transparent")([^"]*)"')


def check(path):
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    style = "(unnamed)"
    found = []

    for i, line in enumerate(lines):
        named = STYLE.search(line)
        if named:
            style = named.group(1)

        if not OFF.search(line):
            continue

        # The trigger's own body: this line onwards until it closes.
        for j in range(i, min(i + 12, len(lines))):
            painted = PAINTS.search(lines[j])

            if painted:
                found.append((j + 1, style, painted.group(1), lines[j].strip()))

            if "</Trigger>" in lines[j]:
                break

    return found


def main():
    bad = []

    for path in SHEETS:
        for line, style, value, text in check(path):
            bad.append((path.relative_to(ROOT), line, style, value, text))

    if not bad:
        print("no disabled control paints a plate behind itself")
        return 0

    print("Disabled controls drawn as pressed:\n")

    for path, line, style, value, text in bad:
        print("  {}:{}  in {}".format(path, line, style))
        print("    {}".format(text))

    print("\nA filled plate is what these styles draw for hover and for a press, so filling it for")
    print("disabled makes an unavailable control read as a held-down one. Use Transparent and fade")
    print("the content instead - Opacity on the plate, Faint on the foreground.")

    return 1


if __name__ == "__main__":
    sys.exit(main())
