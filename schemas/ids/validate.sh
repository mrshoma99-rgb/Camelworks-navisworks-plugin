#!/usr/bin/env bash
# Validate written IDS against the published buildingSMART schema.
#
# Run after the test suite: IdsWriterTests dumps real writer output into an
# "ids-validation" directory under the test binaries, and this checks every file in it.
# Takes that directory as its argument, or finds it.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
out="${1:-}"

if [[ -z "$out" ]]; then
  out="$(find "$here/../.." -type d -name ids-validation -print -quit || true)"
fi

if [[ -z "$out" || ! -d "$out" ]]; then
  echo "no ids-validation directory found; run the tests first" >&2
  exit 1
fi

# ids.xsd is vendored verbatim and imports three schemas from w3.org. Only one of them
# matters - idsValue is <xs:element ref="xs:restriction"/> and applicability carries
# <xs:attributeGroup ref="xs:occurs"/> - and both live in XMLSchema.xsd, which is 90KB of
# machinery for two declarations. xsd-subset.xsd declares exactly those two, so the check
# runs with no network at all; see README.md.
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

cp "$here/xsd-subset.xsd" "$work/"

sed -e 's#<xs:import namespace="http://www.w3.org/XML/1998/namespace"[^>]*>##' \
    -e 's#<xs:import namespace="http://www.w3.org/2001/XMLSchema" schemaLocation="http://www.w3.org/2001/XMLSchema.xsd"/>#<xs:import namespace="http://www.w3.org/2001/XMLSchema" schemaLocation="xsd-subset.xsd"/>#' \
    -e 's#<xs:import namespace="http://www.w3.org/2001/XMLSchema-instance"[^>]*>##' \
    "$here/ids.xsd" > "$work/ids.xsd"

# If the substitution missed - a new schema revision with different import lines - the
# schema will not compile, and saying so beats reporting every file as valid.
if grep -q 'schemaLocation="http://www.w3.org/2001/XMLSchema.xsd"' "$work/ids.xsd"; then
  echo "the XMLSchema import in ids.xsd was not redirected; the vendored schema has changed" >&2
  exit 1
fi

fail=0
checked=0

shopt -s nullglob
for file in "$out"/*.ids; do
  if xmllint --noout --nonet --schema "$work/ids.xsd" "$file"; then
    checked=$((checked + 1))
  else
    fail=1
  fi
done

if [[ "$checked" -eq 0 ]]; then
  echo "no IDS files were checked, which means the dump did not run" >&2
  exit 1
fi

echo "$checked IDS files validated against the published schema"
exit "$fail"
