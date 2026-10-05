#!/usr/bin/env bash
# Bootstrap uv before Python package recipes can run.
set -euo pipefail
test "$(uname -s)" = Darwin || { echo "mac setup must run on macOS" >&2; exit 1; }
command -v brew >/dev/null 2>&1 || { echo "missing Homebrew" >&2; exit 1; }
brew install git just dotnet uv
brew install --cask docker-desktop
echo "Open Docker Desktop once, then run: just --justfile mac/justfile check"
