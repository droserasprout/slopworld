set shell := ["bash", "-euc"]
set quiet
set default-list
set working-directory := '..'

# Run every stage locally; GitHub Actions groups these into parallel jobs
all: protocol format-mod tools tests rust coverage-rust coverage-mod sidecar-rust sidecar-tools bench

# Check generated bindings
protocol:
    "$JUST_CMD" --justfile "$JUST_ROOT" check protocol

# Check C# formatting
format-mod:
    "$JUST_CMD" --justfile "$JUST_ROOT" lint mod

# Check Python sources
tools:
    "$JUST_CMD" --justfile "$JUST_ROOT" lint tools

# Run regular Python and pager tests
tests:
    "$JUST_CMD" --justfile "$JUST_ROOT" check themes
    "$JUST_CMD" --justfile "$JUST_ROOT" test tools
    "$JUST_CMD" --justfile "$JUST_ROOT" test pager

# Check daemon Rust sources
rust:
    "$JUST_CMD" --justfile "$JUST_ROOT" lint daemon

# Run daemon Rust coverage
coverage-rust:
    "$JUST_CMD" --justfile "$JUST_ROOT" coverage daemon

# Run game-free C# coverage
coverage-mod:
    "$JUST_CMD" --justfile "$JUST_ROOT" coverage mod

# Check and test the sidecar launcher
sidecar-rust:
    "$JUST_CMD" --justfile "$JUST_ROOT" sidecar::lint sidecar::test-rust

# Test sidecar Python and shell tooling
sidecar-tools:
    "$JUST_CMD" --justfile "$JUST_ROOT" sidecar::test-tools

# Test benchmark tooling separately from regular tests
bench:
    "$JUST_CMD" --justfile "$JUST_ROOT" bench test
