# Build the native host-side launcher
[group('sidecar')]
sidecar-build:
    cd slopcar && cargo build --quiet --locked $CARGOFLAGS

# Build the Linux sidecar runtime image
[group('sidecar')]
sidecar-image: sidecar-build
    "$SLOPCAR" build --source "$PWD"

# Install the native launcher
[group('sidecar')]
sidecar-install: sidecar-build
    mkdir -p "$BIN"
    install -m755 "$SLOPCAR" "$BIN/slopcar"
    uv run --locked python -m tools.cli_completions install slopcar

# Format launcher sources
[group('sidecar')]
sidecar-format:
    cd slopcar && cargo fmt

# Check formatting and Clippy
[group('sidecar')]
sidecar-lint:
    cd slopcar && cargo fmt --check
    cd slopcar && cargo clippy --quiet --locked --all-targets -- -D warnings

# Run launcher Rust tests; Python tests belong to tools-test
[positional-arguments]
[group('sidecar')]
sidecar-test *args:
    cd slopcar && cargo test --quiet --locked "$@"

# Remove component build output
[group('sidecar')]
sidecar-clean:
    cd slopcar && cargo clean

# Check nested Bubblewrap, pasta and tmux
[group('sidecar')]
sidecar-doctor: sidecar-build
    "$SLOPCAR" doctor

# Rebuild and redeploy before each game launch
[positional-arguments]
[group('sidecar')]
sidecar-devloop *args: sidecar-build
    uv run --locked python -m slopcar.devloop --workspace "$SLOPCAR_WORKSPACE" "$@"
