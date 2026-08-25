#!/usr/bin/env python3
"""Hold every `Ui.` call site to the signature `Ui.cs` actually declares.

The widget vocabulary is a closed set - sixty-odd helpers that the whole
interface is written in - and the mistakes it invites are always the same two.
An argument lands in the wrong slot, because two overloads differ by one
parameter in the middle:

    Ui.Text("", "no limit", 90)          // the placeholder went where the
                                         // handler was meant to go

Or the count is simply wrong, because a helper grew a parameter and one call
site did not hear about it. Both are CS1503 or CS7036, both are certain, and
both are currently found by a Windows runner several minutes after a push -
which is the wrong place to find something a hundred lines of Python can see.

The rules are deliberately few, and each only fires when the answer is not a
matter of inference:

  * arity, counting optional parameters and `params` arrays, across every
    overload - a call is wrong only when no overload could take it;
  * a literal in a slot that cannot hold one - a string where a delegate goes,
    a number where a string goes, `null` where the parameter is not nullable -
    again only when every overload rejects it.

Everything else is left alone. An identifier, a member access, a nested call:
this does not know their types and does not guess. Which is the point - the
compiler is thorough and slow, and this is neither, so it may only report what
it is sure of.

Three things had to be got right before the false positives stopped:

  * comments are blanked first, because an apostrophe in a sentence reads as a
    character literal to a scanner and swallows the rest of the method;
  * a lambda counts only when its arrow is at the top level of the argument,
    or every `Ui.Field(label, Ui.Text(value, t => ...))` in the repository
    looks like a delegate handed to a control slot;
  * named arguments bind by name, because `Ui.Runner(caption, panel, work: ...)`
    puts the third argument in the fourth parameter's place.
"""
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UI = os.path.join(ROOT, "CamelWorks.UI", "Views", "Ui.cs")

# `public static Border Card(` - a method, not the `public static class Ui` line.
METHOD = re.compile(r"\b(?:public|internal)\s+static\s+(?!class\b)(?:readonly\s+)?"
                    r"([A-Za-z_][\w.<>?\[\],\s]*?)\s+([A-Z]\w*)\s*\(")

# `public Step(` inside a nested class.
CTOR = re.compile(r"\bpublic\s+([A-Z]\w*)\s*\(")
NESTED = re.compile(r"\bpublic\s+(?:sealed\s+)?class\s+([A-Z]\w*)")


def scrub(src):
    """Blank every comment, keeping the length so line numbers still hold."""
    out, i, n = [], 0, len(src)
    while i < n:
        c = src[i]
        if c == '"':
            verbatim = src[i - 1:i] == '@'
            j = i + 1
            while j < n:
                if not verbatim and src[j] == '\\':
                    j += 2
                    continue
                if src[j] == '"':
                    if verbatim and src[j + 1:j + 2] == '"':
                        j += 2
                        continue
                    break
                j += 1
            out.append(src[i:j + 1])
            i = j + 1
            continue
        if c == "'":
            j = i + 1
            if src[j:j + 1] == '\\':
                j += 1
            j += 1
            if src[j:j + 1] == "'":
                out.append(src[i:j + 1])
                i = j + 1
                continue
            out.append(c)
            i += 1
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
        if c == '"':
            j = i + 1
            while j < n:
                if text[j] == '\\':
                    j += 2
                    continue
                if text[j] == '"':
                    break
                j += 1
            i = j + 1
            continue
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
    out, depth, cur, i, n = [], 0, [], 0, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            j = i + 1
            while j < n:
                if text[j] == '\\':
                    j += 2
                    continue
                if text[j] == '"':
                    break
                j += 1
            cur.append(text[i:j + 1])
            i = j + 1
            continue
        if c in '([{':
            depth += 1
        elif c in ')]}':
            depth -= 1
        if c == ',' and depth == 0:
            out.append(''.join(cur).strip())
            cur = []
            i += 1
            continue
        cur.append(c)
        i += 1
    tail = ''.join(cur).strip()
    if tail or out:
        out.append(tail)
    return out


def parameters(text):
    """(type, name, optional, is_params) for each declared parameter."""
    out = []
    for part in split_top(text):
        part = part.strip()
        if not part:
            continue
        variadic = part.startswith("params ")
        if variadic:
            part = part[7:].strip()
        optional = False
        if '=' in part:
            head, _, _ = part.partition('=')
            if not head.rstrip().endswith(('=', '<', '>', '!')):
                part, optional = head.strip(), True
        part = re.sub(r"\b(this|ref|out|in)\s+", "", part)
        named = re.match(r"^(.*?)\s+([A-Za-z_]\w*)$", part, re.S)
        if named:
            out.append((named.group(1).strip(), named.group(2),
                        optional or variadic, variadic))
    return out


def declared():
    """Every overload of every helper, and the constructors of the nested types."""
    src = scrub(open(UI, encoding="utf-8").read())
    methods, types = {}, {}
    for hit in METHOD.finditer(src):
        args, _ = spans(src, hit.end() - 1)
        if args is not None:
            methods.setdefault(hit.group(2), []).append(parameters(args))
    for hit in NESTED.finditer(src):
        name = hit.group(1)
        args, past = spans(src, src.index('{', hit.end()))
        for ctor in CTOR.finditer(args or ""):
            if ctor.group(1) != name:
                continue
            shape, _ = spans(args, ctor.end() - 1)
            if shape is not None:
                types.setdefault(name, []).append(parameters(shape))
    return methods, types


ELEMENTS = ("UIElement", "Panel", "StackPanel", "TextBox", "Border", "ListView",
            "ComboBox", "CheckBox", "FrameworkElement", "ScrollViewer", "TextBlock")


def wants(kind):
    """What a parameter type will hold, or None when this has no opinion."""
    kind = kind.strip()
    if kind in ("string", "string?"):
        return "text"
    if kind in ("double", "int", "double?", "int?"):
        return "number"
    if kind in ("bool", "bool?"):
        return "flag"
    if kind.startswith("Action") or kind.startswith("Func"):
        return "handler"
    if kind.rstrip('?') in ELEMENTS:
        return "control"
    return None


def bare(text):
    """The expression with its brackets emptied, so only the top level is left."""
    out, depth, i, n = [], 0, 0, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            j = i + 1
            while j < n:
                if text[j] == '\\':
                    j += 2
                    continue
                if text[j] == '"':
                    break
                j += 1
            if depth == 0:
                out.append('""')
            i = j + 1
            continue
        if c in '([{':
            depth += 1
            i += 1
            continue
        if c in ')]}':
            depth -= 1
            i += 1
            continue
        if depth == 0:
            out.append(c)
        i += 1
    return ''.join(out)


def given(text):
    """What an argument plainly is, or None when it takes a compiler to say."""
    text = text.strip()
    if not text:
        return None
    if text == "null":
        return "nothing"
    if text in ("true", "false"):
        return "flag"
    if "=>" in bare(text):
        return "handler"
    if re.match(r'^\s*\$?@?"', text):
        return "text"
    if re.match(r"^-?\d+(\.\d+)?[dfm]?$", text):
        return "number"
    return None


def fault(shape, args):
    """The first argument this overload cannot take, or None."""
    for index, arg in enumerate(args):
        named = re.match(r"^([A-Za-z_]\w*)\s*:(?!:)\s*(.*)$", arg, re.S)
        if named:
            slot = [p for p in shape if p[1] == named.group(1)]
            if not slot:
                continue
            kind, name, _, variadic = slot[0]
            arg = named.group(2)
        else:
            if index >= len(shape):
                break
            kind, name, _, variadic = shape[index]
        if variadic:
            break
        holds, is_ = wants(kind), given(arg)
        if holds is None or is_ is None:
            continue
        if is_ == "nothing":
            if not kind.strip().endswith("?"):
                return "%s (%s) will not take null" % (name, kind)
            continue
        if holds != is_:
            return "%s wants %s, given %s: %s" % (name, kind, is_, arg.strip()[:44])
    return None


def visit(path, shapes, prefix, findings):
    src = scrub(open(path, encoding="utf-8").read())
    for hit in re.finditer(prefix, src):
        name = hit.group(1)
        if name not in shapes:
            continue
        args, _ = spans(src, hit.end() - 1)
        if args is None:
            continue
        # A generic argument list has commas this cannot tell from the real ones.
        probe = bare(args).replace("=>", "").replace("<=", "").replace(">=", "")
        if "<" in probe:
            continue
        given_args = [a for a in split_top(args) if a != ""] if args.strip() else []
        line = src.count("\n", 0, hit.start()) + 1
        fits = []
        for shape in shapes[name]:
            least = sum(1 for _, _, optional, _ in shape if not optional)
            most = 10 ** 6 if any(v for *_, v in shape) else len(shape)
            if least <= len(given_args) <= most:
                fits.append(shape)
        if not fits:
            counts = " or ".join(sorted({str(len(s)) for s in shapes[name]}))
            findings.append((path, line, "%s takes %s arguments, given %d"
                             % (name, counts, len(given_args))))
            continue
        refusals = [fault(shape, given_args) for shape in fits]
        if all(r is not None for r in refusals):
            findings.append((path, line, "%s: %s" % (name, refusals[0])))


def main():
    methods, types = declared()
    if len(methods) < 40:
        print("validate-ui-calls: only %d helpers parsed out of Ui.cs - the shape of the "
              "file has changed and this check would pass by not looking." % len(methods))
        return 1
    findings = []
    for area in ("CamelWorks.UI", "CamelWorks.Studio"):
        for folder, _, names in os.walk(os.path.join(ROOT, area)):
            for name in names:
                if not name.endswith(".cs"):
                    continue
                path = os.path.join(folder, name)
                visit(path, methods, r"\bUi\.([A-Z]\w*)\s*\(", findings)
                visit(path, types, r"\bnew\s+Ui\.([A-Z]\w*)\s*\(", findings)
    for path, line, message in sorted(findings):
        print("%s:%d  %s" % (os.path.relpath(path, ROOT), line, message))
    if findings:
        print("\n%d call site(s) the widget vocabulary will not accept." % len(findings))
        return 1
    print("Every Ui. call site fits a declared signature (%d helpers, %d nested types)."
          % (len(methods), len(types)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
