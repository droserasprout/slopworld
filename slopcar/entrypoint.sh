#!/bin/sh
set -eu

config=/home/slop/.config/slopworld
data=/home/slop/.local/share/slopworld

if [ "$(/usr/bin/id -u)" -eq 0 ]; then
    PUID=${PUID:-1000}
    PGID=${PGID:-1000}
    case "$PUID" in
        ''|*[!0-9]*|0*) echo "slopcar: PUID must be a non-root decimal UID" >&2; exit 1 ;;
    esac
    case "$PGID" in
        ''|*[!0-9]*) echo "slopcar: PGID must be a decimal GID" >&2; exit 1 ;;
    esac

    # Root initialization must not resolve tools through the application PATH.
    /usr/sbin/groupmod -o -g "$PGID" slop
    # usermod normally chowns the home recursively, including mounted credentials.
    # Select a nonexistent home while changing IDs, then restore the account path.
    /usr/sbin/usermod -o -u "$PUID" -g "$PGID" -d /nonexistent slop
    /usr/sbin/usermod -d /home/slop slop
    /usr/bin/chown "$PUID:$PGID" /home/slop
    exec /usr/bin/setpriv --reuid=slop --regid=slop --clear-groups \
        --bounding-set=-all --inh-caps=-all --ambient-caps=-all -- "$0" "$@"
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
    echo "slopcar: missing $config/config.toml. Create the container with the host launcher." >&2
    exit 1
fi

exec "$@"
