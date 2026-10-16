# Restore the locked Python tooling environment
[group('tools')]
tools-restore:
    uv sync --locked

# Apply import sorting and formatting
[group('tools')]
tools-format:
    uv run --locked ruff check --select I --fix tools bench/runner.py bench/test_runner.py mac slopcar/*.py
    uv run --locked ruff format tools bench/runner.py bench/test_runner.py mac slopcar/*.py

# Apply automatically correctable lint fixes
[group('tools')]
tools-fix:
    uv run --locked ruff check --fix tools bench/runner.py bench/test_runner.py mac slopcar/*.py

# Check formatting, Ruff rules and strict types without modifying sources
[group('tools')]
tools-lint:
    uv run --locked ruff format --check tools bench/runner.py bench/test_runner.py mac slopcar/*.py
    uv run --locked ruff check tools bench/runner.py bench/test_runner.py mac slopcar/*.py
    uv run --locked mypy

# Run Python tests; pass pytest paths/options to select a subset
[positional-arguments]
[group('tools')]
tools-test *args:
    uv run --locked python -m pytest "$@"

# Collect Python branch coverage
[group('tools')]
tools-coverage:
    mkdir -p "$COVERAGE_DIR"
    uv run --locked python -m pytest --cov=tools --cov=mac --cov=slopcar --cov=bench --cov-report=term-missing --cov-report=xml:"$COVERAGE_DIR/python.cobertura.xml"

# Check pager geometry with isolated tmux and less
[group('tools')]
tools-test-pager:
    uv run --locked python -m tools.terminal.pager_geometry

# Print required and optional host tools
[group('tools')]
tools-check-requirements:
    uv run --locked python -m tools.host.check_reqs
