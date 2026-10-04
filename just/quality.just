CSHARP_FORMAT_INCLUDE := 'mod/Source/SlopWorld mod/Tests bench/ipc/csharp bench/terminal-input/tests/http_transport/Probe.cs'
CSHARP_FORMAT_EXCLUDE := 'mod/Source/SlopWorld/obj mod/Tests/obj bench/ipc/csharp/obj mod/Source/SlopWorld/Client/Generated'
CSHARP_FORMAT_COMMAND := DOTNET + ' format whitespace . --folder --include ' + CSHARP_FORMAT_INCLUDE + ' --exclude ' + CSHARP_FORMAT_EXCLUDE

# Formatting

# Format Rust sources
[group('Formatting')]
format-daemon:
    cd slopd && {{ CARGO }} fmt

# Format C# production, test and benchmark sources
[group('Formatting')]
format-csharp:
    {{ CSHARP_FORMAT_COMMAND }}

# Check formatting across C# production, tests and benchmarks
[group('Formatting')]
check-format-csharp:
    {{ CSHARP_FORMAT_COMMAND }} --verify-no-changes

# Check Rust source formatting without compiling
[group('Formatting')]
check-format-daemon:
    cd slopd && {{ CARGO }} fmt --check

# Lint

# Check Rust formatting and Clippy
[group('Lint')]
lint-daemon: api-contract check-format-daemon
    cd slopd && {{ CARGO }} clippy --all-targets -- -D warnings

# Build and check C# formatting (requires game assemblies)
[group('Lint')]
lint-mod:
    "{{ just_executable() }}" BUILD=release MOD_WARNINGS_AS_ERRORS=true mod check-format-csharp

# Format and check Python package code with Ruff
[group('Lint')]
lint-tools: format-tools
    {{ UV }} run --locked ruff check tools

# Fix automatically correctable Python package lint findings
[group('Formatting')]
fix-tools:
    {{ UV }} run --locked ruff check --fix tools

# Format the Python package with Ruff
[group('Formatting')]
format-tools:
    {{ UV }} run --locked ruff check --select I --fix tools
    {{ UV }} run --locked ruff format tools
