# Tests and coverage

# Test supporting tools and generated data
[group('Tests and coverage')]
test-tools: api-contract validate-themes test-licenses test-bench-report
    mkdir -p "{{ COVERAGE_DIR }}"
    {{ PYTHON }} -m pytest --cov=tools --cov-report=term-missing --cov-report=xml:coverage/python.cobertura.xml

# Stage and verify notices shipped with the mod
[group('Tests and coverage')]
test-licenses: stage-licenses check-licenses

# Run Rust tests
[group('Tests and coverage')]
test-daemon: api-contract
    cd slopd && {{ CARGO }} test --quiet {{ TEST_ARGS }}

# Run game-free C# tests
[group('Tests and coverage')]
test-mod: mod-deps api-contract
    {{ DOTNET }} run --project "{{ TEST_PROJECT }}" --configuration Release -p:RestoreLockedMode=true -- --quiet

# Test shared definitions and generated bindings
[group('Tests and coverage')]
test-wire-contract: api-contract
    {{ PYTHON }} -m tools.protocol.test_wire_contract

# Test theme catalog build validation
[group('Tests and coverage')]
test-themes: validate-themes
    {{ PYTHON }} -m tools.themes.test_validate_themes

# Check text sprite metadata and loading-tip ASCII coverage
[group('Tests and coverage')]
test-text-sprites:
    {{ PYTHON }} -m tools.assets.test_text_sprites

# Test benchmark reporting
[group('Tests and coverage')]
test-bench-report:
    {{ PYTHON }} bench/test_bench_report.py
    {{ PYTHON }} -m unittest discover -s bench/terminal-input -p 'test_*.py'

# Test pager geometry with isolated tmux and less (no game)
[group('Tests and coverage')]
test-pager:
    {{ PYTHON }} -m tools.terminal.pager_geometry

# Measure Python, Rust and game-free C# test coverage
[group('Tests and coverage')]
coverage: coverage-tools coverage-daemon coverage-mod

# Measure Python package coverage and write coverage/python.cobertura.xml
[group('Tests and coverage')]
coverage-tools: test-tools

# Measure Rust coverage and write coverage/rust.cobertura.xml
[group('Tests and coverage')]
coverage-daemon: api-contract
    bash tools/coverage/coverage.sh daemon

# Measure game-free C# coverage and write coverage/csharp.cobertura.xml
[group('Tests and coverage')]
coverage-mod: mod-deps api-contract
    bash tools/coverage/coverage.sh mod

# Summarize existing Python, Rust and C# coverage reports
[group('Tests and coverage')]
coverage-summary:
    {{ PYTHON }} -m tools.coverage.coverage_summary "{{ COVERAGE_DIR }}/python.cobertura.xml" 'Python'
    {{ PYTHON }} -m tools.coverage.coverage_summary "{{ COVERAGE_DIR }}/rust.cobertura.xml" 'Rust (default scope)'
    {{ PYTHON }} -m tools.coverage.coverage_summary "{{ COVERAGE_DIR }}/rust.filtered.cobertura.xml" 'Rust (excluding test/generated/benchmark files)'
    echo
    {{ PYTHON }} -m tools.coverage.coverage_summary "{{ COVERAGE_DIR }}/csharp.cobertura.xml" 'C#'

# Test recipe configuration and recursive overrides without building or launching
[group('Tests and coverage')]
test-recipes:
    {{ PYTHON }} -m tools.recipes.test_recipes

# Test the mod and daemon environment reference scope
[group('Tests and coverage')]
test-reference:
    {{ PYTHON }} -m tools.docs.test_reference
