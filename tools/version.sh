#!/bin/sh
set -eu

fallback=${1:?usage: version.sh FALLBACK}
repo=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
tag=$(git -C "$repo" describe --tags --exact-match HEAD 2>/dev/null || true)
hash=$(git -C "$repo" rev-parse --short HEAD 2>/dev/null || true)
date=$(date -u +%Y%m%d)

case "$tag" in
    [0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9])
        printf '0.0.%s\n' "$tag"
        ;;
    *)
        if [ -n "$hash" ]; then
            printf '0.0.%s-%s\n' "$date" "$hash"
        else
            printf '%s\n' "$fallback"
        fi
        ;;
esac
