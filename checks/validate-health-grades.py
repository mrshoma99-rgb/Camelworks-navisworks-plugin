#!/usr/bin/env python3
"""Fail when a model-health rule has no grade.

ModelHealth emits findings identified by a string; FederationReview ranks them by the grade in
ModelHealth.Grades. Adding a rule and forgetting to grade it is not a compile error and not a test
failure - the finding simply defaults to "worth a look" and sorts itself below everything that
matters, which is the one outcome the ranking exists to prevent.
"""
import re
import sys
from pathlib import Path

SOURCE = Path(__file__).resolve().parent.parent / "CamelWorks.Core" / "Data" / "ModelHealth.cs"

EMITTED = re.compile(r'Add\(\s*findings,\s*"([a-z0-9-]+)"')
GRADED = re.compile(r'\["([a-z0-9-]+)"\]\s*=\s*ReviewGrade\.')


def main():
    if not SOURCE.is_file():
        print(f"no such file: {SOURCE}", file=sys.stderr)
        return 1

    text = SOURCE.read_text(encoding="utf-8")
    emitted = set(EMITTED.findall(text))
    graded = set(GRADED.findall(text))

    if not emitted:
        print("found no health rules at all - has Add(findings, ...) been renamed?", file=sys.stderr)
        return 1

    ungraded = sorted(emitted - graded)
    orphaned = sorted(graded - emitted)

    for rule in ungraded:
        print(f'ModelHealth emits "{rule}" but ModelHealth.Grades does not grade it', file=sys.stderr)

    for rule in orphaned:
        print(f'ModelHealth.Grades grades "{rule}" but no rule emits it', file=sys.stderr)

    if ungraded or orphaned:
        return 1

    print(f"health grades clear: {len(emitted)} rules, every one graded")
    return 0


if __name__ == "__main__":
    sys.exit(main())
