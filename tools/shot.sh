#!/bin/sh
# Grabs the game's window into a PNG, so whoever is working on the mod can look
# at what they just built.
#
# This exists because an agent working on this repo is drawing a UI it cannot
# see: the sandbox has no display unless its project asks for one, and even the
# process is invisible from in there (see game.rs). Ask slopd whether the game is
# up; ask X what it looks like.
#
# X11 and not Wayland on purpose. RimWorld is an SDL/X11 client, so on a Wayland
# desktop it is an Xwayland one - which is the thing that makes this possible at
# all, because a Wayland compositor hands out no screen contents without a
# portal prompt, while an X client's window can simply be read. The `x11` preset
# is what binds /tmp/.X11-unix and the auth cookie; a project without it gets the
# message below rather than a mystery.
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

# --onlyvisible, because RimWorld leaves unmapped windows of its own around and
# an unmapped one grabs as a black rectangle.
win=$(xdotool search --onlyvisible --name 'RimWorld' 2>/dev/null | head -1 || true)
[ -n "$win" ] || win=$(xdotool search --onlyvisible --class 'rimworld' 2>/dev/null | head -1 || true)
[ -n "$win" ] || {
	echo "no RimWorld window on $DISPLAY - is the game running? (curl -s localhost:7717/api/game)" >&2
	exit 1
}

import -window "$win" "$out"
echo "$out"
