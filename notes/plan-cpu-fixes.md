# CPU work still open

Apply these stages in order; [performance notes](perf-cpu-optimization.md) identify the
baseline. Working-rule decay and cursor-position-only non-activity already exist.

1. **Classification races and regression coverage.** In `manager/session_state.rs` and
   capture code, test quiet decay, authoritative Waiting/Idle, metadata-only changes and
   rules reload. Classification awaits locks: validate run/sequence/rules revision before
   commit so old work cannot overwrite newer output or mark it classified. Use deterministic
   clocks/timestamps, preserving `state_since` and durable activity restoration.
2. **Incremental URL rows.** `TerminalRunCache` currently reparses linked screens. Cache base
   ANSI rows separately from decorations. Invalidate entire connected URL spans, including
   removed/newly joined spans; a fixed neighbor halo is insufficient. Preserve OSC 8,
   wide columns and snapshot immutability. Differential tests must match full parsing across
   edits, resize and scroll. Target 80% less CPU/allocation in the sparse static-URL fixture
   without materially regressing plain or broadly changed screens.
3. **Coalesce before decoding.** `HubEventBatch` should inspect lightweight JSON envelopes,
   then decode retained live frames only within its existing batch budget. Preserve control,
   reply and history order. Escapes/property order/nesting need structural parsing; malformed
   or ambiguous envelopes must reach full parsing/error handling, not suppress valid frames.
   Compare delivery with the old batcher. Target 80% less allocation for the 32-frame fixture;
   this is separate from the already bounded transport queue.
4. **Batch host metadata.** Replace per-host tmux queries with one framed `list-panes -a`
   result. Preserve paths containing delimiters/newlines and independently missing fields.
   Failed polls retain known state. Verify zero subprocesses for no hosts, one otherwise,
   plus mapping/disappearance behavior at 1/8/32 hosts.
5. **Classification cache and idle wakes.** Cache rule matches by text/rules revision while
   applying activity decay separately. Every reload/adoption path must invalidate correctly.
   Replace clean-screen recurring wakeups with demand-driven deadlines, preserving output,
   subscription and clipboard wakeups. Use one controllable monotonic clock; test quiet,
   newly watched, dirty, clipboard-completion and exit cases.

Run affected language tests/lint and release benchmarks through make, then `make test`.
Record comparable deltas; helper timing does not establish Unity CPU or live latency.
