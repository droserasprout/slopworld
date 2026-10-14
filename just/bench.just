set shell := ["bash", "-euc"]
set quiet
set default-list
set working-directory := '..'

# Run daemon, mod, ipc or gamefree benchmarks; pass report CLI options
[positional-arguments]
run what='gamefree' *args:
    shift; $PYTHON -m bench.report run --suite {{ quote(what) }} --build "$BUILD" ${BENCH_RUN:+--run "$BENCH_RUN"} "$@"

# Render saved measurements; pass report CLI options
[positional-arguments]
report *args:
    $PYTHON -m bench.report report ${BENCH_RUN:+--run "$BENCH_RUN"} "$@"

# Write the latest report
[positional-arguments]
latest *args:
    $PYTHON -m bench.report report --latest ${BENCH_RUN:+--run "$BENCH_RUN"} "$@"

# Measure desktop terminal input; pass terminal benchmark CLI options
[positional-arguments]
terminal *args:
    "$JUST_CMD" --justfile "$JUST_ROOT" build daemon
    $PYTHON bench/terminal-input/terminal-input-bench.py ${BENCH_RUN:+--run "$BENCH_RUN"} "$@"

# Test benchmark runners, reports and terminal-input helpers
test:
    $PYTHON -m pytest bench/test_runner.py bench/test_bench_report.py
    $PYTHON -m unittest discover -s bench/terminal-input -p 'test_*.py'
