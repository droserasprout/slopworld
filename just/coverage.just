# Collect Python, Rust and C# coverage
[group('Check and fix')]
coverage: tools-coverage daemon-coverage mod-coverage

# Summarize saved coverage reports
[group('Check and fix')]
coverage-summary: _coverage-summary-python coverage-summary-rust coverage-summary-mod

[private]
_coverage-summary-python:
    if [ -f "${COVERAGE_DIR}/python.cobertura.xml" ]; then uv run --locked python -m tools.coverage.coverage_summary "${COVERAGE_DIR}/python.cobertura.xml" 'Python'; fi

# Summarize saved daemon coverage reports
[private]
coverage-summary-rust:
    uv run --locked python -m tools.coverage.coverage_summary "${COVERAGE_DIR}/rust.cobertura.xml" 'Rust (default scope)'
    uv run --locked python -m tools.coverage.coverage_summary "${COVERAGE_DIR}/rust.filtered.cobertura.xml" 'Rust (excluding test/generated/benchmark files)'

# Summarize saved game-free C# coverage
[private]
coverage-summary-mod:
    uv run --locked python -m tools.coverage.coverage_summary "${COVERAGE_DIR}/csharp.cobertura.xml" 'C#'

# Remove coverage output
[private]
clean-coverage:
    rm -rf "$COVERAGE_DIR"
