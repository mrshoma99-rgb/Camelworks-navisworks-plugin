#!/usr/bin/env python3
"""Fail when a file uses a type this repository declares without importing where it lives.

There is no C# compiler on the machine these checks run on, so a missing `using` is invisible here
and costs a CI round trip. It is also the most mechanical mistake there is: write a test against a
type, forget that its namespace is not the one the rest of the file uses, push.

The rule only knows about types THIS repository declares. That is what keeps it honest — it has a
real index to check against rather than a guess, and it says nothing at all about the BCL, WPF or
the Navisworks API, where a wrong answer would be noise.

Two things stop it crying wolf, both learned from a first draft that produced fifty-three false
positives on a clean tree:

It also knows one fixed table of framework types - the WPF names this repository actually uses -
because CamelWorks.UI compiles only on a Windows runner and a missing `using System.Windows.Controls`
is otherwise invisible until CI says so.

  * Only type POSITIONS count - `new X`, `X.Member`, `<X>`, `(X)`, `is X`, `as X`, `typeof(X)`,
    and a declaration `X name,` / `X name)` / `X name =` / `X name;`.
    A property or a string that happens to share a name with a type is not a use of it.
  * A name the file itself declares as a member wins. Ui.Finding is a method, and it has nothing to
    do with the Finding type over in Core.Findings.

Doc comments and string literals are stripped before any of it, because prose mentioning a type is
not code using one.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

DECLARES = re.compile(r'^\s*(?:public|internal)\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+)*'
                      r'(?:class|struct|enum|interface|record)\s+(\w+)', re.M)

NAMESPACE = re.compile(r'^\s*namespace\s+([\w.]+)', re.M)
USING = re.compile(r'^\s*using\s+(?:\w+\s*=\s*)?([\w.]+)\s*;', re.M)
MEMBER = re.compile(r'(?:public|private|internal|protected)[\w\s<>,?\[\]]*?\b(\w+)\s*(?:\(|=>|\{\s*get)')

# Somewhere a name can only be a type.
POSITIONS = [
    re.compile(r'\bnew\s+([A-Z]\w+)'),
    re.compile(r'<\s*([A-Z]\w+)[\s,>]'),
    re.compile(r'\(\s*([A-Z]\w+)\s*\)'),
    re.compile(r'\bis\s+([A-Z]\w+)\b'),
    re.compile(r'\bas\s+([A-Z]\w+)\b'),
    re.compile(r'\btypeof\(\s*([A-Z]\w+)\s*\)'),
    # Not preceded by a dot: `Colour.Parse(...)` names a type, `book.Sheets.Count` names a
    # property on an instance that happens to share a name with one. The second cost a false
    # positive on a screen whose workbook has a Sheets property and whose repo has a Sheets class.
    re.compile(r'(?<![.\w])([A-Z]\w+)\.\w+'),

    # A declaration: a type, then a name, then the end of it. Added after a parameter typed
    # `IFileSystem? fs = null` slipped through every position above and cost a CI round trip -
    # the type never appeared in any of them, because the file only ever named it there.
    #
    # What stops it matching a ternary: `Colour? c` has no space before the question mark, and a
    # ternary written `Colour ? c : d` does. What stops it matching two ordinary words: a
    # declaration is followed by one of `, ) ; = {`, and prose is not - a ternary's `: ` is the
    # give-away, which is why the closing set does not contain a colon.
    re.compile(r'\b([A-Z]\w+)\??\s+\w+\s*(?=[,)=;])'),

    # The same declaration, generic. `IReadOnlyList<object> picked,` is invisible to the position
    # above, because that one wants the identifier straight after the type name and this has an
    # argument list in between - and invisible to the `<X>` position, which reads the argument
    # rather than the type. A signature taking IReadOnlyList<object> went to CI without
    # System.Collections.Generic and came back a compile error with both patterns watching it.
    re.compile(r'\b([A-Z]\w+)<[\w\s,<>?\[\]]+>\??\s+\w+\s*(?=[,)=;{])'),
]

# Already written out in full, so no using is needed for it.
QUALIFIED = re.compile(r'\b[\w.]+\.([A-Z]\w+)\b')


# The framework types this repository actually uses, and where they live.
#
# The rule above only knows types THIS repository declares, which is what keeps it honest - it has
# a real index rather than a guess. That leaves one hole big enough to matter: CamelWorks.UI can
# only be compiled on a Windows runner, so a WPF type used without its namespace is invisible here
# and costs the full round trip. It cost exactly that for `Panel` on a screen whose file imported
# System.Windows but not System.Windows.Controls.
#
# So: a small, fixed table. A name that lives in more than one namespace is listed under every one
# of them - importing any of its homes satisfies it, which is what the compiler thinks too, and
# `MessageBox` is in both System.Windows and System.Windows.Forms. A name whose home is not certain
# is left out entirely: `Path` is in System.IO and System.Windows.Shapes, `Image`, `Style`,
# `Binding` and `Run` are each in two places, and a table that guessed would be worse than none.
FRAMEWORK = {
    # The generic collections. Added after a signature taking IReadOnlyList<object> went to CI
    # without System.Collections.Generic and came back a compile error - the same hole the WPF
    # names below were added to close, and the same round trip.
    #
    # Stack and Queue are deliberately absent: Ui.Stack is a method on the vocabulary and a bare
    # Stack in a UI file is far more likely to be that than a collection.
    "System.Collections.Generic": [
        "IReadOnlyList", "IReadOnlyDictionary", "IReadOnlyCollection", "IEnumerable",
        "IEnumerator", "ICollection", "IList", "IDictionary", "ISet",
        "List", "Dictionary", "HashSet", "SortedDictionary", "SortedSet", "LinkedList",
        "KeyValuePair", "IEqualityComparer", "IComparer", "EqualityComparer", "Comparer",
    ],
    "System.Windows": [
        "Window", "Thickness", "Visibility", "GridLength", "GridUnitType", "FrameworkElement",
        "UIElement", "DependencyProperty", "HorizontalAlignment", "VerticalAlignment",
        "TextWrapping", "TextAlignment", "RoutedEventArgs", "MessageBox", "SizeToContent",
        "WindowStartupLocation", "WindowState", "ResizeMode", "FontWeights", "FontStyles",
    ],
    "System.Windows.Controls": [
        "Panel", "StackPanel", "Grid", "Border", "TextBox", "TextBlock", "ComboBox",
        "ComboBoxItem", "Button", "CheckBox", "ListView", "ListViewItem", "GridView",
        "GridViewColumn", "ScrollViewer", "Viewbox", "Canvas", "ColumnDefinition",
        "RowDefinition", "Orientation", "SelectionMode", "ProgressBar", "Separator",
        "RadioButton", "TabControl", "TabItem", "Expander", "ItemsControl", "DockPanel",
        "WrapPanel", "ScrollBarVisibility",
    ],
    "System.Windows.Media": [
        "Brush", "SolidColorBrush", "Colors", "Brushes", "FontFamily", "PathGeometry",
        "ScaleTransform", "TranslateTransform", "DrawingImage", "Stretch", "GeometryDrawing",
    ],
    "System.Windows.Input": ["KeyEventArgs", "MouseButtonEventArgs", "Cursors"],
    "System.Windows.Shapes": ["Rectangle", "Ellipse", "Polygon", "Polyline"],
    "System.Windows.Threading": ["DispatcherTimer", "DispatcherPriority"],
    "Microsoft.Win32": ["OpenFileDialog", "SaveFileDialog"],

    # The ribbon plug-in talks to the host through Windows Forms, so this name has two homes and
    # both of them count.
    "System.Windows.Forms": ["MessageBox"],
}


def sources():
    for path in sorted(ROOT.rglob("*.cs")):
        if "/obj/" in str(path) or "/bin/" in str(path):
            continue
        yield path


def code(text):
    """The file with its usings, doc comments and string literals taken out."""
    text = re.sub(r'^\s*using[^\n]*\n', '', text, flags=re.M)
    text = re.sub(r'///[^\n]*', '', text)
    return re.sub(r'"(?:[^"\\]|\\.)*"', '""', text)


def main():
    home = {}

    for path in sources():
        text = path.read_text(encoding="utf-8")
        namespace = NAMESPACE.search(text)
        if not namespace:
            continue

        for name in DECLARES.findall(text):
            home.setdefault(name, set()).add(namespace.group(1))

    for namespace, names in FRAMEWORK.items():
        for name in names:
            home.setdefault(name, set()).add(namespace)

    problems = 0
    checked = 0

    for path in sources():
        text = path.read_text(encoding="utf-8")
        namespace = NAMESPACE.search(text)
        if not namespace:
            continue

        checked += 1
        mine = namespace.group(1)

        # A file sees what it imports, its own namespace, and every ancestor of it.
        visible = set(USING.findall(text)) | {mine}
        visible |= {mine.rsplit(".", n)[0] for n in range(1, mine.count(".") + 1)}

        body = code(text)
        declared_here = set(MEMBER.findall(body))

        # Names in type position, gathered once per file rather than by trying every known type
        # against every file - which is 219 x 506 regex searches and takes minutes.
        used = set()
        for pattern in POSITIONS:
            used.update(pattern.findall(body))

        qualified = set(QUALIFIED.findall(body))

        for name in sorted(used):
            namespaces = home.get(name)

            if namespaces is None or namespaces & visible:
                continue

            if name in declared_here or name in qualified:
                continue

            line = next((n for n, text in enumerate(body.splitlines(), 1)
                         if re.search(r'\b' + re.escape(name) + r'\b', text)), 0)

            print(f"FAIL: {path.relative_to(ROOT)}:{line}: {name} is used but "
                  f"{' or '.join(sorted(namespaces))} is not imported")
            problems = 1

    if problems:
        return 1

    print(f"usings resolve: {checked} files against {len(home)} declared types")
    return 0


if __name__ == "__main__":
    sys.exit(main())
