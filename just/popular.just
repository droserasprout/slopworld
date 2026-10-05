# Common commands

# Build the daemon and mod
[group('Common commands')]
all: daemon mod

# Format Python, Rust and C# sources
[group('Common commands')]
format: format-tools format-daemon format-mod

# Check Python, Rust and C# sources
[group('Common commands')]
lint: lint-tools lint-daemon lint-mod

# Run all game-free tests (requires tmux and less)
[group('Common commands')]
test: test-daemon test-mod test-tools test-pager

# Run game-free CI checks with coverage
[group('Common commands')]
ci: check-generated check-format-daemon check-format-mod lint-tools test-tools test-pager lint-daemon coverage-daemon coverage-mod

# Run the game-free daemon, C# and IPC benchmarks into shared CSV results
[group('Common commands')]
bench:
    JUST_CMD="{{ just_executable() }}" {{ PYTHON }} -m bench.report run --suite gamefree --build "{{ BUILD }}" {{ if BENCH_RUN != '' { '--run "' + BENCH_RUN + '"' } else { '' } }}

# Install the daemon, runner and mod
[group('Common commands')]
install: install-daemon install-runner install-mod

# Launch the game through the runner
[group('Common commands')]
run: daemon
    {{ RUNNER }} --game "{{ RIMWORLD }}" {{ if PROFILE != '' { '--profile "' + PROFILE + '"' } else { '' } }}
