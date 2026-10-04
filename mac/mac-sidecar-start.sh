#!/usr/bin/env bash
set -euo pipefail

container=${SLOPCAR_CONTAINER:?SLOPCAR_CONTAINER is required}
repo=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
slopcar="$repo/slopcar/slopcar"

state=$(docker container inspect --format '{{.State.Running}}' "$container" 2>/dev/null || true)
if [[ "$state" == true ]]; then
	echo "sidecar $container is already running"
elif docker container inspect "$container" >/dev/null 2>&1; then
	"$slopcar" start
else
	"$slopcar" start "$@"
fi
