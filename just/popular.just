# Common commands

# Build the daemon and mod
[group('Common commands')]
all: daemon mod

# Format Rust and C# sources
[group('Common commands')]
format: format-daemon format-csharp

# Check Rust and C# sources
[group('Common commands')]
lint: lint-daemon lint-mod

# Run all game-free tests (requires tmux and less)
[group('Common commands')]
test: test-daemon test-mod test-tools test-pager

# Run game-free CI checks with coverage
[group('Common commands')]
ci: coverage test-tools test-pager lint-daemon check-format-csharp check-generated

# Run the game-free daemon, C# and IPC benchmarks into shared CSV results
[group('Common commands')]
bench:
    JUST_CMD="{{ just_executable() }}" {{ PYTHON }} tools/bench-report.py run --suite gamefree --build "{{ BUILD }}" {{ if BENCH_RUN != '' { '--run "' + BENCH_RUN + '"' } else { '' } }}

# Install the daemon, runner and mod
[group('Common commands')]
install: install-daemon install-runner install-mod

# Launch the game through the runner
[group('Common commands')]
run: daemon
    {{ RUNNER }} --game "{{ RIMWORLD }}" {{ if PROFILE != '' { '--profile "' + PROFILE + '"' } else { '' } }}
