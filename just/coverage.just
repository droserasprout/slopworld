set shell := ["bash", "-euc"]
set quiet
set default-list
set working-directory := '..'

# Collect Python, Rust and C# coverage
all:
    "$JUST_CMD" --justfile "$JUST_ROOT" _tools::coverage _daemon::coverage _mod::coverage

# Summarize saved coverage reports
summary:
    if [ -f "${COVERAGE_DIR}/python.cobertura.xml" ]; then ${PYTHON} -m tools.coverage.coverage_summary "${COVERAGE_DIR}/python.cobertura.xml" 'Python'; fi
    "$JUST_CMD" --justfile "$JUST_ROOT" coverage summary-rust
    echo
    "$JUST_CMD" --justfile "$JUST_ROOT" coverage summary-mod

# Summarize saved daemon coverage reports
summary-rust:
    ${PYTHON} -m tools.coverage.coverage_summary "${COVERAGE_DIR}/rust.cobertura.xml" 'Rust (default scope)'
    ${PYTHON} -m tools.coverage.coverage_summary "${COVERAGE_DIR}/rust.filtered.cobertura.xml" 'Rust (excluding test/generated/benchmark files)'

# Summarize saved game-free C# coverage
summary-mod:
    ${PYTHON} -m tools.coverage.coverage_summary "${COVERAGE_DIR}/csharp.cobertura.xml" 'C#'

# Remove coverage output
clean:
    rm -rf "$COVERAGE_DIR"
