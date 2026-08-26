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

EXPECTED_COMMANDS = 22
EXPECTED_TOOLS = 51

# A ribbon panel is three rows tall, so a stack of Standard buttons is three at most.
MAX_PER_COLUMN = 3

UID = "{http://schemas.microsoft.com/winfx/2006/xaml}Uid"


def every_cs():
    """Every C# file in the UI, for the checks that look for a caption anywhere in it."""
    return "".join(p.read_text(encoding="utf-8") for p in sorted(UI.rglob("*.cs")))


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

    # Every button must name a tool that exists, and no two buttons may name the same one.
    #
    # This replaced a check that routes named a real "workspace/tab". Under that model three
    # buttons routed to a tab another button already opened - Headroom onto Clash Triage, Merge
    # onto BCF, Excel onto Data Manager - and the check passed every time, because each route
    # named a real place. It just happened to be somebody else's.
    tools_source = read("Shell/Tools.cs")
    tools = set(re.findall(r'Of\("([a-z]+)", "', tools_source))

    if len(tools) != EXPECTED_TOOLS:
        problems |= fail(f"{len(tools)} tools in the registry, expected {EXPECTED_TOOLS}")

    # Every tool that shows a surface has to name its one main action.
    for tool_id, shape, action in re.findall(
            r'Of\("([a-z]+)", "[^"]+", ToolShape\.(\w+),(?:.|\n)*?\n\s+(null|"[^"]*")\),',
            tools_source):
        if shape in ("Act", "Menu"):
            continue
        if action == "null" and tool_id != "help":
            problems |= fail(f'tool "{tool_id}" is a {shape} with no main action')

    # Every tool must sit on exactly one desk, and every benched tool must have a surface.
    #
    # This replaced a check that ToolViews carried a route per tool. Routing goes through the
    # desks now: a tool the desk registry has never heard of is unreachable from everywhere but
    # search, and a desk rail row whose tool has no case in Surfaces.cs benches "no surface
    # here". Help is the one tool on no desk - it is the window that must work when the rest of
    # the product will not - and Review sits on the Coordination desk but opens its palette.
    desks_source = read("Shell/Desks.cs")
    on_desks = re.findall(r'new DeskStage\("[^"]+"(?:, "([a-z]+)")(?:, "([a-z]+)")?(?:, "([a-z]+)")?(?:, "([a-z]+)")?(?:, "([a-z]+)")?\)', desks_source)
    desk_tools = [t for row in on_desks for t in row if t]

    seen_desk = {}
    for tool_id in desk_tools:
        if tool_id in seen_desk:
            problems |= fail(f'tool "{tool_id}" appears on the desks twice')
        seen_desk[tool_id] = True

    surfaces = read("Views/Surfaces.cs")
    benched = set(re.findall(r'case "([a-z]+)"', surfaces))

    for tool_id, shape in re.findall(
            r'Of\("([a-z]+)", "[^"]+", ToolShape\.(\w+),', tools_source):
        if tool_id == "help":
            continue
        if tool_id not in seen_desk:
            problems |= fail(f'tool "{tool_id}" is on no desk, so no rail reaches it')
        if shape in ("Act", "Menu", "Panel") or tool_id == "review":
            continue
        if tool_id not in benched:
            problems |= fail(f'tool "{tool_id}" is a {shape} with no surface in Surfaces.cs')

    # And no two tools may fall through to the same surface, unless the surface is told which
    # tool it is for. Five field tools share SiteWindow and the three exports share
    # ExportWindow; both are told, so five rail rows bench five different tools.
    for group in re.finditer(r'((?:\s*case "[a-z]+":\n)+\s*case "[a-z]+": return [^\n]+)', surfaces):
        block = group.group(1)
        ids = re.findall(r'case "([a-z]+)":', block)
        answer = block[block.index("return"):]

        if len(ids) > 1 and "toolid" not in answer.lower() and "what" not in answer.lower():
            problems |= fail(f"{len(ids)} tools ({', '.join(ids)}) all bench "
                             f"{answer.strip().rstrip(';')} - a rail row per tool, one surface "
                             f"between them")

    # And the main action must be a button that exists.
    #
    # Without this the registry is a wish. Every tool promises one filled button with a named
    # caption, and twenty-four of them once promised a caption no screen had - because the
    # registry was written as the design and the screens kept their own words. A promise nothing
    # checks is how "every tool has a main action" becomes a sentence in a doc comment.
    #
    # Matched on a prefix either way, because some captions are built at run time: the compile
    # button reads "Compile 9 files" and the repoint button reads "Bring in 4".
    #
    # Ui.Commit counts, and has to: a two-stage write builds its own filled Runner out of the
    # first argument, so on a screen whose whole purpose is that write there is no other Primary
    # for the tool's main action to point at.
    #
    # So does Ui.OnPicked marked primary. A screen whose main action is "do this to the rows you
    # picked" builds its filled button inside Ui.Picking, from the caption on the OnPicked - there
    # is no literal Ui.Primary anywhere for the tool to point at, and captioning it in two places
    # so a check can find it is the tail wagging the dog.
    buttons = [c.strip().rstrip(".").strip().lower()
               for c in re.findall(
                   r'Ui\.(?:Primary|Runner|Commit)\(\s*"([^"]+)"'
                   # The action is a lambda, so this has to cross parentheses to reach
                   # "primary:" - bounded, and stopped at the statement's semicolon, so it
                   # cannot run on into the next call and borrow its caption.
                   r'|Ui\.OnPicked\(\s*"([^"]+)"[^;]{0,300}?primary:\s*true',
                   every_cs()) for c in c if c]

    for tool_id, shape, action in re.findall(
            r'Of\("([a-z]+)", "[^"]+", ToolShape\.(\w+),(?:.|\n)*?\n\s+(null|"[^"]*")\),',
            tools_source):
        if shape in ("Act", "Menu") or action == "null":
            continue

        want = action.strip('"').strip().rstrip(".").strip().lower()

        if not any(b == want or b.startswith(want) or want.startswith(b) for b in buttons):
            problems |= fail(f'tool "{tool_id}" promises the action {action} '
                             f"and no Primary, Runner or picked action has that caption")

    named = re.findall(r'Cmd\("(ID_CW_\w+)", "[^"]+", "([a-z]+)"', catalogue_source)
    seen = {}

    for cid, tool_id in named:
        if tool_id not in tools:
            problems |= fail(f"{cid} names a tool that does not exist: {tool_id}")
        if tool_id in seen:
            problems |= fail(f"{cid} and {seen[tool_id]} both open \"{tool_id}\" - "
                             f"two buttons doing the identical thing")
        seen[tool_id] = cid

    if problems:
        return 1

    print(f"ribbon consistent: {len(xaml)} commands, {stacks} stacks, "
          f"{len(tools)} tools, no two buttons on the same one")
    return 0


if __name__ == "__main__":
    sys.exit(main())
