#!/usr/bin/env python3
"""The SDK the workflows ask for must be the SDK the build actually uses.

The release failed to restore with:

    C:\\Program Files\\dotnet\\sdk\\10.0.400\\NuGet.targets(198,5): error MSB4181

Every workflow step says `dotnet-version: 8.0.x`, and every one of them was building with .NET 10.
`actions/setup-dotnet` installs an SDK; it does not remove the ones already on the runner image,
and MSBuild picks the highest installed SDK unless a global.json pins it. So the day the
windows-latest image gained .NET 10, four workflow steps quietly changed which compiler they used
and nothing in the repository said otherwise.

It is invisible locally: a machine with only the 8 SDK installed builds exactly as intended and
cannot reproduce it. That is what makes it worth a check rather than a memory.

global.json is the fix and the enforcement in one - the SDK itself obeys it, on the runner and on
a contributor's laptop alike. This only holds it to the version the workflows ask for, so the pin
and the workflows cannot drift apart.

    python3 checks/validate-sdk-pinned.py
"""

import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
PIN = ROOT / "global.json"
FLOWS = sorted((ROOT / ".github" / "workflows").glob("*.yml"))

ASKED = re.compile(r"dotnet-version:\s*([0-9]+)\.")


def fail(message):
    print("FAIL: " + message)
    return 1


def main():
    problems = 0

    if not PIN.exists():
        return fail("there is no global.json, so the build uses whatever SDK the runner image "
                    "happens to ship - which is how a release built with .NET 10 while every "
                    "workflow step asked for 8")

    try:
        pinned = json.loads(PIN.read_text(encoding="utf-8"))["sdk"]["version"]
    except Exception as trouble:
        return fail("global.json does not name an SDK version: {}".format(trouble))

    major = pinned.split(".")[0]

    wanted = set()

    for flow in FLOWS:
        for found in ASKED.finditer(flow.read_text(encoding="utf-8", errors="replace")):
            wanted.add(found.group(1))

    if not wanted:
        print("global.json pins SDK {} and no workflow asks for one".format(pinned))
        return 0

    for asked in sorted(wanted):
        if asked != major:
            problems |= fail("a workflow asks for .NET {} but global.json pins {} - one of them "
                             "is wrong, and the build will follow global.json"
                             .format(asked, pinned))

    if problems:
        return 1

    print("global.json pins SDK {}, which is what the workflows ask for".format(pinned))
    return 0


if __name__ == "__main__":
    sys.exit(main())
