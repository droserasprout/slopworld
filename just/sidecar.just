# Linux sidecar

# Build the native host-side container launcher
[group('Linux sidecar')]
sidecar:
    cd slopcar && {{ CARGO }} build --locked {{ CARGOFLAGS }}

# Install the native sidecar launcher
[group('Installation')]
install-sidecar: sidecar
    mkdir -p "{{ BIN }}"
    install -m755 "{{ SLOPCAR }}" "{{ BIN }}/slopcar"

# Run game-free sidecar launcher tests
[group('Tests and coverage')]
test-sidecar:
    cd slopcar && {{ CARGO }} test --locked {{ TEST_ARGS }}

# Resolve sidecar dependencies after changing its manifest
[group('Generated files')]
lock-sidecar:
    cd slopcar && {{ CARGO }} generate-lockfile

# Format the sidecar launcher
[group('Formatting')]
format-sidecar:
    cd slopcar && {{ CARGO }} fmt

# Check sidecar formatting and Clippy
[group('Lint')]
lint-sidecar:
    cd slopcar && {{ CARGO }} fmt --check
    cd slopcar && {{ CARGO }} clippy --locked --all-targets -- -D warnings

# Run a separate profile against the running sidecar daemon (start the sidecar first)
[group('Linux sidecar')]
sidecar-run: daemon
    SLOPD_ENDPOINT="{{ SLOPCAR_ENDPOINT }}" \
        SLOPCAR_PROFILE="{{ SLOPCAR_PROFILE }}" \
        {{ RUNNER }} --game "{{ RIMWORLD }}"

# Build the Linux sidecar image
[group('Linux sidecar')]
sidecar-build: sidecar
    "{{ SLOPCAR }}" build --source "{{ justfile_directory() }}"

# Check nested Bubblewrap, pasta, and tmux in the sidecar
[group('Linux sidecar')]
sidecar-doctor: sidecar
    "{{ SLOPCAR }}" doctor

# Rebuild and redeploy the sidecar before each game launch
[group('Linux sidecar')]
sidecar-devloop: sidecar
    JUST_CMD="{{ just_executable() }}" SLOPCAR_PROFILE="{{ SLOPCAR_PROFILE }}" {{ SLOPCAR_ENV }} {{ PYTHON }} -m slopcar.devloop {{ SLOPCAR_START_ARGS }}

# Refresh the sidecar executable's full dependency notices
[group('Generated files')]
refresh-sidecar-licenses:
    {{ PYTHON }} -m tools.licenses.sidecar_licenses
