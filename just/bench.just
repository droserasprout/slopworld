# Benchmarks

# Run the daemon benchmark into shared CSV results
[group('Benchmarks')]
bench-daemon:
    JUST_CMD="{{ just_executable() }}" {{ PYTHON }} -m bench.report run --suite daemon --build "{{ BUILD }}" {{ if BENCH_RUN != '' { '--run "' + BENCH_RUN + '"' } else { '' } }}

# Run the C# helper benchmark into shared CSV results
[group('Benchmarks')]
bench-mod:
    JUST_CMD="{{ just_executable() }}" {{ PYTHON }} -m bench.report run --suite mod --build "{{ BUILD }}" {{ if BENCH_RUN != '' { '--run "' + BENCH_RUN + '"' } else { '' } }}

# Run production Protobuf IPC into shared CSV results
[group('Benchmarks')]
bench-ipc:
    JUST_CMD="{{ just_executable() }}" {{ PYTHON }} -m bench.report run --suite ipc --build "{{ BUILD }}" {{ if BENCH_RUN != '' { '--run "' + BENCH_RUN + '"' } else { '' } }}

[group('Benchmarks')]
[private]
bench-build: api-contract mod-deps
    {{ PYTHON }} -m bench.runner build

# Render saved CSV results; BENCH_BASELINE and BENCH_MODE=relative compare runs
[group('Benchmarks')]
bench-report:
    {{ PYTHON }} -m bench.report report --run "{{ BENCH_RUN }}" {{ if BENCH_BASELINE != '' { '--baseline "' + BENCH_BASELINE + '"' } else { '' } }} {{ if BENCH_MODE != '' { '--mode "' + BENCH_MODE + '"' } else { '' } }} {{ if BENCH_REPORT_OUTPUT != '' { '--output "' + BENCH_REPORT_OUTPUT + '"' } else { '' } }}

# Write the latest benchmark report from BENCH_RUN
[group('Benchmarks')]
bench-latest:
    {{ PYTHON }} -m bench.report report --run "{{ BENCH_RUN }}" --latest {{ if BENCH_FALLBACK_RUN != '' { '--fallback-run "' + BENCH_FALLBACK_RUN + '"' } else { '' } }}

# Run focused desktop terminal input into shared CSV results
[group('Benchmarks')]
bench-terminal:
    {{ PYTHON }} bench/terminal-input/terminal-input-bench.py {{ if BENCH_RUN != '' { '--run "' + BENCH_RUN + '"' } else { '' } }} {{ if BENCH_PHASE != '' { '--phase "' + BENCH_PHASE + '"' } else { '' } }} {{ if BENCH_FILL_HISTORY =~ '^(1|true)$' { '--fill-history' } else { '' } }} {{ if BENCH_MULTIPLIER != '' { '--multiplier "' + BENCH_MULTIPLIER + '"' } else { '' } }} {{ if BENCH_PREPARE_SECONDS != '' { '--prepare-seconds "' + BENCH_PREPARE_SECONDS + '"' } else { '' } }} {{ if BENCH_BACKEND != '' { '--backend "' + BENCH_BACKEND + '"' } else { '' } }} {{ if BENCH_TRACE_LOG != '' { '--log "' + BENCH_TRACE_LOG + '"' } else { '' } }}

# Fill an empty host tab, then measure typing repaint reasons
[group('Benchmarks')]
bench-terminal-typing:
    "{{ just_executable() }}" BENCH_PHASE=typing BENCH_FILL_HISTORY=1 bench-terminal
