set shell := ["bash", "-euc"]
set quiet
set default-list
set working-directory := '..'

# Check the selected sidecar and install the mod into native macOS RimWorld
install: game-check doctor
    "$JUST_CMD" --justfile "$JUST_ROOT" MANAGED="${MAC_MANAGED}" MODS="${MAC_MODS}" install mod

# Start the macOS sidecar and launch native RimWorld
run: game-check mod-check start daemon
    "${RUNNER}" --sidecar --game-exe "${MAC_GAME}" --working-dir "${MAC_RESOURCES}" \
        --mods "${MAC_MODS}" ${PROFILE:+--profile "$PROFILE"} --no-window-fix -- ${MAC_GAME_ARGS}

# Install and run native macOS RimWorld with the sidecar
dev:
    ${PYTHON} -m mac.workflow dev

# Verify nested Bubblewrap, pasta and tmux on macOS
[private]
doctor: docker-check
    "$JUST_CMD" --justfile "$JUST_ROOT" sidecar doctor

# Start the configured macOS sidecar, reusing its container
[private]
start: docker-check sidecar
    ${PYTHON} -m mac.workflow start --workspace "$SLOPCAR_WORKSPACE"

# Install the macOS build tools and Docker Desktop with Homebrew
setup:
    bash mac/setup.sh

# Check Docker Desktop on macOS
docker-check:
    ${PYTHON} -m mac.workflow docker-check

# Check the native macOS RimWorld and .NET SDK paths
game-check:
    ${PYTHON} -m mac.workflow game-check

# Check Docker, .NET SDK and the native macOS RimWorld install
check: docker-check game-check

# Build SlopWorld.dll against native macOS RimWorld
mod-build: game-check
    "$JUST_CMD" --justfile "$JUST_ROOT" MANAGED="${MAC_MANAGED}" build mod

# Create the isolated native macOS sidecar profile if it is absent
profile: daemon
    "${RUNNER}" --sidecar --init-profile ${PROFILE:+--profile "$PROFILE"}

[private]
mod-check:
    ${PYTHON} -m mac.workflow mod-check

[private]
daemon:
    "$JUST_CMD" --justfile "$JUST_ROOT" build daemon

[private]
sidecar:
    "$JUST_CMD" --justfile "$JUST_ROOT" sidecar build
