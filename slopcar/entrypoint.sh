#!/bin/sh
set -eu

config=/home/slop/.config/slopworld
data=/home/slop/.local/share/slopworld

if [ "$(id -u)" -eq 0 ]; then
    echo "slopcar: refusing to run slopd as root" >&2
    exit 1
fi

if [ "${1:-}" = "slopcar-doctor" ]; then
    exec "$@"
fi

for path in "$config" "$data"; do
    if [ ! -d "$path" ] || [ ! -w "$path" ]; then
        echo "slopcar: required persistent directory is not writable: $path" >&2
        exit 1
    fi
done

if [ ! -f "$config/config.toml" ]; then
    echo "slopcar: missing $config/config.toml; create the container with the host launcher" >&2
    exit 1
fi

exec "$@"
