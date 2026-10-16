# Check the selected sidecar and install the mod into native macOS RimWorld
[group('macOS')]
mac-install: mac-game-check mac-doctor (install-mod MAC_MANAGED MAC_MODS)

# Start the macOS sidecar and launch native RimWorld
[group('macOS')]
mac-run: mac-game-check mac-mod-check mac-start daemon-build
    "${RUNNER}" --sidecar --game-exe "${MAC_GAME}" --working-dir "${MAC_RESOURCES}" \
        --mods "${MAC_MODS}" ${PROFILE:+--profile "$PROFILE"} --no-window-fix -- ${MAC_GAME_ARGS}

# Install and run native macOS RimWorld with the sidecar
[group('macOS')]
mac-dev: mac-install && mac-run

# Verify nested Bubblewrap, pasta and tmux on macOS
[private]
mac-doctor: mac-docker-check sidecar-doctor

# Start the configured macOS sidecar, reusing its container
[private]
mac-start: mac-docker-check sidecar-build
    uv run --locked python -m mac.workflow start --workspace "$SLOPCAR_WORKSPACE"

# Install the macOS build tools and Docker Desktop with Homebrew
[group('macOS')]
mac-setup:
    bash mac/setup.sh

# Check Docker Desktop on macOS
[private]
mac-docker-check:
    uv run --locked python -m mac.workflow docker-check

# Check the native macOS RimWorld and .NET SDK paths
[private]
mac-game-check:
    uv run --locked python -m mac.workflow game-check

# Check Docker, .NET SDK and the native macOS RimWorld install
[group('macOS')]
mac-check: mac-docker-check mac-game-check

# Build SlopWorld.dll against native macOS RimWorld
[group('macOS')]
mac-mod-build: mac-game-check (mod-build MAC_MANAGED)

# Create the isolated native macOS sidecar profile if it is absent
[group('macOS')]
mac-profile: daemon-build
    "${RUNNER}" --sidecar --init-profile ${PROFILE:+--profile "$PROFILE"}

[private]
mac-mod-check:
    uv run --locked python -m mac.workflow mod-check
