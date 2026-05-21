#!/usr/bin/env bash
# Compute the next release version for Pumex.
#
# Reads the major.minor from Directory.Build.props (strict X.Y.0-dev form),
# then finds the highest existing vX.Y.* git tag and prints the next full
# version (X.Y.Z) to stdout. If no matching tag exists, prints X.Y.0.
#
# Usage: scripts/compute-version.sh
# Output: e.g. "0.1.18"
# Exits non-zero with a clear error message on bad input.

set -euo pipefail

PROPS_FILE="Directory.Build.props"

if [[ ! -f "$PROPS_FILE" ]]; then
    echo "error: $PROPS_FILE not found (run from repo root)" >&2
    exit 1
fi

# Extract the <Version> element contents. Tolerant of surrounding whitespace.
raw_version=$(grep -oE '<Version>[^<]*</Version>' "$PROPS_FILE" | sed -E 's|<Version>([^<]*)</Version>|\1|' || true)

if [[ -z "$raw_version" ]]; then
    echo "error: no <Version> element found in $PROPS_FILE" >&2
    exit 1
fi

# Strict format: X.Y.0-dev. The patch and -dev suffix are conventions;
# only major.minor are used by CI.
if [[ ! "$raw_version" =~ ^([0-9]+)\.([0-9]+)\.0-dev$ ]]; then
    echo "error: <Version> in $PROPS_FILE must match X.Y.0-dev (got '$raw_version')" >&2
    exit 1
fi

major="${BASH_REMATCH[1]}"
minor="${BASH_REMATCH[2]}"

# Find highest patch among existing vX.Y.* tags. Ignores non-conforming tags.
highest_patch=$(
    git tag --list "v${major}.${minor}.*" \
        | sed -nE "s|^v${major}\\.${minor}\\.([0-9]+)$|\\1|p" \
        | sort -n \
        | tail -1
)

if [[ -z "$highest_patch" ]]; then
    next_patch=0
else
    next_patch=$((highest_patch + 1))
fi

echo "${major}.${minor}.${next_patch}"
