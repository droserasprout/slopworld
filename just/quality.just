CSHARP_FORMAT_FOLDERS := 'mod/Source/SlopWorld mod/Tests bench/ipc/csharp bench/terminal-input/tests/http_transport'
CSHARP_FORMAT_COMMAND := DOTNET + ' format whitespace . --folder --exclude obj Client/Generated'
PYTHON_TOOLS := 'tools bench/runner.py bench/test_runner.py mac slopcar/*.py'

# Formatting

# Format Rust sources
[group('Formatting')]
format-daemon:
    cd slopd && {{ CARGO }} fmt

# Format C# production, test and benchmark sources
[group('Formatting')]
format-mod:
    for source in {{ CSHARP_FORMAT_FOLDERS }}; do (cd "$source" && {{ CSHARP_FORMAT_COMMAND }}) || exit "$?"; done

# Check formatting across C# production, tests and benchmarks
[group('Formatting')]
check-format-mod:
    for source in {{ CSHARP_FORMAT_FOLDERS }}; do (cd "$source" && {{ CSHARP_FORMAT_COMMAND }} --verify-no-changes) || exit "$?"; done

# Check Rust source formatting without compiling
[group('Formatting')]
check-format-daemon:
    cd slopd && {{ CARGO }} fmt --check

# Lint

# Check Rust formatting and Clippy
[group('Lint')]
lint-daemon: check-format-daemon
    cd slopd && {{ CARGO }} clippy --all-targets -- -D warnings

# Build and check C# formatting (requires game assemblies)
[group('Lint')]
lint-mod:
    "{{ just_executable() }}" BUILD=release MOD_WARNINGS_AS_ERRORS=true mod check-format-mod

# Format Python code, check Ruff rules, and strictly type-check tools
[group('Lint')]
lint-tools: format-tools
    {{ UV }} run --locked ruff check {{ PYTHON_TOOLS }}
    {{ UV }} run --locked mypy

# Fix automatically correctable Python package lint findings
[group('Formatting')]
fix-tools:
    {{ UV }} run --locked ruff check --fix {{ PYTHON_TOOLS }}

# Format the Python package with Ruff
[group('Formatting')]
format-tools:
    {{ UV }} run --locked ruff check --select I --fix {{ PYTHON_TOOLS }}
    {{ UV }} run --locked ruff format {{ PYTHON_TOOLS }}
