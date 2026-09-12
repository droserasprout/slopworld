# CPU and idle-state plan

Establish race-safe idle classification before optimizing terminal parsing,
message decoding, metadata polling, and idle wakes. Apply phases in order.

## 0. Establish and fix the idle-state contract

Sources: [session state](../slopd/src/manager/session_state.rs),
[frame capture](../slopd/src/manager/capture_frame.rs), and
[state notes](daemon-session-state.md).

- Observe quiet Working sessions through the daemon across `IDLE_MS`.
  Record the stripped tail, content hash, cursor position, frame metadata,
  `last_change`, `state_since`, `seq`, `retick_seq`, matched rule, retick execution,
  and emitted `Event::Sessions`. Use fixtures first; inspect an existing affected
  API session when available without altering live daemon configuration.
- Separate unchanged text plus cursor movement, metadata changes, and a static
  `esc to interrupt` line. Preserve cursor-only activity behavior and its regression
  test in `manager/capture.rs`; retain Working decay and authoritative Waiting/Idle rules.
- Test every `FrameMeta` field: cursor shape/blink, mouse/drag modes, alternate
  screen, and title. Exclude presentation-only churn from activity only when the
  reproduction supports it; retain proven activity and continue screen delivery.
- Add retick integration coverage for quiet Working-to-Idle decay and its session
  event, active output near the deadline, persistent Working/Waiting text, and
  repeated redraws that must not reset `state_since`. Keep client parsing of the
  lowercase `idle` value covered; change transport only if an emitted event is lost.
- `now_ms()` uses wall time, so paused Tokio time alone cannot advance idle decay.
  Set fixture timestamps or introduce a narrow clock seam for deterministic tests;
  avoid real sleeps. Base activity deadlines on `last_change`, not state age.
- Test output and same-name session replacement while classification awaits the
  rules lock. Before committing a result, validate the captured run/sequence and
  rules revision; stale work must not overwrite new state or mark newer output
  as classified. Keep `Live::set_state` and durable activity persistence intact.

Make the smallest fix supported by these cases and update the state note. Preserve
the rule/activity contract in the phase 4 cache. Verify quiet and active real API
sessions when available; otherwise report runtime validation as outstanding.

## 1. Retain terminal parsing when a URL is visible

- Separate immutable ANSI row parsing from autolink decoration in `Sgr.cs` and
  `TerminalRunCache.cs`. Reuse base rows even when visible text contains URLs;
  never modify cached base runs while splitting or decorating link runs.
- Retain link spans and row dependencies per screen. Invalidate changed rows
  and every connected URL span they can affect, expanding through physical row
  boundaries until stable. A fixed one-row halo is insufficient for long URLs.
- Include removed links and newly joined or split spans in invalidation. Preserve
  OSC 8 targets, absolute columns, wide characters and links across color runs.
  Mark rows whose decoration changes for repaint even if their text is unchanged.
- Invalidate on dimensions, theme/font revision and incompatible screen/history
  changes. Keep full parsing as the correctness fallback for broad changes.
- Test URL addition/removal, multi-row URLs, delimiter edits at boundaries,
  explicit OSC 8 links, resizing, scrolling and retained-snapshot immutability.
  Compare incremental results with full parsing across sequences of edits.
- Target: at least 80% less allocation and CPU in the 200-row static-URL sparse
  benchmark, with no material plain-screen regression. Also measure broad edits.

## 2. Coalesce live frames before full JSON decoding

- Add an allocation-light envelope reader alongside `Json.cs` to extract event
  type, screen name, offset and request ID while skipping terminal payload values.
  Handle escaped strings, nested values and arbitrary property order; do not use
  substring searches or regex to recognize JSON fields.
- Let `HubEventBatch` retain raw messages and select the last live frame per
  session within its existing 32-message budget. Fully decode retained messages
  only, dispatching them in original order. Keep history, request replies and
  control messages under the existing ordering semantics.
- Ambiguous or unsupported envelopes must fall back to the full parser. Validate
  the skipped JSON structure or fall back so malformed messages still reach the
  error handler rather than silently replacing valid frames.
- Extend `IdleWorkTests.Messages` with reordered/escaped fields, multiple sessions,
  malformed input, missing/default fields, history replies, batch boundaries and
  disconnect callbacks. Differentially compare delivery with the current batcher.
- Target: at least 80% less allocation in the 32-frame benchmark, a clear CPU
  reduction, and no material single-frame or empty-queue regression. Track full
  decodes and discarded frames separately. This does not bound the socket queue;
  queue backpressure is owned by the [memory plan](plan-memory-leaks.md). Coordinate
  envelope/coalescing semantics with that work; do not count this phase as a memory bound.

## 3. Batch daemon host metadata queries

`manager/sessions.rs::refresh_host_metadata` currently queries each live host
terminal separately with bounded concurrency.

- Add one formatted `list-panes -a` query in `tmux.rs`; return pane identity, cwd
  and foreground command, then match only the expected session/pane targets.
  Retain robust framing for spaces, delimiters, Unicode and newlines in paths.
- Preserve independent missing fields, host filtering, path persistence and
  process-state updates. A failed query must not erase known metadata. Launch
  no query when there are no live host terminals.
- Test parsing and session mapping, multi-pane sessions, disappearance during a
  poll, command-only/path-only changes and query failure with a fake tmux runner.
- Acceptance: one process per due poll instead of N, zero for N=0. Measure
  subprocess count and elapsed poll time at 1, 8 and 32 host terminals; elapsed
  time alone is not a CPU measurement.

## 4. Remove redundant daemon classification and idle wakes

- Cache regex matches by visible-text revision and rules revision; apply the
  phase 0 activity/decay contract separately using `last_change`. Cursor-position
  changes must not reset activity. Preserve matching-rule precedence and the
  metadata decisions from phase 0. Audit every config/rule reload path and restart
  hydration so unchanged screens are reclassified when rules change, even when
  `seq == retick_seq` and the session is already Idle.
- Test persistent Waiting and rule-matched Working states, fallback Working to
  Idle, output near the deadline, rules reload and replacement sessions. Stable
  rule matches should require no regex work on subsequent unchanged reticks.
- Replace recurring clean-screen capture timers with demand-driven deadlines.
  Output, subscription changes and pending clipboard delivery must wake the
  reader; keep watched 16 ms cadence and existing unwatched rendering limits.
  Do not alter scheduling of unrelated daemon maintenance.
- Capture scheduling in `manager/capture_reader.rs` mixes Tokio deadlines with
  `std::time::Instant` for the unwatched draw limit. Use one controllable monotonic
  clock or a narrow test seam before relying on paused Tokio time.
- Test with controlled time: clean panes do not repeatedly wake, output is
  delivered on time, a newly watched dirty pane flushes promptly, clipboard
  completion cannot strand pending content, and exits cancel pending work.

## Delivery and verification

Implement each numbered phase as a separate reviewable change, in order. Record
benchmark deltas in [CPU optimization](perf-cpu-optimization.md) and keep comments
beside the changed algorithms. Carry the phase 0 regressions through phase 4.
Run `make BUILD=release test-mod` and `make BUILD=release bench-mod` for C# phases;
run `make test-daemon` and `make BUILD=release bench-daemon` for Rust phases.
Use `make lint-mod` or `make lint-daemon` for the affected production language;
the mod build needs the configured RimWorld assemblies.
Use `make format-mod` or `make format-daemon` when formatting changed code, and
finish with `make test` for the full game-free suite. Report baseline failures
separately. Benchmarks establish cost changes, not correctness or live latency.

Defer broad simulation changes until a runtime profile identifies a larger cost. No game launch
or image inspection is part of this plan. If live verification is requested, use existing
`PerfTrace` counters to check parsing, repaint work, backlog and input latency.
