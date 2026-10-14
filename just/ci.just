set shell := ["bash", "-euc"]
set quiet
set default-list
set working-directory := '..'

# Run each stage in the same order as GitHub Actions
all: format tools tests rust coverage-rust coverage-mod sidecar bench

# Check generated bindings and source formatting
format:
    "$JUST_CMD" --justfile "$JUST_ROOT" check protocol
    "$JUST_CMD" --justfile "$JUST_ROOT" check daemon-format
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

# Check and test the separate sidecar group
sidecar:
    "$JUST_CMD" --justfile "$JUST_ROOT" sidecar::lint sidecar::test

# Test benchmark tooling separately from regular tests
bench:
    "$JUST_CMD" --justfile "$JUST_ROOT" bench test
