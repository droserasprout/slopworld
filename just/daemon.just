set shell := ["bash", "-euc"]
set quiet
set default-list
set working-directory := '..'

# Build the daemon, CLI and launcher
build:
    cd slopd && env ${VERSION:+SLOPWORLD_BUILD_VERSION="$VERSION"} $CARGO build $CARGOFLAGS

# Format Rust sources
format:
    cd slopd && $CARGO fmt

# Check Rust formatting
check-format:
    cd slopd && $CARGO fmt --check

# Check formatting and Clippy
lint: check-format
    cd slopd && $CARGO clippy --all-targets -- -D warnings

# Run Rust tests
[positional-arguments]
test *args:
    cd slopd && $CARGO test --quiet $TEST_ARGS "$@"

# Collect Rust coverage
coverage:
    $PYTHON -m tools.coverage.collect daemon

# Install the daemon, CLI, launcher and user service
install: build
    $PYTHON -m tools.host.install_daemon
    install -Dm755 "$RUNNER" "$BIN/slopworld"

# Remove the daemon, CLI, launcher and user service
uninstall:
    -systemctl --user disable --now slopd.service
    rm -f "$UNITS/slopd.service" "$BIN/slopd" "$BIN/slopctl" "$BIN/slopworld"
    systemctl --user daemon-reload

# Remove component build output
clean:
    cd slopd && $CARGO clean
