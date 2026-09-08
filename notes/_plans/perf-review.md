# Performance review plan

Reduce the largest recurring costs found in the daemon and mod without changing the wire
contract or visible behavior. This is an implementation plan, not a runtime profile: the first
step is to establish measurements before changing hot paths.

The watched-terminal rendering work is covered by the daemon frame-path items below. This plan
owns the broader ordering, WebSocket cost, UI repaint work, and interaction-time operations.

## Baseline

Add or extend measurements that can run through `make` and record median and p95 timings for:

- emulator render, frame hashing, ANSI stripping, and WebSocket serialization;
- 120x34 terminal cases: no output, cursor-only movement, one-row edits, and full redraws;
- one, four, and eight watched sessions and clients;
- `ContentTreeView` rows visited versus rows drawn;
- `UiListView` rows copied and drawn;
- routed-sidebar rebuilds and terminal cache repaints;
- HTTP/WebSocket completion backlog and per-frame allocations where practical.

The deterministic daemon benchmark is available through `make bench-daemon` (or `make bench`);
use `BUILD=release` for release timings. Runtime counters and diagnostic timing add overhead only
when `SLOPWORLD_DEBUG=1` is set.

Use the existing `slopd::perf` lanes for `frame`, `websocket-send`, and `retick`, and extend the
opt-in mod diagnostics rather than logging every frame by default. Keep the wire format and the
current client-side row cache unchanged during the baseline.

## Priority 1: daemon frame path

Implement the daemon frame-path work:

1. Cache serialized rows, row hashes, and the aggregate content hash in `SessionEmu`.
2. Consume terminal damage ranges and rebuild only affected rows.
3. Filter cursor-only damage so it does not reserialize content.
4. Avoid stripping ANSI from the complete viewport when only the classification tail is needed.
5. Keep full-render fallback for resize, scroll, alternate-screen, and uncertain damage state.

The first implementation should preserve complete `ScreenView.lines` arrays. A delta protocol is a
separate project because the WebSocket pump coalesces frames and can drop intermediate snapshots.

## Priority 1: WebSocket serialization

Measure how much time and allocation is spent in `serde_json::to_string` for identical screen
events sent to multiple clients. If safe for the event scope and authorization rules, encode a
shared root event once and reuse the resulting bytes. Otherwise cache only reusable screen/frame
components while retaining per-client filtering.

Acceptance criteria:

- clients receive the same JSON shape and metadata as before;
- scoped events cannot leak another session or pane;
- coalescing and reconnect behavior remain unchanged;
- serialization work scales with changed frames rather than client count where possible.

## Priority 1: mod repaint work

### Content trees

Change `ContentTreeView` from “scan all flattened rows, then skip invisible rows” to a visible
range lookup. Preserve row ordering, hit testing, selection, reveal behavior, and expansion
semantics. Rebuild the flattened row cache only when the tree revision changes.

### Generic lists

Give `UiListView<T>` the same visible-range behavior already used by `TasksView`. Avoid
`Rows.ToList()` on every repaint; use a cached snapshot or an indexed source with an explicit
revision. Precompute project session counts once per session revision instead of counting all
sessions once per project row.

### Sidebar routing

Compute routed sessions once per sidebar repaint or layout revision. Cache the filtered/sorted
result, calculate its height once, and pass that height into the tree body instead of rebuilding
and resorting the list from multiple geometry queries.

### Terminal fallback

Instrument cache misses before changing rendering. If broad invalidations are common, reduce
`GUI.Label` and substring work in `PaintRows`/`DrawRun`, while preserving the existing texture
cache and selective changed-row repaint behavior.

## Priority 2: transport and interaction costs

- Reuse or pool the per-frame WebSocket event collections in `HubTransport` if measurements show
  persistent GC pressure.
- Keep HTTP and WebSocket per-frame caps as backpressure protection; optimize the producer first
  if the queues are routinely full.
- Profile Git refreshes separately from frame time. Large untracked trees can trigger many
  `git diff --no-index` subprocesses during numstat collection; preserve the existing result
  cap and concurrency limit while reducing redundant refreshes where safe.
- Measure Files browsing, Git refresh, and workspace search as interaction latency. Do not move
  these user-triggered operations into the frame-hot-path work without evidence.
- Measure `retick` with large session/rule counts before changing its locking or classification
  flow.

## Tests and validation

Add focused game-free tests for:

- unchanged, cursor-only, single-row, style-only, and full-screen terminal updates;
- resize, scroll, alternate-screen, hyperlinks, wide characters, and metadata-only frames;
- visible-range tree/list drawing, hit testing, selection, and empty states;
- routed-session filtering, sorting, height, and cache invalidation;
- WebSocket event scoping, coalescing, serialization, and reconnect behavior.

Run repository checks through Make:

```text
make format
make test
make lint
```

Do not use game launches or screenshots as part of this plan. If runtime confirmation is needed,
collect it later with the opt-in diagnostics and the repository's normal host workflow.

## Acceptance

The work is ready when:

- watched terminal renders avoid full-grid allocation and full-viewport rescans for partial damage;
- unchanged or cursor-only frames do not repeat expensive content serialization;
- repeated clients do not cause avoidable duplicate JSON encoding;
- tree and list repaints visit approximately the visible row count, not total row count;
- sidebar layout queries do not repeat filtering and sorting;
- interaction operations remain bounded and do not regress correctness;
- `make format`, `make test`, and `make lint` pass with the wire contract unchanged.

Recommended order: baseline, daemon damage/cache work, WebSocket serialization, tree/list
virtualization, sidebar caching, terminal fallback tuning, then interaction-time profiling.
