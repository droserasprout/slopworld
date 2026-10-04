# macOS

# Build the sidecar and install the mod into native macOS RimWorld
[group('macOS')]
mac-install: mac-game-check mac-doctor
    "{{ just_executable() }}" MANAGED="{{ MAC_MANAGED }}" MODS="{{ MAC_MODS }}" install-mod

# Start the macOS sidecar and launch native RimWorld
[group('macOS')]
mac-run: mac-game-check mac-mod-check mac-ready
    echo "launching native macOS RimWorld against {{ SLOPCAR_ENDPOINT }}"
    SLOPD_ENDPOINT="{{ SLOPCAR_ENDPOINT }}" \
        SLOPCAR_PROFILE="{{ MAC_PROFILE }}" \
        "{{ RUNNER }}" --game-exe "{{ MAC_GAME }}" --working-dir "{{ MAC_RESOURCES }}" \
            --mods "{{ MAC_MODS }}" --profile "{{ MAC_PROFILE }}" --no-window-fix -- {{ MAC_GAME_ARGS }}

# Install and run native macOS RimWorld with the sidecar
[group('macOS')]
mac:
    JUST_CMD="{{ just_executable() }}" tools/mac.sh

# Build the Linux sidecar image on macOS
[group('macOS')]
mac-build: mac-docker-check sidecar-build

# Verify nested Bubblewrap, pasta and tmux on macOS
[group('macOS')]
mac-doctor: mac-build sidecar-doctor

[group('macOS')]
[private]
mac-ready: mac-build
    {{ SLOPCAR_ENV }} tools/mac-sidecar-start.sh {{ SLOPCAR_START_ARGS }}

# Start the configured macOS sidecar, reusing its container
[group('macOS')]
mac-start: mac-docker-check
    {{ SLOPCAR_ENV }} tools/mac-sidecar-start.sh {{ SLOPCAR_START_ARGS }}

# Stop the macOS sidecar without removing its state
[group('macOS')]
mac-stop: mac-docker-check
    {{ SLOPCAR_ENV }} {{ SLOPCAR }} stop

# Show macOS sidecar status
[group('macOS')]
mac-status: mac-docker-check
    {{ SLOPCAR_ENV }} {{ SLOPCAR }} status

# Show macOS sidecar logs. Use LOG_ARGS='--tail 100' to limit output.
[group('macOS')]
mac-logs: mac-docker-check
    {{ SLOPCAR_ENV }} {{ SLOPCAR }} logs {{ LOG_ARGS }}

# Install the macOS build tools and Docker Desktop with Homebrew
[group('macOS')]
mac-setup:
    bash tools/mac-check.sh setup

# Check Docker Desktop on macOS
[group('macOS')]
mac-docker-check:
    bash tools/mac-check.sh docker-check

# Check the native macOS RimWorld and .NET SDK paths
[group('macOS')]
mac-game-check:
    bash tools/mac-check.sh game-check

# Check Docker, .NET SDK and the native macOS RimWorld install
[group('macOS')]
mac-check: mac-docker-check mac-game-check

# Build SlopWorld.dll against native macOS RimWorld
[group('macOS')]
mac-mod: mac-game-check
    "{{ just_executable() }}" MANAGED="{{ MAC_MANAGED }}" mod

# Create the isolated native macOS sidecar profile if it is absent
[group('macOS')]
mac-profile: daemon
    "{{ RUNNER }}" --profile "{{ MAC_PROFILE }}" --init-profile --sidecar

[group('macOS')]
[private]
mac-mod-check:
    bash tools/mac-check.sh mod-check
