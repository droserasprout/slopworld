# Development tools

# Show new entries in the game's Player.log
[group('Development tools')]
logs:
    tail -f "{{ LOG }}"

export TRACE_SECONDS := env("TRACE_SECONDS", '30')
export TRACE_LABEL := env("TRACE_LABEL", 'current')
export TRACE_OUT := env("TRACE_OUT", 'bench/results/' + (if BENCH_RUN != '' { BENCH_RUN } else { 'current' }) + '/raw/trace/' + TRACE_LABEL + '.log')
# Capture new performance/latency log entries (requires tracing enabled)
[group('Development tools')]
trace-mod:
    {{ PYTHON }} -m tools.trace.capture --log "{{ TRACE_LOG }}" --seconds "{{ TRACE_SECONDS }}" --label "{{ TRACE_LABEL }}" --output "{{ TRACE_OUT }}"

export TRACE_FILES := env("TRACE_FILES", 'bench/results/*/raw/trace/*.log')
# Summarize captured windows by observed Eco/terminal/session/size state
[group('Development tools')]
trace-summary:
    {{ PYTHON }} -m tools.trace.summary {{ TRACE_FILES }}

# Print required and optional host requirements
[group('Development tools')]
check-reqs:
    RIMWORLD="{{ RIMWORLD }}" {{ PYTHON }} -m tools.host.check_reqs

# Build and install Arch package
[group('Development tools')]
pkg-arch:
    cd packaging/arch && makepkg -p PKGBUILD.local -sif

# Build user documentation
[group('Development tools')]
docs:
    cd docs && mdbook build

# Serve user documentation
[group('Development tools')]
docs-serve:
    cd docs && mdbook serve

# Reinstall and relaunch after every game exit
[group('Development tools')]
devloop:
    JUST_CMD="{{ just_executable() }}" {{ PYTHON }} -m tools.host.devloop
