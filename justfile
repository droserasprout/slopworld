# Imported recipe files share one flat namespace; root recipes combine components.
set shell := ["bash", "-euc"]
set quiet
set default-list

import 'just/settings-game.just'
import 'just/config.just'
import 'just/settings-release.just'
import 'mac/config.just'

# Build the daemon, launcher and mod
[group('Build and run')]
build: daemon-build mod-build

# Install the daemon, launcher and mod
[group('Build and run')]
install: daemon-install install-mod

# Install Bash, Zsh and Fish completions for all clients into user data directories
[group('Build and run')]
install-completions:
    uv run --locked python -m tools.cli_completions install slopctl slopworld slopcar

# Remove ordinary build and coverage output
[group('Build and run')]
clean: sidecar-clean daemon-clean mod-clean clean-coverage

# Remove installed files; preserve config and saves
[group('Build and run')]
uninstall: daemon-uninstall uninstall-mod

# Format Python, Rust and C# sources
[group('Check and fix')]
format: sidecar-format tools-format daemon-format mod-format

# Check sources without rewriting them
[group('Check and fix')]
lint: sidecar-lint tools-lint daemon-lint mod-lint

# Run regular game-free tests
[group('Check and fix')]
test: daemon-test sidecar-test mod-test test-tools

# Run all game-free Python, pager and benchmark-helper tests
[group('Check and fix')]
test-tools: mod-check-themes tools-test tools-test-pager

import 'just/daemon.just'
import 'just/mod.just'
import 'just/tools.just'
import 'just/generate.just'
import 'just/coverage.just'
import 'just/ci.just'

# Build the launcher and launch the game
[group('Build and run')]
run: daemon-build
    SLOPWORLD_GAME="$RIMWORLD" "$RUNNER" ${PROFILE:+--profile "$PROFILE"}

# Reinstall and relaunch after each game exit
[group('Build and run')]
devloop:
    uv run --locked python -m tools.host.devloop

# Show new game log entries; accepts slopctl options
[group('Specialist commands')]
[positional-arguments]
logs *args: daemon-build
    #!/usr/bin/env bash
    set -euo pipefail
    if [ -n "$LOG" ]; then export SLOPWORLD_GAME_LOG="$LOG"; fi
    exec "$SLOPCTL" "$@" logs game --follow

import 'just/deps.just'
import 'just/dependency-graphs.just'
import 'just/release.just'
import 'just/package.just'
import 'just/image.just'
import 'just/docs.just'
import 'just/bench.just'
import 'just/trace.just'
import 'just/sidecar.just'
import 'just/mac.just'
import 'just/game.just'

# Install the mod and its launcher
[group('Build and run')]
install-mod managed=MANAGED mods=MODS: daemon-build (mod-build managed)
    uv run --locked python -m tools.host.install_mod --runner "$RUNNER" --mods {{ quote(mods) }}

# Remove the installed mod
[group('Build and run')]
uninstall-mod mods=MODS: daemon-build
    "$RUNNER" mod uninstall --mods {{ quote(mods) }}

# Restore reviewed Python, runtime and .NET tool locks
[group('Maintain')]
deps-restore: tools-restore mod-restore mod-restore-tools

# Update every dependency graph, then regenerate and stage notices
[group('Maintain')]
deps-update: deps-update-tools deps-update-daemon deps-update-sidecar deps-update-bench deps-update-mod generate-daemon-licenses generate-sidecar-licenses generate-licenses

# Launch against an already running sidecar
[group('Specialist commands')]
sidecar-run: daemon-build
    "$RUNNER" --sidecar --game "$RIMWORLD" ${PROFILE:+--profile "$PROFILE"}

# Measure desktop terminal input; pass terminal benchmark CLI options
[group('Specialist commands')]
[positional-arguments]
bench-terminal *args: daemon-build
    uv run --locked python bench/terminal-input/terminal-input-bench.py "$@"
