# Build the daemon, CLI and launcher
[group('daemon')]
daemon-build:
    cd slopd && env ${VERSION:+SLOPWORLD_BUILD_VERSION="$VERSION"} cargo build --quiet --locked $CARGOFLAGS

# Format Rust sources
[group('daemon')]
daemon-format:
    cd slopd && cargo fmt

# Check Rust formatting
[group('daemon')]
daemon-check-format:
    cd slopd && cargo fmt --check

# Check formatting and Clippy
[group('daemon')]
daemon-lint: daemon-check-format
    cd slopd && cargo clippy --quiet --locked --all-targets -- -D warnings

# Run Rust tests
[positional-arguments]
[group('daemon')]
daemon-test *args:
    cd slopd && cargo test --quiet "$@"

# Native llvm-cov scope excludes adjacent test modules and generated files.
# Git seccomp setup runs after fork and before exec, which discards its coverage counters.
# git/exec_tests.rs checks enforcement in the child; retain setup in the raw report only.
rust_coverage_exclude := '/tests/|/benches/|/benchmark/|/(tests|test_support|test_http|http_fixture|benchmark|[^/]*_tests)\.rs$|/shared/(http_wire|protocol)\.rs$|/git/exec\.rs$|/target/|/rustc[^/]*/library/'

# Collect Rust coverage and require at least 92.68% filtered line coverage
[group('daemon')]
daemon-coverage:
    #!/usr/bin/env bash
    set -euo pipefail
    mkdir -p "$COVERAGE_DIR"
    output="$(cd "$COVERAGE_DIR" && pwd)"
    cd slopd
    # Retain instrumented dependencies; llvm-cov selects matching rustup LLVM tools.
    cargo llvm-cov clean --workspace
    cargo llvm-cov --locked --no-report
    cargo llvm-cov report --cobertura --output-path "$output/rust.cobertura.xml"
    cargo llvm-cov report --ignore-filename-regex {{ quote(rust_coverage_exclude) }} --cobertura --output-path "$output/rust.filtered.cobertura.xml"
    cargo llvm-cov report --ignore-filename-regex {{ quote(rust_coverage_exclude) }} --fail-under-lines 92.68 | tee "$output/rust.files.txt"

# Install the daemon, CLI, launcher and user service
[group('daemon')]
daemon-install: daemon-build
    uv run --locked python -m tools.host.install_daemon
    install -Dm755 "$RUNNER" "$BIN/slopworld"
    uv run --locked python -m tools.cli_completions install slopctl slopworld

# Remove the daemon, CLI, launcher and user service
[group('daemon')]
daemon-uninstall:
    uv run --locked python -m tools.cli_completions uninstall slopctl slopworld
    -systemctl --user disable --now slopd.service
    rm -f "$UNITS/slopd.service" "$BIN/slopd" "$BIN/slopctl" "$BIN/slopworld"
    systemctl --user daemon-reload

# Remove component build output
[group('daemon')]
daemon-clean:
    cd slopd && cargo clean
