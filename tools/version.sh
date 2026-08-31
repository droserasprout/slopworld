#!/bin/sh
set -eu

fallback=${1:?usage: version.sh FALLBACK}
repo=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
tag=$(git -C "$repo" describe --tags --exact-match HEAD 2>/dev/null || true)
hash=$(git -C "$repo" rev-parse --short HEAD 2>/dev/null || true)
date=$(date -u +%Y%m%d)

if printf '%s\n' "$tag" | grep -Eq '^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'; then
    printf '%s\n' "${tag#v}"
elif [ -n "$hash" ]; then
    printf '%s-%s-%s\n' "$fallback" "$date" "$hash"
else
    printf '%s\n' "$fallback"
fi
