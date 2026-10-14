# Task-first developer commands. Components and implementation modules stay private.
set shell := ["bash", "-euc"]
set quiet

# List commands, or show supported scopes: just help build
[group('Help')]
[positional-arguments]
help what='':
    #!/usr/bin/env bash
    set -euo pipefail
    case "$1" in
        '') exec "$JUST_CMD" --list --unsorted ;;
        deps|release|image|docs|bench|trace|sidecar|mac|game) exec "$JUST_CMD" --list "$1" ;;
        *) exec python3 -m tools.recipes.dispatch "$1" --help ;;
    esac

import 'just/settings-game.just'
import 'just/config.just'
import 'just/settings-bench.just'
import 'just/settings-release.just'
import 'just/settings-trace.just'
import 'mac/config.just'
import 'just/workflows.just'

# Build daemon + mod by default, or select one component
[group('Build and run')]
[positional-arguments]
build what='all':
    python3 -m tools.recipes.dispatch build "$@"

# Install daemon + mod by default, or select one component
[group('Build and run')]
[positional-arguments]
install what='all':
    python3 -m tools.recipes.dispatch install "$@"

# Format all sources or one component
[group('Check and fix')]
[positional-arguments]
format what='all':
    python3 -m tools.recipes.dispatch format "$@"

# Check all sources or one component without rewriting
[group('Check and fix')]
[positional-arguments]
lint what='all':
    python3 -m tools.recipes.dispatch lint "$@"

# Run regular game-free tests or one suite
[group('Check and fix')]
[positional-arguments]
test what='all' *args:
    python3 -m tools.recipes.dispatch test "$@"

# Collect coverage, or summarize saved reports
[group('Check and fix')]
[positional-arguments]
coverage what='all':
    python3 -m tools.recipes.dispatch coverage "$@"

# Run a focused check; see just help check
[group('Check and fix')]
[positional-arguments]
check what:
    python3 -m tools.recipes.dispatch check "$@"

# Apply Python lint fixes
[group('Check and fix')]
[positional-arguments]
fix what='tools':
    python3 -m tools.recipes.dispatch fix "$@"

# Run all CI stages or one named stage
[group('Check and fix')]
[positional-arguments]
ci what='all':
    python3 -m tools.recipes.dispatch ci "$@"

# Regenerate all outputs or one named output
[group('Maintain')]
[positional-arguments]
generate what='all':
    python3 -m tools.recipes.dispatch generate "$@"

# Remove build output for all components or one
[group('Build and run')]
[positional-arguments]
clean what='all':
    python3 -m tools.recipes.dispatch clean "$@"

# Remove installed files; preserve config and saves
[group('Build and run')]
[positional-arguments]
uninstall what='all':
    python3 -m tools.recipes.dispatch uninstall "$@"

# Build the launcher and launch the game
[group('Build and run')]
run: _daemon::build
    SLOPWORLD_GAME="$RIMWORLD" "$RUNNER" ${PROFILE:+--profile "$PROFILE"}

# Reinstall and relaunch after each game exit
[group('Build and run')]
devloop:
    $PYTHON -m tools.host.devloop

# Show new game log entries; accepts slopctl options
[group('Specialist commands')]
[positional-arguments]
logs *args: _daemon::build
    #!/usr/bin/env bash
    set -euo pipefail
    if [ -n "$LOG" ]; then export SLOPWORLD_GAME_LOG="$LOG"; fi
    exec "$SLOPCTL" "$@" logs game --follow

# Restore locks or explicitly update dependency graphs
[group('Maintain')]
mod deps "just/deps.just"
# Version bumps, release archives and publication
[group('Maintain')]
mod release "just/release.just"
import 'just/package.just'
# Distribution release compiler images
[group('Maintain')]
mod image "just/image.just"
# Book builds, validation and serving
[group('Specialist commands')]
mod docs "just/docs.just"
# Benchmark suites, terminal measurements and reports
[group('Specialist commands')]
mod bench "just/bench.just"
# Capture and summarize mod latency logs
[group('Specialist commands')]
mod trace "just/trace.just"
# Build, install, check and run the sidecar
[group('Specialist commands')]
mod sidecar "just/sidecar.just"
# Native macOS game setup and launch
[group('Specialist commands')]
mod mac "just/mac.just"
# GOG authentication, installation and updates
[group('Specialist commands')]
mod game "just/game.just"

[private]
mod _daemon "just/daemon.just"

[private]
mod _mod "just/mod.just"

[private]
mod _tools "just/tools.just"

[private]
mod _generate "just/generate.just"

[private]
mod _coverage "just/coverage.just"

[private]
mod _ci "just/ci.just"

[private]
mod _deps "just/dependency-graphs.just"
