# Tests and coverage

# Test supporting tools and generated data
[group('Tests and coverage')]
test-tools: test-recipes test-reference test-wire-contract test-themes test-text-sprites test-bench-report

# Run Rust tests
[group('Tests and coverage')]
test-daemon: api-contract
    cd slopd && {{ CARGO }} test --quiet {{ TEST_ARGS }}

# Run game-free C# tests
[group('Tests and coverage')]
test-mod: mod-deps api-contract
    {{ DOTNET }} run --project "{{ TEST_PROJECT }}" --configuration Release -- --quiet

# Test shared definitions and generated bindings
[group('Tests and coverage')]
test-wire-contract: api-contract
    {{ PYTHON }} tools/test_wire_contract.py

# Test theme catalog build validation
[group('Tests and coverage')]
test-themes: validate-themes
    {{ PYTHON }} tools/test_validate_themes.py

# Check text sprite metadata and loading-tip ASCII coverage
[group('Tests and coverage')]
test-text-sprites:
    {{ PYTHON }} tools/test_text_sprites.py

# Test benchmark reporting
[group('Tests and coverage')]
test-bench-report:
    {{ PYTHON }} tools/test_bench_report.py
    {{ PYTHON }} -m unittest discover -s bench/terminal-input -p 'test_*.py'

# Test pager geometry with isolated tmux and less (no game)
[group('Tests and coverage')]
test-pager:
    {{ PYTHON }} tools/test_pager_geometry.py

# Measure Rust and game-free C# test coverage
[group('Tests and coverage')]
coverage: coverage-daemon coverage-mod

# Measure Rust coverage and write coverage/rust.cobertura.xml
[group('Tests and coverage')]
coverage-daemon: api-contract
    bash tools/coverage.sh daemon

# Measure game-free C# coverage and write coverage/csharp.cobertura.xml
[group('Tests and coverage')]
coverage-mod: mod-deps api-contract
    bash tools/coverage.sh mod

# Summarize existing Rust and C# coverage reports
[group('Tests and coverage')]
coverage-summary:
    {{ PYTHON }} tools/coverage_summary.py "{{ COVERAGE_DIR }}/rust.cobertura.xml" 'Rust (default scope)'
    {{ PYTHON }} tools/coverage_summary.py "{{ COVERAGE_DIR }}/rust.filtered.cobertura.xml" 'Rust (excluding test/generated/benchmark files)'
    echo
    {{ PYTHON }} tools/coverage_summary.py "{{ COVERAGE_DIR }}/csharp.cobertura.xml" 'C#'

# Test recipe configuration and recursive overrides without building or launching
[group('Tests and coverage')]
test-recipes:
    {{ PYTHON }} tools/test_recipes.py

# Test the mod and daemon environment reference scope
[group('Tests and coverage')]
test-reference:
    {{ PYTHON }} tools/test_reference.py
