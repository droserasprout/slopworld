# Shared CI dependency groups; imported into the root namespace.

# Run all game-free CI checks
[group('Check and fix')]
ci: ci-tools ci-rust ci-mod

# Check daemon and sidecar Rust, collecting daemon coverage
[group('CI')]
ci-rust: daemon-lint daemon-coverage sidecar-lint sidecar-test

# Check generated files and Python, pager, sidecar and benchmark tooling
[group('CI')]
ci-tools: generate-check-protocol tools-lint test-tools

# Check C# formatting and collect game-free coverage
[group('CI')]
ci-mod: mod-lint mod-coverage
