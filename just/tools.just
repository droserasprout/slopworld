set shell := ["bash", "-euc"]
set quiet
set default-list
set working-directory := '..'

# Restore the locked Python tooling environment
restore:
    $UV sync --locked

# Apply import sorting and formatting
format:
    $UV run --locked ruff check --select I --fix $PYTHON_TOOLS
    $UV run --locked ruff format $PYTHON_TOOLS

# Apply automatically correctable lint fixes
fix:
    $UV run --locked ruff check --fix $PYTHON_TOOLS

# Check formatting, Ruff rules and strict types without modifying sources
lint:
    $UV run --locked ruff format --check $PYTHON_TOOLS
    $UV run --locked ruff check $PYTHON_TOOLS
    $UV run --locked mypy

# Run Python tests; pass pytest paths/options to select a subset
[positional-arguments]
test *args:
    if [ "$#" -eq 0 ]; then set -- tools mac; fi; $PYTHON -m pytest "$@"

# Collect Python branch coverage
coverage:
    mkdir -p "$COVERAGE_DIR"
    $PYTHON -m pytest tools mac --cov=tools --cov=mac --cov-report=term-missing --cov-report=xml:coverage/python.cobertura.xml

# Check pager geometry with isolated tmux and less
test-pager:
    $PYTHON -m tools.terminal.pager_geometry

# Print required and optional host tools
check-requirements:
    $PYTHON -m tools.host.check_reqs
