# Shared CI dependency groups; imported into the root namespace.

# Run all game-free CI checks
[group('Check and fix')]
ci: ci-tools ci-rust ci-mod ci-web

# Check daemon and sidecar Rust, collecting daemon coverage
[group('CI')]
ci-rust: daemon-lint daemon-coverage sidecar-lint sidecar-test

# Check generated files and Python, pager, sidecar and benchmark tooling
[group('CI')]
ci-tools: generate-check-protocol tools-lint test-tools

# Compile against pinned references, check C# formatting and collect game-free coverage
[group('CI')]
ci-mod: mod-lint (mod-build 'rimref' 'release' 'true') mod-coverage

# Check slopweb tooling and game-free Node/managed contracts without launching a browser
[group('CI')]
ci-web: mod-restore-tools slopweb-lint slopweb-test
