#!/usr/bin/env python3
"""Fail when a match on a non-unique key claims nothing.

The same defect has been written three times. Two candidates match one held record, nothing
claims the one that matched, and which candidate wins is whatever order a dictionary
enumerated in:

  * ClashExchange.Apply - two returned rows reaching one finding through a neighbouring cell.
  * CheckDelta.Between - two findings offering the same alias, so a moved model reported
    fewer gone than it had.
  * FileCheck.Fold and FileCheck.Adopt - two clash tests reporting one physical conflict,
    which is two findings with different ids and one topic guid. One approval became two
    approved rows, and in the other direction the second finding was dropped from the read.

The keys this goes wrong on are the ones the product knows are not unique: a topic guid,
derived from the element pair and the cell they meet in and not from the run, and the alias
ids a check offers for a finding. Matching on a host result id or a finding id is a different
thing and is not looked at here.

The rule: a loop over candidates that looks a held record up by a topic guid or an alias must
hold a claimed set, and must order its candidates so the same two answer the same way twice.

The same rule covers the other half of the defect, which is where it is written rather than
where it is read: an index built inside a loop as index[key] = value, keyed on a topic guid or
an alias, silently keeps the last writer. FindingBcf.Merge built the register's answer index
that way, so of two findings on one topic guid only one could ever be answered.

What the check keys on is the topic guid or alias in the lookup argument or in the subscript
being assigned to. Anything looser flags every index in the product. Two limits follow, and
both are the reason the two clash folds now share one Claims index rather than each rolling
their own:

  * A lookup or a build keyed on a value that is non-unique for a reason nothing in the name
    says is not seen at all.
  * Whether a match is consumed - the record removed, its decisions taken onto something else
    - cannot be read off the text, so a read-only match is flagged like a consuming one. Those
    go in checks/claim-once-allowed.txt with the reason they may be made twice. An entry there
    may name which half it allows - "path::method#lookup" or "path::method#build" - and a bare
    entry allows both. A method whose lookup is allowed does not thereby get to build an index
    the same way.

    python3 checks/validate-claim-once.py
"""

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
LOOKED_AT = [ROOT / "CamelWorks.Core", ROOT / "CamelWorks.UI", ROOT / "CamelWorks.Nav"]

ALLOWED = pathlib.Path(__file__).resolve().parent / "claim-once-allowed.txt"

# A held record read out of an index or a journal, under a key this product declares
# non-unique: a topic guid, derived from the element pair and the cell rather than from the
# run, or one of the alias ids a check offers for a finding. The key has to be in the lookup
# itself. A dictionary BUILT under a topic guid inside a loop is not a match and is not this
# check's business, and telling the two apart by anything looser than the argument would flag
# every index in the product.
LOOKUP = re.compile(
    r"\.(?:TryGetValue|ContainsKey|Holds|Contains|Find)\(\s*[A-Za-z0-9_.!\[\]]*"
    r"(?:[Tt]opic|[Aa]lias)[A-Za-z0-9_.!\[\]]*\s*[,)]")

# An index built under a topic guid or an alias inside a loop: index[thatKey] = value. Last
# writer wins and nothing says which writer that was, which is the same defect as an
# unclaimed lookup with the read and the write swapped.
#
# Two ways the key is named, because both are how it is written: the subscript says what it
# holds (byWhat[topic.Guid]), or the index does (byTopic[guid]). Anything looser - every
# assignment into every dictionary - flags each index in the product. A compound assignment
# (==, +=, =>) is not a build.
BUILD = re.compile(
    r"\b(?:[A-Za-z_][A-Za-z0-9_]*\s*\[\s*[A-Za-z0-9_.!()\[\]]*"
    r"(?:[Tt]opic|[Aa]lias)[A-Za-z0-9_.!()\[\]]*\s*\]"
    r"|[A-Za-z_][A-Za-z0-9_]*(?:[Tt]opic|[Aa]lias)[A-Za-z0-9_]*"
    r"\s*\[[^\[\]\n]*\])\s*=(?![=>])")

# What claiming looks like: a set of what has been taken, or an index that hands each held
# record out once. Removing the record from a register does not count - the index the lookup
# reads is a copy, and the old Adopt removed the finding it matched and still handed the same
# one to the next candidate.
CLAIMED = re.compile(r"\bclaim(?:ed)?\b|\bClaim\(|\bClaims\(")

# What ordering the candidates looks like.
ORDERED = re.compile(r"\bOrderBy\(|\bOrdered\(|\.Sort\(|\bOrderByDescending\(")

STATEMENT = re.compile(
    r"\b(?:if|else|for|foreach|while|do|switch|case|using|lock|try|catch|finally|fixed|"
    r"unchecked|checked|get|set|add|remove)\b\s*$")

METHOD = re.compile(r"\)\s*(?::\s*this\([^)]*\)\s*)?$")


def masked(source):
    """The source with comments and string bodies blanked, so neither counts as code."""
    out = []
    at = 0
    length = len(source)

    while at < length:
        two = source[at:at + 2]

        if two == "//":
            end = source.find("\n", at)
            end = length if end < 0 else end
            out.append(" " * (end - at))
            at = end
        elif two == "/*":
            end = source.find("*/", at + 2)
            end = length if end < 0 else end + 2
            out.append("".join(c if c == "\n" else " " for c in source[at:end]))
            at = end
        elif source[at] in "\"'":
            quote = source[at]
            verbatim = at > 0 and source[at - 1] == "@" and quote == "\""
            end = at + 1
            while end < length:
                if not verbatim and source[end] == "\\":
                    end += 2
                    continue
                if source[end] == quote:
                    if verbatim and source[end:end + 2] == quote * 2:
                        end += 2
                        continue
                    end += 1
                    break
                if not verbatim and source[end] == "\n":
                    break
                end += 1
            out.append("".join(c if c == "\n" else " " for c in source[at:end]))
            at = end
        else:
            out.append(source[at])
            at += 1

    return "".join(out)


def blocks(source):
    """Every brace pair in the file, as (open, close) offsets."""
    seen = []
    stack = []

    for at, character in enumerate(source):
        if character == "{":
            stack.append(at)
        elif character == "}" and stack:
            seen.append((stack.pop(), at))

    return seen


def loops(blank):
    """Every foreach body in the file, as (head start, open, close)."""
    found = []
    pairs = blocks(blank)

    for hit in re.finditer(r"\bforeach\s*\(", blank):
        after = blank.find(")", hit.end())

        if after < 0:
            continue

        opens = blank.find("{", after)

        if opens < 0:
            continue

        # A brace that opens something else before the loop body does is not this loop.
        if blank[after + 1:opens].strip():
            continue

        for start, close in pairs:
            if start == opens:
                found.append((hit.start(), opens, close))
                break

    return found


NAME = re.compile(r"([A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^<>()]*>)?\s*\([^()]*\)\s*$")


def named(blank, opens):
    """What the method holding a body is called, or None."""
    head = blank[:opens].rstrip()
    found = NAME.search(head)

    return found.group(1) if found else None


def site_around(blank, at):
    """The method holding this offset, as (open, close) offsets, or None."""
    holding = [pair for pair in blocks(blank) if pair[0] < at < pair[1]]
    holding.sort(key=lambda pair: pair[1] - pair[0])

    for opens, closes in holding:
        head = blank[:opens].rstrip()

        if METHOD.search(head) and not STATEMENT.search(head):
            return opens, closes

    return None


KINDS = ("lookup", "build")


def allowed():
    """Each allowed site, as site -> the halves it is allowed for."""
    found = {}

    if not ALLOWED.exists():
        return found

    for line in ALLOWED.read_text(encoding="utf-8").splitlines():
        line = line.strip()

        # A comment line, as against the "#lookup" or "#build" suffix on a site.
        if not line or line.startswith("#"):
            continue

        site, _, kind = line.partition("#")
        site = site.strip()
        kind = kind.strip()

        if not site:
            continue

        found.setdefault(site, set()).update([kind] if kind in KINDS else KINDS)

    return found


def files():
    for root in LOOKED_AT:
        if root.is_dir():
            for path in sorted(root.rglob("*.cs")):
                if "obj" not in path.parts and "bin" not in path.parts:
                    yield path


def main():
    skip = allowed()
    problems = []
    looked = 0

    for path in files():
        source = path.read_text(encoding="utf-8", errors="replace")

        if "TopicGuid" not in source and "lias" not in source:
            continue

        blank = masked(source)
        name = str(path.relative_to(ROOT)).replace("\\", "/")

        for head, opens, closes in loops(blank):
            body = blank[opens:closes]

            reads = LOOKUP.search(body) is not None
            builds = BUILD.search(body) is not None

            if not reads and not builds:
                continue

            looked += 1

            method = site_around(blank, head)
            around = blank[method[0]:method[1]] if method else body
            line = blank[:head].count("\n") + 1
            where = named(blank, method[0]) if method else None
            site = f"{name}::{where or '?'}"

            # Allowed one half at a time: a method whose lookup may be made twice is not
            # thereby allowed to build an index that keeps whichever was written last.
            waived = skip.get(site, set())

            if reads and "lookup" in waived:
                reads = False

            if builds and "build" in waived:
                builds = False

            if not reads and not builds:
                continue

            missing = []

            if not CLAIMED.search(around):
                missing.append("claims nothing")

            if not ORDERED.search(around):
                missing.append("does not order its candidates")

            if missing:
                does = "matches on" if reads else "indexes by"
                problems.append(f"{name}:{line} ({site}): this loop {does} a topic guid "
                                f"or an alias and " + " and ".join(missing))

    if problems:
        print("matches that can be taken twice:\n")

        for problem in problems:
            print("  " + problem)

        print(
            "\nA topic guid is derived from the element pair and the cell, and an alias is a\n"
            "neighbouring cell: neither is unique, so two candidates can match one held record,\n"
            "and an index built under one keeps whichever was written last.\n"
            "Claim what is matched - a set of what has been taken - and order the candidates,\n"
            "so the same two answer the same way twice.\n"
            "FileCheck's Claims index is the pattern to copy.\n"
            "A site where a second match is genuinely wanted goes in checks/claim-once-allowed.txt\n"
            "with the reason.")
        return 1

    print(f"claim once: {looked} loop(s) matching or indexing on a topic guid or an alias, "
          "each claims and orders")
    return 0


if __name__ == "__main__":
    sys.exit(main())
