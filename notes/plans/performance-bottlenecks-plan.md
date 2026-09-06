# Performance bottlenecks plan

Status: proposed. Scope: the Unity mod and `slopd` daemon.

The current review is static, not a runtime profile. The most credible risks
are Unity-main-thread I/O and unbounded queue drains, the full terminal-frame
pipeline, repeated sidebar/worksite scans, expanded-tree traversal, and
sequential host polling in the daemon. Keep the wire protocol and visible
behavior unchanged while reducing work and allocation pressure.

Baseline verified 2026-09-06: `make test`, `make lint-mod`, and
`make lint-prose` pass. The aggregate `make lint` gate currently stops in
`cargo fmt --check` at `slopd/src/manager/sessions.rs:163`; this is an
existing daemon formatting mismatch, before the mod lint target runs. Add
runtime measurements before changing the larger hot paths;
`SLOPWORLD_SCROLL_DEBUG=1` already provides aggregate scroll timing.

## Phase 0: instrument the hot paths

Add temporary or low-noise timing/counter markers around:

- `SessionHub.Update`, completion pumping, and WebSocket event draining;
- terminal frame decode, SGR/link parsing, cache blitting, and full painting;
- sidebar layout/row drawing and Worksite assignment/free/sweep;
- daemon frame capture/delta generation, WebSocket sends, and `retick` host
  metadata polling.

Record queue depths, frame dimensions, parsed rows, visible rows, allocation
counts where available, and the number of tmux subprocesses per retick. Use
the Unity Profiler and daemon logs/tracing to separate frequency from worst
case latency.

## Phase 1: remove blocking work from the Unity frame

Targets:

- `Client/MiniWebSocket.cs` connect, handshake, and frame writes;
- `Client/SessionHub/HubTransport.cs` event dispatch;
- `Client/DaemonClient.cs` completion dispatch.

Move connect/handshake and outbound writes behind a background transport or
dedicated writer. Keep Unity callbacks on the main thread, but give completion
and event dispatch an explicit per-frame budget. Preserve control/input
ordering; coalesce only replaceable screen updates. Make reconnect state
transitions explicit so a failed background attempt cannot trigger repeated
main-thread work.

Acceptance criteria:

- no socket connect, handshake, write, flush, or read wait runs on the Unity
  thread;
- queue drains have a bounded work budget and report backlog without dropping
  non-replaceable control events;
- reconnect, input, and screen-update ordering remain unchanged;
- a burst of completions/events cannot monopolize one frame.

## Phase 2: reduce terminal frame work

Targets:

- daemon capture/render and frame delta generation;
- `ScreenBuf` JSON decoding and vertical-shift detection;
- `TerminalRunCache`, `Sgr`, and terminal painting.

First preserve parsed runs for unchanged rows and carry row revisions or a
screen revision through the frame path. Avoid joining and stripping the full
screen when the change classification does not require it. Keep the daemon's
existing per-client screen coalescing, but avoid rebuilding identical payloads
and retain the newest frame under backpressure.

Then make link detection and SGR parsing operate on changed or candidate rows,
and replace repeated front insertion in SGR run merging with an append-friendly
path. Keep the render-texture cache fallback observable; do not silently turn a
cache failure into permanent full-screen painting without a diagnostic.

Acceptance criteria:

- unchanged terminal rows do not reparse or repaint;
- normal frames avoid a full-screen `join`/strip/link scan where possible;
- screen updates remain ordered and scrollback/history behavior is unchanged;
- measured parse, paint, allocation, and network payload costs improve for
  small changes on large screens.

## Phase 3: cache sidebar layout inputs

Targets: `Patches/AgentSidebar/` and `SidebarRowRenderer`.

Parse the status filter once when its setting changes. Build session/project/
worker counts and visible-parent sets in one indexed pass instead of scanning
all sessions and worker lists once per project. Rebuild layout only when the
session/project/filter/width inputs change, and memoize row titles, status
labels, and measured widths until their source revision changes.

Preserve the current ordering, folded groups, ghost rows, status filtering,
hover behavior, and colonist-bar geometry.

## Phase 4: bound Worksite search and simulation scans

Targets: `Sim/Worksite.cs` and `Sim/WorksiteErrands.cs`.

Keep candidate frames indexed by map/area and invalidate the index when a
relevant frame, reservation, construction state, or pawn skill changes. Avoid
repeating footprint and reachability checks for the same placement attempt;
budget or stagger expensive path checks. Split the once-per-second sweep into
bounded work when a map contains many frames.

Preserve vanilla reservation, skill, shunning, priority, and blueprint rules.
Measure active-colony tick time before and after; do not optimize inactive
worksites at the expense of simpler correctness.

## Phase 5: virtualize expanded content trees

Targets: `UI/ContentTreeView.cs` and `UI/FilesView.cs`.

Cache source groups and flatten only the expanded/visible portion needed for
the current scroll window. Replace recursive full-tree measurement with cached
row heights or subtree counts invalidated by expansion and load changes. Keep
async loading and row actions caller-owned.

Acceptance criteria: large expanded trees measure and draw proportional to the
visible window plus a small look-ahead, while expansion, loading, selection,
and file actions behave identically.

## Phase 6: shorten daemon retick and command lanes

Targets: `manager/session_state.rs`, `manager/sessions.rs`, `tmux.rs`, and the
WebSocket handlers.

Batch host metadata queries where tmux permits it, or run bounded concurrent
queries instead of sequential subprocesses. Poll host path/process metadata at
the necessary cadence rather than on every state retick. Keep the receive loop
responsive when a scroll capture or other command is expensive by separating
long capture work from the socket's command intake, with per-client ordering
preserved.

Do not turn tmux or emulator work into unbounded Tokio tasks. Bound concurrency
and retain cancellation behavior during session shutdown.

## Verification and completion

After each phase, run `make test` and the relevant `make lint` target. Recheck
Unity main-thread timings, terminal frame latency, sidebar draw cost, active
Worksite tick cost, daemon retick duration, queue depth, and memory allocation
behavior against the Phase 0 baseline. Update this note with measured results
and mark each phase implemented only after behavior and performance evidence
are recorded.
