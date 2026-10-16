# Capture new mod latency entries (pass true for sidecar mode)
[group('Specialist commands')]
[positional-arguments]
trace-capture sidecar='false' log='' *args: daemon-build
    #!/usr/bin/env bash
    set -euo pipefail
    shift 2
    trace_log={{ quote(log) }}
    if [ -z "$trace_log" ]; then
        mode=()
        if [ {{ quote(sidecar) }} = true ]; then mode=(--sidecar); fi
        profile_path=$(SLOPWORLD_PROFILE="${PROFILE:-${SLOPWORLD_PROFILE:-}}" "$RUNNER" "${mode[@]}" paths profile)
        trace_log="$profile_path/SlopWorld-trace.log"
    fi
    exec uv run --locked python -m tools.trace.capture --log "$trace_log" --output bench/results/current/raw/trace/current.log "$@"

# Summarize captured trace windows
[group('Specialist commands')]
[positional-arguments]
trace-summary *files:
    if [ "$#" -eq 0 ]; then set -- bench/results/*/raw/trace/*.log; fi; uv run --locked python -m tools.trace.summary "$@"
