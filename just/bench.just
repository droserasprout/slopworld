# Run daemon, mod, ipc or gamefree benchmarks; pass report CLI options
[positional-arguments]
[group('bench')]
bench-run what='gamefree' *args:
    shift; uv run --locked python -m bench.report run --suite {{ quote(what) }} --build "$BUILD" "$@"

# Render saved measurements; pass report CLI options
[positional-arguments]
[group('bench')]
bench-report *args:
    uv run --locked python -m bench.report report "$@"

# Write the latest report
[positional-arguments]
[group('bench')]
bench-latest *args:
    uv run --locked python -m bench.report report --latest "$@"

# Format the Rust IPC benchmark
[group('bench')]
bench-format:
    cd bench/ipc/rust && cargo fmt

# Check the Rust IPC benchmark
[group('bench')]
bench-lint:
    cd bench/ipc/rust && cargo fmt --check
    cd bench/ipc/rust && cargo clippy --quiet --locked --all-targets -- -D warnings
