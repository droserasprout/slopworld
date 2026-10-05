# Linux sidecar

# Run a separate profile against the running sidecar daemon (start the sidecar first)
[group('Linux sidecar')]
sidecar-run: daemon
    SLOPD_ENDPOINT="{{ SLOPCAR_ENDPOINT }}" \
        SLOPCAR_PROFILE="{{ SLOPCAR_PROFILE }}" \
        {{ RUNNER }} --game "{{ RIMWORLD }}"

# Build the Linux sidecar image
[group('Linux sidecar')]
sidecar-build:
    {{ SLOPCAR }} build

# Check nested Bubblewrap, pasta, and tmux in the sidecar
[group('Linux sidecar')]
sidecar-doctor:
    {{ SLOPCAR }} doctor

# Rebuild and redeploy the sidecar before each game launch
[group('Linux sidecar')]
sidecar-devloop:
    JUST_CMD="{{ just_executable() }}" SLOPCAR_PROFILE="{{ SLOPCAR_PROFILE }}" {{ SLOPCAR_ENV }} {{ PYTHON }} -m slopcar.devloop {{ SLOPCAR_START_ARGS }}
