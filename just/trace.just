set shell := ["bash", "-euc"]
set quiet
set default-list
set working-directory := '..'

# Capture new mod latency entries (pass true for sidecar mode)
capture sidecar='false':
    #!/usr/bin/env bash
    set -euo pipefail
    "$JUST_CMD" --justfile "$JUST_ROOT" build daemon
    trace_log="$TRACE_LOG"
    if [ -z "$trace_log" ]; then
        mode=()
        if [ {{ quote(sidecar) }} = true ]; then mode=(--sidecar); fi
        profile_path=$(SLOPWORLD_PROFILE="${PROFILE:-${SLOPWORLD_PROFILE:-}}" "$RUNNER" "${mode[@]}" paths profile)
        trace_log="$profile_path/SlopWorld-trace.log"
    fi
    exec $PYTHON -m tools.trace.capture --log "$trace_log" --seconds "$TRACE_SECONDS" --label "$TRACE_LABEL" --output "$TRACE_OUT"

# Summarize captured trace windows
summary:
    $PYTHON -m tools.trace.summary $TRACE_FILES
