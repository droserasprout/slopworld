#!/bin/sh
# Capture the game window in a PNG file.
# An agent sandbox has no display unless its project requests one.
#
# Use X11 because RimWorld is an SDL/X11 client.
# On Wayland, Xwayland provides access to the RimWorld window without a portal request.
# The `x11` preset mounts /tmp/.X11-unix and the authentication cookie.
#
# Usage: tools/shot.sh [out.png]  (default /tmp/slopworld-shot.png)
set -eu

out="${1:-/tmp/slopworld-shot.png}"

[ -n "${DISPLAY:-}" ] || {
	echo "no DISPLAY: add the 'x11' preset to this project's sandbox" >&2
	exit 1
}
[ -e /tmp/.X11-unix ] || {
	echo "no X socket in the sandbox: add the 'x11' preset to this project" >&2
	exit 1
}
for t in xdotool import; do
	command -v "$t" >/dev/null || { echo "$t is not installed" >&2; exit 1; }
done

# Use --onlyvisible because RimWorld keeps unmapped windows.
# Capturing an unmapped window produces a black rectangle.
win=$(xdotool search --onlyvisible --name 'RimWorld' 2>/dev/null | head -1 || true)
[ -n "$win" ] || win=$(xdotool search --onlyvisible --class 'rimworld' 2>/dev/null | head -1 || true)
[ -n "$win" ] || {
	echo "no RimWorld window on $DISPLAY - is the game running?" >&2
	exit 1
}

import -window "$win" "$out"
echo "$out"
