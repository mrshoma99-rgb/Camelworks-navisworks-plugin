#!/usr/bin/env python3
"""Fetch Google Material Symbols and rasterise them into ribbon icons.

Kept in the repository, and re-runnable, because a folder of PNGs with no provenance is a folder
nobody dares change: which icon is this, where did it come from, what licence is it under, and how
do I make a matching one for the button I am about to add? The answer to all four is this file.

    python3 tools/icons.py            # write CamelWorks.UI/Resources/*.png
    python3 tools/icons.py --check    # verify every command has an icon, fetch nothing

Material Symbols are Apache-2.0 (see NOTICE). They are fetched at build-authoring time and the
resulting PNGs are committed, so neither the build nor CI ever needs the network.

There is no SVG rasteriser on the machines this runs on, so there is one here: a path parser, a
flattener, and a scanline fill with 4x4 supersampling. That is a lot less code than it sounds, and
far less risk than a build step that depends on ImageMagick being installed.
"""

import argparse
import math
import re
import struct
import sys
import urllib.request
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "CamelWorks.UI" / "Resources"

SOURCE = ("https://raw.githubusercontent.com/google/material-design-icons/master/symbols/web/"
          "{name}/materialsymbolsoutlined/{name}_24px.svg")

# The glyph colour when a command belongs to no workspace. Autodesk's own ribbon icons are
# near-black because the Navisworks ribbon is light on every supported release.
INK = (0x33, 0x38, 0x3D)

# One colour per ribbon panel, and the reason the tools stop looking alike.
#
# Twenty-seven near-black outline glyphs in one ribbon tab is twenty-seven of the same button: the
# eye has nothing to sort them by, so every trip to the ribbon is a read rather than a glance.
# Tinting by panel gives the row a structure that matches the grouping already printed under it.
#
# It used to tint by workspace, read out of a command's "Coordinate/Triage" route. There are no
# workspaces now - a button opens a tool, not a tab - so the grouping comes from the ribbon markup
# itself, which is where a button's panel was always actually decided.
#
# These values are the light column of Skin.cs's Fam.* rows and have to stay that way. The dark
# column is not used here: the Navisworks ribbon is light on every supported release, which is why
# there is one icon set rather than two.
# Keyed on the panel titles exactly as the markup writes them, entity and all, so the two are
# obviously the same string rather than nearly the same one.
FAMILY_INK = {
    "Project":          (0x0F, 0x76, 0x6E),
    "Clash":            (0xC2, 0x41, 0x0C),
    "Issues":           (0xC2, 0x41, 0x0C),
    "Revisions":        (0x6D, 0x46, 0xC8),
    "Data":             (0x03, 0x69, 0xA1),
    "Sets &amp; Views": (0xB4, 0x53, 0x09),
    "Deliver":          (0x4D, 0x7C, 0x0F),
    "Site":             (0x4D, 0x7C, 0x0F),
    "Automate":         (0xBE, 0x18, 0x5D),
}

# Kept for the two panes that carry no RibbonPanel of their own in the markup, and as the answer
# for anything the markup grouping cannot place.
LOOSE = {
    "FixLinks":   "Project",
    "Review":     "Coordinate",
    "Baseline":   "Compare",
    "SectionBox": "Sets &amp; Views",
    "ExportIfc":  "Project",
    "Help":       "Home",
}

SIZES = (16, 32)

# One icon per ribbon command. The command id is the file stem, so the [Command] attribute and the
# file on disk cannot drift apart without the checker noticing.
ICONS = {
    "HealthCheck":     "troubleshoot",
    "ProjectSettings": "tune",
    "FixLinks":        "link_off",
    "Help":            "help",
    "Board":           "dashboard",
    "Review":          "fact_check",
    "ClashTests":      "grid_on",
    "ClashRules":      "rule",
    "Clearance":       "height",
    "ClashReport":     "description",
    "Bcf":             "sync_alt",
    "Merge":           "merge",
    "ClashXml":        "code",
    "Compare":         "compare",
    "Changes":         "difference",
    "Baseline":        "bookmark_add",
    "ChangeNotice":    "gavel",
    "DataManager":     "table_chart",
    "Zones":           "layers",
    "Takeoff":         "calculate",
    "Wbs":             "lan",
    "Ids":             "verified",
    "Sets":            "filter_alt",
    "Appearance":      "palette",
    "Viewpoints":      "photo_camera",
    "SectionBox":      "crop_free",
    "ExportGlb":       "view_in_ar",
    "ExportData":      "table_view",
    "ExportDrawing":   "architecture",
    "ExportIfc":       "upload_file",
    "Extract":         "content_cut",
    "Compile":         "library_add",
    "SetOut":          "my_location",
    "Dimensions":      "straighten",
    "Weight":          "scale",
    "Labels":          "qr_code_2",
    "Paperwork":       "attach_file",
    "Batch":           "checklist",
    "Graph":           "account_tree",
}


# ---------------------------------------------------------------------------------------------
# SVG path parsing
# ---------------------------------------------------------------------------------------------

TOKEN = re.compile(r"[MmLlHhVvCcSsQqTtAaZz]|-?\d*\.?\d+(?:[eE][-+]?\d+)?")


def numbers(tokens, at, count):
    values = [float(tokens[at + i]) for i in range(count)]
    return values, at + count


def flatten(d):
    """A path's `d` attribute as a list of closed polygons, in user units."""
    tokens = TOKEN.findall(d)

    polygons = []
    current = []

    x = y = 0.0
    start_x = start_y = 0.0
    last_control = None
    command = None

    i = 0
    while i < len(tokens):
        token = tokens[i]

        if token.isalpha():
            command = token
            i += 1
        elif command in ("M", "m"):
            command = "L" if command == "M" else "l"
        # else: an implicit repeat of the previous command, which the branches below handle.

        relative = command.islower()
        upper = command.upper()

        if upper == "Z":
            if current:
                current.append((start_x, start_y))
                polygons.append(current)
                current = []
            x, y = start_x, start_y
            last_control = None
            continue

        if upper == "M":
            (dx, dy), i = numbers(tokens, i, 2)
            if current:
                polygons.append(current)
            x, y = (x + dx, y + dy) if relative else (dx, dy)
            start_x, start_y = x, y
            current = [(x, y)]
            last_control = None

        elif upper == "L":
            (dx, dy), i = numbers(tokens, i, 2)
            x, y = (x + dx, y + dy) if relative else (dx, dy)
            current.append((x, y))
            last_control = None

        elif upper == "H":
            (dx,), i = numbers(tokens, i, 1)
            x = x + dx if relative else dx
            current.append((x, y))
            last_control = None

        elif upper == "V":
            (dy,), i = numbers(tokens, i, 1)
            y = y + dy if relative else dy
            current.append((x, y))
            last_control = None

        elif upper in ("C", "S"):
            if upper == "C":
                (x1, y1, x2, y2, dx, dy), i = numbers(tokens, i, 6)
                if relative:
                    x1, y1, x2, y2, dx, dy = x + x1, y + y1, x + x2, y + y2, x + dx, y + dy
            else:
                (x2, y2, dx, dy), i = numbers(tokens, i, 4)
                if relative:
                    x2, y2, dx, dy = x + x2, y + y2, x + dx, y + dy
                x1, y1 = (2 * x - last_control[0], 2 * y - last_control[1]) if last_control else (x, y)

            current.extend(cubic(x, y, x1, y1, x2, y2, dx, dy))
            last_control = (x2, y2)
            x, y = dx, dy

        elif upper in ("Q", "T"):
            if upper == "Q":
                (x1, y1, dx, dy), i = numbers(tokens, i, 4)
                if relative:
                    x1, y1, dx, dy = x + x1, y + y1, x + dx, y + dy
            else:
                (dx, dy), i = numbers(tokens, i, 2)
                if relative:
                    dx, dy = x + dx, y + dy
                x1, y1 = (2 * x - last_control[0], 2 * y - last_control[1]) if last_control else (x, y)

            # A quadratic is a cubic whose controls sit two thirds of the way along each leg.
            current.extend(cubic(x, y,
                                 x + 2 / 3 * (x1 - x), y + 2 / 3 * (y1 - y),
                                 dx + 2 / 3 * (x1 - dx), dy + 2 / 3 * (y1 - dy),
                                 dx, dy))
            last_control = (x1, y1)
            x, y = dx, dy

        elif upper == "A":
            (rx, ry, rotation, large, sweep, dx, dy), i = numbers(tokens, i, 7)
            if relative:
                dx, dy = x + dx, y + dy

            current.extend(arc(x, y, rx, ry, rotation, large, sweep, dx, dy))
            last_control = None
            x, y = dx, dy

        else:
            raise ValueError("unsupported path command: " + str(command))

    if current:
        polygons.append(current)

    return polygons


def cubic(x0, y0, x1, y1, x2, y2, x3, y3, steps=24):
    points = []
    for step in range(1, steps + 1):
        t = step / steps
        u = 1 - t
        points.append((
            u * u * u * x0 + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t * t * t * x3,
            u * u * u * y0 + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t * t * t * y3,
        ))
    return points


def arc(x0, y0, rx, ry, rotation, large, sweep, x1, y1):
    """An SVG endpoint arc, sampled. Endpoint to centre parameterisation, per the SVG spec."""
    if rx == 0 or ry == 0 or (x0 == x1 and y0 == y1):
        return [(x1, y1)]

    rx, ry = abs(rx), abs(ry)
    phi = math.radians(rotation)
    cos_phi, sin_phi = math.cos(phi), math.sin(phi)

    dx2, dy2 = (x0 - x1) / 2, (y0 - y1) / 2
    x1p = cos_phi * dx2 + sin_phi * dy2
    y1p = -sin_phi * dx2 + cos_phi * dy2

    # An arc whose radii cannot span the chord is scaled up until it can, rather than dropped.
    lam = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry)
    if lam > 1:
        scale = math.sqrt(lam)
        rx, ry = rx * scale, ry * scale

    numerator = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p
    denominator = rx * rx * y1p * y1p + ry * ry * x1p * x1p
    factor = math.sqrt(max(0.0, numerator / denominator)) if denominator else 0.0
    if large == sweep:
        factor = -factor

    cxp = factor * rx * y1p / ry
    cyp = -factor * ry * x1p / rx

    cx = cos_phi * cxp - sin_phi * cyp + (x0 + x1) / 2
    cy = sin_phi * cxp + cos_phi * cyp + (y0 + y1) / 2

    def angle(ux, uy, vx, vy):
        dot = ux * vx + uy * vy
        length = math.hypot(ux, uy) * math.hypot(vx, vy)
        a = math.acos(max(-1.0, min(1.0, dot / length))) if length else 0.0
        return -a if ux * vy - uy * vx < 0 else a

    start = angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry)
    delta = angle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry)

    if not sweep and delta > 0:
        delta -= 2 * math.pi
    elif sweep and delta < 0:
        delta += 2 * math.pi

    steps = max(4, int(abs(delta) / (math.pi / 16)))
    points = []

    for step in range(1, steps + 1):
        theta = start + delta * step / steps
        px, py = rx * math.cos(theta), ry * math.sin(theta)
        points.append((cos_phi * px - sin_phi * py + cx, sin_phi * px + cos_phi * py + cy))

    return points


# ---------------------------------------------------------------------------------------------
# Rasterising
# ---------------------------------------------------------------------------------------------

def coverage(polygons, size, view, samples=4):
    """Per-pixel coverage 0..1, nonzero winding, supersampled."""
    min_x, min_y, width, height = view
    scale = size / max(width, height)

    edges = []
    for polygon in polygons:
        for i in range(len(polygon) - 1):
            (ax, ay), (bx, by) = polygon[i], polygon[i + 1]
            if ay != by:
                edges.append(((ax - min_x) * scale, (ay - min_y) * scale,
                              (bx - min_x) * scale, (by - min_y) * scale))

        # An unclosed subpath is closed implicitly; a fill has no other sensible reading.
        (ax, ay), (bx, by) = polygon[-1], polygon[0]
        if ay != by:
            edges.append(((ax - min_x) * scale, (ay - min_y) * scale,
                          (bx - min_x) * scale, (by - min_y) * scale))

    grid = [[0] * size for _ in range(size)]
    step = 1.0 / samples

    for sub in range(size * samples):
        sy = (sub + 0.5) * step
        row = sub // samples

        crossings = []
        for ax, ay, bx, by in edges:
            if (ay <= sy < by) or (by <= sy < ay):
                t = (sy - ay) / (by - ay)
                crossings.append((ax + t * (bx - ax), 1 if by > ay else -1))

        if not crossings:
            continue

        crossings.sort()

        winding = 0
        spans = []
        for i, (cx, direction) in enumerate(crossings):
            before = winding
            winding += direction
            if before == 0 and winding != 0:
                spans.append([cx, None])
            elif before != 0 and winding == 0 and spans:
                spans[-1][1] = cx

        for left, right in spans:
            if right is None:
                continue
            for sx in range(int(left * samples), int(right * samples) + 1):
                px = sx // samples
                if 0 <= px < size and left <= (sx + 0.5) * step <= right:
                    grid[row][px] += 1

    total = samples * samples
    return [[min(1.0, cell / total) for cell in row] for row in grid]


def png(cover, size, ink):
    raw = bytearray()
    for row in cover:
        raw.append(0)  # filter: none. These are tiny; a predictor would save almost nothing.
        for alpha in row:
            a = int(round(alpha * 255))
            raw.extend((ink[0], ink[1], ink[2], a))

    def chunk(tag, payload):
        return (struct.pack(">I", len(payload)) + tag + payload
                + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + chunk(b"IEND", b""))


def render(svg, size, ink=INK):
    view = [float(v) for v in re.search(r'viewBox="([^"]+)"', svg).group(1).split()]

    polygons = []
    for d in re.findall(r'<path[^>]*\sd="([^"]+)"', svg):
        polygons.extend(flatten(d))

    if not polygons:
        raise ValueError("the SVG has no path data")

    return png(coverage(polygons, size, view), size, ink)


# ---------------------------------------------------------------------------------------------

def commands():
    """The command ids the ribbon declares, so the icon set can be checked against them."""
    source = (ROOT / "CamelWorks.UI" / "CamelWorksPlugin.cs").read_text(encoding="utf-8")
    return sorted(set(re.findall(r'\[Command\("ID_CW_(\w+)"', source)))


def families():
    """Which ribbon panel each command sits in, read from the markup rather than restated.

    The markup already groups the buttons under a titled RibbonPanelSource, and that grouping is
    the same fact a colour is about to encode. Reading it means a button moved from one panel to
    another changes colour by itself, instead of keeping the old one until somebody notices.
    """
    source = (ROOT / "CamelWorks.UI" / "CamelWorks.xaml").read_text(encoding="utf-8")

    found = dict(LOOSE)
    panel = None

    for line in source.splitlines():
        title = re.search(r'<RibbonPanelSource[^>]*\sTitle="([^"]+)"', line)

        if title:
            panel = title.group(1)
            continue

        button = re.search(r'Id="ID_CW_(\w+)"', line)

        if button and panel:
            found[button.group(1)] = panel

    return found


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true", help="verify, fetch nothing")
    args = parser.parse_args()

    declared = commands()
    missing = [c for c in declared if c not in ICONS]
    extra = [c for c in ICONS if c not in declared]

    problems = 0

    if missing:
        print("FAIL: no icon named for: " + ", ".join(missing), file=sys.stderr)
        problems = 1

    if extra:
        print("FAIL: icons named for commands that do not exist: " + ", ".join(extra), file=sys.stderr)
        problems = 1

    homes = families()
    homeless = [c for c in declared if c not in homes]

    if homeless:
        print("FAIL: no ribbon panel known for: " + ", ".join(homeless), file=sys.stderr)
        problems = 1

    strange = sorted({f for f in homes.values() if f not in FAMILY_INK})

    if strange:
        print("FAIL: no colour for ribbon panel: " + ", ".join(strange), file=sys.stderr)
        problems = 1

    if args.check:
        for command in declared:
            for size in SIZES:
                path = OUT / f"{command}_{size}.png"
                if not path.exists():
                    print("FAIL: missing " + str(path.relative_to(ROOT)), file=sys.stderr)
                    problems = 1

        if problems:
            return 1

        print(f"icons complete: {len(declared)} commands x {len(SIZES)} sizes, "
              f"{len(set(homes[c] for c in declared))} panel colours")
        return 0

    if problems:
        return 1

    OUT.mkdir(parents=True, exist_ok=True)

    for command, symbol in sorted(ICONS.items()):
        url = SOURCE.format(name=symbol)

        try:
            with urllib.request.urlopen(url, timeout=30) as response:
                svg = response.read().decode("utf-8")
        except Exception as error:  # noqa: BLE001 - the name is what matters in the message
            print(f"FAIL: {command} -> {symbol}: {error}", file=sys.stderr)
            problems = 1
            continue

        home = homes.get(command)
        ink = FAMILY_INK.get(home, INK)

        for size in SIZES:
            (OUT / f"{command}_{size}.png").write_bytes(render(svg, size, ink))

        print(f"  {command:<15} {symbol:<16} {home or '-'}")

    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
