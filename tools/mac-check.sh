#!/usr/bin/env bash
# Called by Make from the repository root. Settings come from make/config.mk.
set -euo pipefail

case "${1:-}" in
setup)
test "$(uname -s)" = Darwin || { echo "mac-setup must run on macOS" >&2; exit 1; }
command -v brew >/dev/null 2>&1 || { echo "missing Homebrew" >&2; exit 1; }
brew install git make dotnet
brew install --cask docker-desktop
echo "Use GNU Make as gmake on macOS. Open Docker Desktop once, then run: gmake mac-check"
;;
docker-check)
test "$(uname -s)" = Darwin || { echo "macOS target requires Darwin" >&2; exit 1; }
command -v docker >/dev/null 2>&1 || { echo "Docker is missing. Run gmake mac-setup." >&2; exit 1; }
docker info >/dev/null 2>&1 || { echo "Docker Desktop is not running. Open Docker and retry." >&2; exit 1; }
;;
game-check)
test "$(uname -s)" = Darwin || { echo "macOS target requires Darwin" >&2; exit 1; }
command -v "${DOTNET}" >/dev/null 2>&1 || { echo "${DOTNET} is missing. Run gmake mac-setup." >&2; exit 1; }
test -x "${MAC_GAME}" || { echo "The native RimWorld executable is missing: ${MAC_GAME}. Set MAC_RIMWORLD or MAC_GAME." >&2; exit 1; }
test -f "${MAC_MANAGED}/Assembly-CSharp.dll" || { echo "missing RimWorld assemblies under ${MAC_MANAGED}" >&2; exit 1; }
test -d "${MAC_MODS}" || { echo "The RimWorld Mods directory is missing: ${MAC_MODS}. Set MAC_RIMWORLD." >&2; exit 1; }
;;
mod-check)
test -f "${MAC_MODS}/SlopWorld/About/About.xml" || { echo "The SlopWorld mod is missing. Run gmake mac-install." >&2; exit 1; }
;;
*) echo "usage: $0 {setup|docker-check|game-check|mod-check}" >&2; exit 2 ;;
esac
