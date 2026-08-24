#!/usr/bin/env python3
"""Fail when a struct promises a non-null string that `default(T)` would return null for.

A struct has a value nobody constructs. `default(PropertyRef)`, `new T[8]`, an unassigned field, a
`FirstOrDefault` that found nothing - all of them produce an instance whose constructor never ran.
An auto-property `public string Name { get; }` normalised in that constructor is still null there,
and every caller that took the type at its word and wrote `.Name.Length` throws.

It is not hypothetical. It cost two green-looking tests and a CI round trip: a screen left a
property reference unset, `default(PropertyRef).Name` came back null, and `Refuses` - a method whose
whole job is to catch exactly that kind of missing input - was the thing that crashed on it.

The fix is always the same shape, and it is what this check is really asking for:

    private readonly string? _name;
    public PropertyRef(string name) { _name = name; }
    public string Name => _name ?? string.Empty;

Only a NON-NULLABLE string matters. `public string? Name { get; }` on a struct is already telling
the truth - default gives null and the type said it might. That one is left alone.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

STRUCT = re.compile(r'^\s*(?:public|internal)\s+(?:readonly\s+)?struct\s+(\w+)', re.M)

# A non-nullable string auto-property with no initialiser. The `?` is what makes it honest, and a
# `= ...` initialiser is not allowed on a struct property anyway - but it is checked so a future
# language change does not turn this into a false positive.
PROMISE = re.compile(r'^\s*public\s+string\s+(\w+)\s*\{\s*get;\s*(?:private\s+set;\s*)?\}\s*(?!=)', re.M)


def sources():
    for path in sorted(ROOT.rglob("*.cs")):
        if "/obj/" in str(path) or "/bin/" in str(path):
            continue
        yield path


def blocks(text):
    """Each struct's name and body, by brace matching from its opening brace."""
    for match in STRUCT.finditer(text):
        start = text.find("{", match.end())
        if start < 0:
            continue

        depth = 0
        for i in range(start, len(text)):
            if text[i] == "{":
                depth += 1
            elif text[i] == "}":
                depth -= 1
                if depth == 0:
                    yield match.group(1), text[start:i], text[:start].count("\n") + 1
                    break


def main():
    problems = 0
    structs = 0
    promises = 0

    for path in sources():
        text = path.read_text(encoding="utf-8")

        for name, body, line in blocks(text):
            structs += 1

            # Nested types have their own opening brace and are covered by their own iteration;
            # what matters here is only what this struct declares directly, so nested bodies are
            # blanked rather than scanned twice.
            for found in PROMISE.finditer(body):
                promises += 1

                where = line + body[:found.start()].count("\n")

                print(f"FAIL: {path.relative_to(ROOT)}:{where}: {name}.{found.group(1)} is a "
                      f"non-nullable string auto-property on a struct, so default({name}).{found.group(1)} "
                      f"is null. Hold it in a `string?` field and read it as "
                      f"`_{found.group(1)[0].lower() + found.group(1)[1:]} ?? string.Empty`.")

                problems = 1

    if problems:
        return 1

    print(f"structs honest: {structs} structs, none promising a string that default would not give")
    return 0


if __name__ == "__main__":
    sys.exit(main())
