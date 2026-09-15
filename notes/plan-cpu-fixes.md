# CPU work still open

Only classification correctness/cache and idle-wake scheduling remain open. Working-rule
decay and cursor-position-only non-activity already exist.

1. **Classification races and regression coverage.** In `manager/session_state.rs` and
   capture code, test quiet decay, authoritative Waiting/Idle, metadata-only changes and
   rules reload. Classification awaits locks: validate run/sequence/rules revision before
   commit so old work cannot overwrite newer output or mark it classified. Use deterministic
   clocks/timestamps, preserving `state_since` and durable activity restoration.
2. **Classification cache and idle wakes.** Cache rule matches by text/rules revision while
   applying activity decay separately. Every reload/adoption path must invalidate correctly.
   Replace clean-screen recurring wakeups with demand-driven deadlines, preserving output,
   subscription and clipboard wakeups. Use one controllable monotonic clock; test quiet,
   newly watched, dirty, clipboard-completion and exit cases.

Run affected language tests/lint and release benchmarks through make, then `make test`.
Record comparable deltas; helper timing does not establish Unity CPU or live latency.
Use `make lint-prose` for note edits. Do not launch the game or inspect images without a
direct request. Performance targets above are acceptance goals, not measured improvements.
Update focused ownership notes only if contracts change; remove completed stages from this
plan and delete it when no unresolved work remains.
