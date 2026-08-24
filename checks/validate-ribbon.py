#!/usr/bin/env python3
"""Check that the ribbon's three lists agree.

A command lives in three places: the ribbon markup that draws the button, the [Command] attribute
that registers it with the host, and the catalogue that routes it and feeds Find a Tool. Nothing in
the compiler connects them — a button present in two of the three is a button that either does
nothing, never appears, or cannot be found by search, and each of those looks like a different bug.

Also checks that every route a command points at is a workspace and tab that actually exist, since
a typo there sends a ribbon button to the wrong screen and nothing complains.

And checks the shape of the markup, because nothing compiles it. The project file ships CamelWorks.xaml
as loose content, so a layout mistake in it reaches the user intact. The mistake that matters is not
cosmetic: a Standard button placed straight into a RibbonPanelSource takes a whole column to itself
rather than stacking, which widens the panel until AdWindows claws the width back by dropping the
labels off the small buttons - and the tab renders as a row of unlabelled icons running off the end
of the ribbon.
"""

import re
import sys
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parent.parent
UI = ROOT / "CamelWorks.UI"

EXPECTED_COMMANDS = 27
EXPECTED_TABS = 19
EXPECTED_SWITCHER = 7

# A ribbon panel is three rows tall, so a stack of Standard buttons is three at most.
MAX_PER_COLUMN = 3

UID = "{http://schemas.microsoft.com/winfx/2006/xaml}Uid"


def read(path):
    return (UI / path).read_text(encoding="utf-8")


def fail(message):
    print("FAIL: " + message, file=sys.stderr)
    return 1


def kind(node):
    """The local name, with the clr-namespace the markup declares stripped off."""
    return node.tag.rpartition("}")[2]


def named(node):
    return node.get(UID) or node.get("Id") or kind(node)


def weight(title, button, size, orientation):
    """A Large button stands alone in its column; a Standard one only ever sits in a stack."""
    problems = 0

    got = button.get("Size")
    if got != size:
        problems |= fail(f'{title}: {named(button)} is Size="{got}" where the column takes {size}')

    got = button.get("Orientation")
    if got != orientation:
        problems |= fail(f'{title}: {named(button)} is Orientation="{got}" where a {size} button takes {orientation}')

    if button.get("ShowText") != "True":
        problems |= fail(f"{title}: {named(button)} does not show its text")

    return problems


def layout():
    """Every column in every panel is one Large button, or a RowPanel of up to three Standard ones."""
    problems = 0
    stacks = 0

    for source in ElementTree.parse(UI / "CamelWorks.xaml").iter():
        if kind(source) != "RibbonPanelSource":
            continue

        title = source.get("Title", "?")

        for column in source:
            if kind(column) == "RibbonRowPanel":
                stacks += 1
                buttons = [c for c in column if kind(c) != "RibbonRowBreak"]
                breaks = [c for c in column if kind(c) == "RibbonRowBreak"]

                if not buttons:
                    problems |= fail(f"{title}: an empty stack")
                elif len(buttons) > MAX_PER_COLUMN:
                    problems |= fail(f"{title}: a stack of {len(buttons)} buttons, and a panel is "
                                     f"{MAX_PER_COLUMN} rows tall")

                if buttons and len(breaks) != len(buttons) - 1:
                    problems |= fail(f"{title}: a stack of {len(buttons)} buttons carries {len(breaks)} "
                                     f"row breaks, expected {len(buttons) - 1} - without one between "
                                     f"every pair they share a row instead of stacking")

                for button in buttons:
                    problems |= weight(title, button, "Standard", "Horizontal")

            elif kind(column) == "NWRibbonButton":
                problems |= weight(title, column, "Large", "Vertical")

            else:
                problems |= fail(f"{title}: {named(column)} is a {kind(column)}, which is neither a "
                                 f"Large button nor a stack of Standard ones")

    if not stacks:
        problems |= fail("no RibbonRowPanel anywhere: nothing in the tab stacks")

    return problems, stacks


def main():
    problems = 0

    xaml = set(re.findall(r'Id="(ID_CW_\w+)"', read("CamelWorks.xaml")))
    plugin = set(re.findall(r'\[Command\("(ID_CW_\w+)"', read("CamelWorksPlugin.cs")))
    catalogue_source = read("Shell/CommandCatalogue.cs")
    catalogue = set(re.findall(r'"(ID_CW_\w+)"', catalogue_source))

    if len(xaml) != EXPECTED_COMMANDS:
        problems |= fail(f"the ribbon markup has {len(xaml)} buttons, expected {EXPECTED_COMMANDS}")

    shape, stacks = layout()
    problems |= shape

    for name, ids in (("ribbon markup", xaml), ("[Command] attributes", plugin), ("catalogue", catalogue)):
        missing = (xaml | plugin | catalogue) - ids
        if missing:
            problems |= fail(f"{name} is missing: {', '.join(sorted(missing))}")

    # Every route must name a workspace and a tab that exist.
    workspaces_source = read("Shell/Workspaces.cs")

    workspaces = re.findall(r'new Workspace\("(\w+)", "([^"]+)", (\w+), hasTabStrip: (true|false),(.*?)\)\),\n',
                            workspaces_source, re.S)

    tabs = {}
    switcher = 0
    tab_total = 0

    for wid, _title, pane, has_strip, body in workspaces:
        ids = re.findall(r'new WorkspaceTab\("([^"]+)"', body)
        tabs[wid] = ids
        if pane == "MainPane":
            switcher += 1
        if has_strip == "true":
            tab_total += len(ids)

    if switcher != EXPECTED_SWITCHER:
        problems |= fail(f"{switcher} switcher entries in the main pane, expected {EXPECTED_SWITCHER}")

    if tab_total != EXPECTED_TABS:
        problems |= fail(f"{tab_total} tabs across the tabbed workspaces, expected {EXPECTED_TABS}")

    for route in re.findall(r'CommandKind\.Pane, "([^"]+)"', catalogue_source):
        workspace, _, tab = route.partition("/")
        if workspace not in tabs:
            problems |= fail(f'route "{route}" names no workspace')
        elif tab and tab not in tabs[workspace]:
            problems |= fail(f'route "{route}" names no tab in {workspace}')

    # The catalogue's Pane entries carry their route as the third argument of the Pane helper, so
    # pull them from there too — a Pane command with no route would silently open nothing.
    pane_ids = re.findall(r'Pane\("(ID_CW_\w+)", "[^"]+", "([^"]+)"', catalogue_source)
    for cid, route in pane_ids:
        workspace, _, tab = route.partition("/")
        if workspace not in tabs:
            problems |= fail(f"{cid} routes to a workspace that does not exist: {route}")

    if problems:
        return 1

    print(f"ribbon consistent: {len(xaml)} commands, {stacks} stacks, "
          f"{switcher} switcher entries, {tab_total} tabs")
    return 0


if __name__ == "__main__":
    sys.exit(main())
