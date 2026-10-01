# Diagnostics

`slopctl logs` reads game and journal output. `slopctl/logs/stream.rs` owns
bounded fan-in and child cleanup. Drop the receiver before joining producers,
and keep child cancellation available during reaping.
The book describes command options and paths.
Use `slopctl status` for API health. Service active status alone does not prove the listener
bound.
[Sandbox diagnostics](ops-debug-from-sandbox.md) explains misleading host observations.

Managed process exits save `exit.json` beside the retained launch plan in the session's
opaque private-state directory. It contains the exit status or signal, run/task identity, and
up to 200 history lines plus the viewport. The file is owner-only because terminal contents
can include private data; it is not emitted into journals or exposed through the scoped API.
Durable state keeps it until reset/delete moves that state into trash. Temporary state follows
its ordinary cleanup lifetime. Capture failure with a live pane does not create a process-exit
record. If writing fails, the dead tmux pane remains available for manual inspection.

`SLOPWORLD_DEBUG=1` enables aggregate counters and correlated input timelines in
daemon and game. Counters and timelines exclude terminal contents.
Start captures after loading and warmup.
Use `make trace-mod` with distinct labels.
Then use `make trace-summary`. Game startup or image inspection still requires an explicit request.

Compare identical session sets, geometry, focus and display pacing, repeating conditions.
Discard initial and transition records. Trace lanes overlap. Do not add root/sidebar/colonist/terminal times.
Do not treat GC collection counts as allocated bytes.
Helper benchmarks do not measure Unity CPU or end-to-end latency. See [CPU accounting](misc-cpu-threads.md).

PerfTrace samples process RSS, managed used/heap, Unity allocated/reserved/unused memory,
and GC collection totals every five seconds, even without timed work. Memory values use
bytes. Unity and process counters overlap. Do not add them. Unsupported RSS is -1.
The profiler does not force GC collections or inspect terminal objects and caches.

Eco cache release emits `[SlopWorld] eco memory released` lines even without debug tracing.
`geometryCapacityBytes` sums retained managed geometry array payloads in the released layers.
`meshes` counts their submeshes. `atlasColorBytes` estimates RGBA color storage only, excluding
depth and driver overhead. Neither is a process-RSS delta, and Unity destruction/managed GC
can occur later.
Compare settled memory samples across Eco transitions.
Restore lines identify when the game rebuilt map geometry.

Correlated input timelines use `SLOPWORLD_DEBUG=1` on both peers.
The client owns request IDs and completion. The daemon carries bounded run-scoped
observations through screen coalescing. Never subtract clocks across processes.
Unity frame end is a pre-presentation proxy, and next-frame correlation does not prove
an application echoed input. See [terminal latency](terminal-latency.md).

Terminal performance and latency diagnostics share a dedicated per-game
`SlopWorld-trace.log` in the save-data folder. The main-thread frame hook owns its
writer and flushes after the latency endpoint. Do not route high-volume records
through Verse's capped logger. The guided host input runner and measurement limits
are documented in [terminal latency](terminal-latency.md).

At high input rates, the 128-entry client and daemon trace limits can censor
requests before dispatch or capture. The daemon starts retention at queue admission.
Later screen updates do not restore evicted IDs. `complete_with_slip` is an
injector outcome, not proof of a representative latency distribution. For mixed
mouse/key dispatch capacity, run the opt-in isolated diagnostic with
`make test-daemon TEST_ARGS='benchmark_mixed_input_dispatch -- --ignored --nocapture'`.

The isolated worker-removal diagnostic
`make test-daemon TEST_ARGS='benchmark_worker_removal_scaling -- --ignored --nocapture'`
compares populations of 1/10/100 and 100 sequential deletions of stopped durable
workers with private state and grants. It measures daemon persistence and cleanup,
excluding live-process shutdown, task history, socket delivery, and the game UI.
