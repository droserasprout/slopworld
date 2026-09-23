# Performance bottleneck investigation — 2026-09-22

Measured in the current worktree based on `7b56915513d0f03e5300da582c9a014215f1bb95`, with pre-existing edits. Release build on AMD Ryzen 7 4800HS, Linux x86-64. These are warmed helper benchmarks, not a Unity profile or end-to-end latency measurements. No game was launched, and no screenshots or images were inspected.

1. **One global session lock can delay terminal input during unrelated worktree operations.**
   [Session boundary](../slopd/src/manager/boundary.rs#L29) holds an exclusive mutex until the entire operation returns. [Worktree creation](../slopd/src/manager/worktrees.rs#L329) holds it through Git allocation and checkout; [WebSocket input](../slopd/src/api/ws.rs#L343) acquires the same mutex for keys, resize, mouse and paste. Scoped HTTP routes, including health and task operations, also share it. This is source-confirmed serialization; checkout delay was not timed. Narrow the critical section or introduce per-session lifetime protection, preserving the existing authorization/revocation and identity guarantees.

2. **Blank scrollback defeats the emulator's sparse-render cache.**
   [history_extent](../slopd/src/emu.rs#L300) compares every cell in every leading blank history row on every render. A 120×34 cursor-only update measured 0.96 µs without history, 27.77 µs with 100 leading blank rows, 403.48 µs with 1,000, and 6.39 ms with 10,000. The control with 10,000 nonblank history rows took 0.95 µs. The expensive scan runs under the emulator mutex, including for scroll snapshots. This workload is a stress case: substantial leading blank history must exist; ordinary nonblank history terminates the scan early. Cache or incrementally track the hidden prefix, with correct invalidation for scrolling, resize, reset and styled cells.

3. **Task progress writes serialize the entire task history synchronously.**
   [Tasks::save](../slopd/src/tasks.rs#L353) pretty-prints and atomically replaces all of `tasks.toml` for a single update, inside the [task-store mutex](../slopd/src/manager/tasks.rs#L47). The HTTP request also holds the global session boundary. Updating the first task with 1 KiB bodies took 0.115 ms for 10 records, 1.00 ms for 100, and 13.88 ms for 1,000. Lookup cost is deliberately minimal in this fixture. Move persistence off executor threads and away from input's global lock; incremental storage or a journal would address the work that still scales with retained history. Task listing also clones all returned bodies, but cloning 1,000 records was only 0.31 ms in this run.

4. **Every session-list refresh rereads and reparses the worktree catalog.**
   [Manager::views](../slopd/src/manager/session_state.rs#L40) calls `Store::load` on every request or session announcement, then searches the worktree vector repeatedly per session. State/title/bell changes call this through `announce_sessions`. Parsing alone measured 80.98 µs for 10 worktrees, 819.77 µs for 100, and 8.93 ms for 1,000; file I/O, cloning config, building views and serialization are excluded. Retain a revisioned worktree snapshot and index records by ID; preserve external-file reload behavior.

5. **An unchanged URL makes a one-row terminal edit rebuild the full URL scan.**
   [AutoLinkSpans](../mod/Source/SlopWorld/UI/Terminal/Sgr.cs#L84) writes every row into a full-screen character grid and allocates `new string(chars)` before rescanning. [TerminalRunCache](../mod/Source/SlopWorld/UI/Terminal/TerminalRunCache.cs#L99) reaches this after sparse updates. In the .NET 8 benchmark, 200 rows took 4.20 µs / 3,280 B per update without a URL, versus 88.65 µs / 56,504 B with one unchanged URL. At 34 rows it was 1.04 versus 16.07 µs. Limit rescans to edited rows and the connected wrapped-link region, preserving cross-row links and snapshot immutability. Actual Unity/Mono rendering cost remains unmeasured.

A lower-priority allocation source is full-screen decoding before live-frame coalescing: the Mono IPC fixture allocates 402,368 B and takes 364.86 µs for eight large frames although only the newest is retained. `ReceivedEvent` parses before queue replacement. This cost runs on the reader thread, not directly in the main-thread pump. Reducing it requires less wire data or a carefully validated envelope; simply skipping parse would break malformed-message handling and ordering rules.

Reproduction and validation:

- `make BUILD=release bench-daemon` includes new blank-history and storage scaling probes. Fixtures use disposable paths and never open user configuration or start a daemon/tmux server.
- `make BUILD=release bench-mod bench-ipc` completed; Rust/C# Protobuf fixture roundtrips passed. C# helper timings above are .NET 8; the separate IPC figures explicitly use Mono.
- `make test-bench-report` passed all six tests.
- `make lint-daemon`: Rust formatting passed; Clippy failed on pre-existing unused methods `mint`, `revoke_grantor`, and `invalidate_session` in `slopd/src/grant.rs`. The same warning appeared in the baseline build before the benchmark edits.
- The existing report parser accepts all 36 daemon benchmark cases; disposable fixture cleanup and coverage exclusions were checked.
- New benchmark code is excluded from the existing production coverage scope.
- Local ignored [daemon raw measurements](perf-suite.raw/bottlenecks-daemon-20260922.log).

Daemon and C# helper measurements use 50 warmed batches; IPC uses 21. The reported medians are batch averages, not individual-operation latency percentiles. Storage timings include atomic file replacement on `/tmp` (tmpfs on this machine) and exclude fsync; the task bottleneck is therefore present even without physical-disk latency. The worktree benchmark isolates the exact TOML parser used in production; it does not measure a full sessions request. Existing unrelated worktree edits were preserved.
