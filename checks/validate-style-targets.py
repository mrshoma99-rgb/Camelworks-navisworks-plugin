"""Fail when a control is dressed in a style meant for a different kind of control.

The workspace switcher was a row of toggles, and its style, RailBtn, targets
ToggleButton. The switcher went; one caller did not. `_back` is a plain Button,
and WPF will not apply a style whose TargetType is *derived* from the element's
own type - so the moment the dock pane laid itself out it threw

    'ToggleButton' TargetType does not match type of element 'Button'

and took the whole pane with it. Six tools - Triage, Changes, Data, Sets,
Appearance and the graph - all opened on that one sentence, in a shipped build.

Nothing caught it. The skin check asks whether a key exists, which RailBtn does,
and the compiler has no opinion about a string. The rule it was missing is the
one WPF enforces at run time: the element's type must BE the TargetType, or be
derived from it. Button wearing a ToggleButton style is the inversion, and it is
always fatal.

Deliberately narrow, because guessing at a variable's type is how a check starts
crying wolf:

  * only `<name>.Wearing("Key")` where the key is a style the sheet declares
    with a TargetType;
  * only where `<name>`'s type can be read off the nearest `new T` above the
    call, in the same file;
  * only where both types are in the small hierarchy below, so "derived from"
    is a fact rather than an assumption.

Everything else is left alone.
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHEET = os.path.join(ROOT, "CamelWorks.UI", "Themes", "SkinSheet.xaml")

# Just enough of the WPF tree to decide "is A a B". Each entry names its base.
BASE = {
    "ToggleButton": "Button",
    "CheckBox": "ToggleButton",
    "RadioButton": "ToggleButton",
    "Button": "ButtonBase",
    "ButtonBase": "Control",
    "ComboBox": "Control",
    "TextBox": "Control",
    "ListView": "Control",
    "ListBox": "Control",
    "TabControl": "Control",
    "Expander": "Control",
    "Control": "FrameworkElement",
    "Border": "FrameworkElement",
    "TextBlock": "FrameworkElement",
    "StackPanel": "Panel",
    "Panel": "FrameworkElement",
}

STYLE = re.compile(r'<Style\s+x:Key="([^"]+)"\s+TargetType="(?:\{x:Type\s+)?(\w+)')
WEARS = re.compile(r'\b(\w+)\.Wearing\(\s*"([^"]+)"\s*\)')


def wears(name, kind):
    """Whether an element of this type may wear a style targeting that one."""
    seen = name
    while seen:
        if seen == kind:
            return True
        seen = BASE.get(seen)
    return False


def declared(src, name, before):
    """The type `name` is constructed as nearest above `before`, or None.

    Nearest, not first. One file declares `var button = new Button(...)` in one
    helper and `var button = new ToggleButton(...)` in the next, and taking the
    first match in the file reports the second helper as wearing the wrong style.
    """
    best = None

    for pattern in (r"\b" + re.escape(name) + r"\s*=\s*new\s+(?:[\w.]*\.)?(\w+)",
                    r"\b(?:[\w.]*\.)?(\w+)\??\s+" + re.escape(name) + r"\s*=\s*new\b"):
        for hit in re.finditer(pattern, src):
            if hit.start() < before and (best is None or hit.start() > best[0]):
                best = (hit.start(), hit.group(1))

    return best[1] if best else None


def main():
    if not os.path.isfile(SHEET):
        print("cannot find the skin sheet", file=sys.stderr)
        return 1

    targets = dict(STYLE.findall(open(SHEET, encoding="utf-8").read()))

    if len(targets) < 10:
        print("only %d keyed styles parsed - the sheet's shape has changed and this check "
              "would pass by not looking." % len(targets))
        return 1

    findings = []

    for area in ("CamelWorks.UI", "CamelWorks.Studio"):
        for folder, _, names in os.walk(os.path.join(ROOT, area)):
            for name in names:
                if not name.endswith(".cs"):
                    continue

                path = os.path.join(folder, name)
                src = open(path, encoding="utf-8").read()

                for hit in WEARS.finditer(src):
                    who, key = hit.group(1), hit.group(2)
                    kind = targets.get(key)

                    if kind is None or kind not in BASE:
                        continue

                    is_a = declared(src, who, hit.start())

                    if is_a is None or is_a not in BASE:
                        continue

                    if not wears(is_a, kind):
                        findings.append((path, src.count("\n", 0, hit.start()) + 1,
                                         '%s is a %s and "%s" targets %s - WPF throws on that'
                                         % (who, is_a, key, kind)))

    for path, line, message in sorted(findings):
        print("%s:%d  %s" % (os.path.relpath(path, ROOT), line, message))

    if findings:
        print("\n%d control(s) dressed in a style for something else." % len(findings))
        return 1

    print("Every styled control matches the style's TargetType (%d keyed styles)." % len(targets))
    return 0


if __name__ == "__main__":
    sys.exit(main())
