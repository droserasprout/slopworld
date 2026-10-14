set shell := ["bash", "-euc"]
set quiet
set default-list
set working-directory := '..'

# Build the native host-side launcher
build:
    cd slopcar && $CARGO build --locked $CARGOFLAGS

# Build the Linux sidecar runtime image
image: build
    "$SLOPCAR" build --source "$PWD"

# Install the native launcher
install: build
    mkdir -p "$BIN"
    install -m755 "$SLOPCAR" "$BIN/slopcar"

# Format launcher sources
format:
    cd slopcar && $CARGO fmt

# Check formatting and Clippy
lint:
    cd slopcar && $CARGO fmt --check
    cd slopcar && $CARGO clippy --locked --all-targets -- -D warnings

# Run launcher and sidecar script tests
[positional-arguments]
test *args:
    cd slopcar && $CARGO test --locked $TEST_ARGS "$@"
    $PYTHON -m pytest slopcar

# Remove component build output
clean:
    cd slopcar && $CARGO clean

# Check nested Bubblewrap, pasta and tmux
doctor: build
    "$SLOPCAR" doctor

# Launch against an already running sidecar
run:
    "$JUST_CMD" --justfile "$JUST_ROOT" build daemon
    "$RUNNER" --sidecar --game "$RIMWORLD" ${PROFILE:+--profile "$PROFILE"}

# Rebuild and redeploy before each game launch
[positional-arguments]
devloop *args: build
    $PYTHON -m slopcar.devloop --workspace "$SLOPCAR_WORKSPACE" "$@"
