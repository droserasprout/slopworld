#!/usr/bin/env bash
set -u

make_cmd=${MAKE_CMD:?MAKE_CMD is required}
repo=$(CDPATH= cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
slopcar="$repo/slopcar/slopcar"

: "${SLOPCAR_CONFIG_DIR:?SLOPCAR_CONFIG_DIR is required}"
: "${SLOPCAR_DATA_DIR:?SLOPCAR_DATA_DIR is required}"
: "${SLOPCAR_PORT:?SLOPCAR_PORT is required}"
: "${SLOPCAR_CONTAINER:?SLOPCAR_CONTAINER is required}"

sidecar_env=(
	env
	"SLOPCAR_CONFIG_DIR=$SLOPCAR_CONFIG_DIR"
	"SLOPCAR_DATA_DIR=$SLOPCAR_DATA_DIR"
	"SLOPCAR_PORT=$SLOPCAR_PORT"
	"SLOPCAR_CONTAINER=$SLOPCAR_CONTAINER"
)

while true; do
	"$make_cmd" sidecar-build
	"$make_cmd" install-mod
	"${sidecar_env[@]}" "$slopcar" rm >/dev/null 2>&1 || true
	"${sidecar_env[@]}" "$slopcar" start "$@"
	"$make_cmd" sidecar-run
	sleep 1
done
