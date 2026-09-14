# C# tests

`make test-mod` runs the game-free C# suite; `make test` includes it alongside the daemon
and contract checks. `mod/Tests/SlopWorld.Tests.csproj` links selected production files
into a dependency-free .NET 8 executable instead of loading the game-bound mod assembly.
`Program.cs` is the current test inventory; the project file lists linked sources and
`TestSupport/` supplies narrow game, transport, and environment substitutes.

Coverage includes settings persistence and drafts, JSON/TOML and wire models, terminal
parsing/history, transport buffering and reconnects, session rename reconciliation,
pager lifecycle, and pure layout/repaint policies. `Pager` and `Sgr` are linked directly;
their external dependencies use test substitutes. `HubCatalogTests` and transport tests
control callback order to exercise stale replies and lifecycle changes.

Keep test output under `mod/Tests/`, never `mod/Assemblies/`: RimWorld loads every DLL
in that directory. Linking production helpers avoids duplicating their behavior in tests,
but substitutes do not validate Unity drawing, input dispatch, or Harmony patch binding.
Runtime patch failures must be checked in `Player.log` when game testing is requested.

## Benchmarks

`make bench-mod BUILD=release` runs the same linked production helpers through the test
executable's separate `--perf-bench` mode. It reports warmed batch p50/p95 microseconds and
managed bytes per operation for tree/list traversal at three sizes, project totals, routed
sessions, terminal parsing/repaint decisions, and cold/warm history view assembly. History cases
exclude daemon capture and network latency. Setup is outside measurements; changed-revision
and cold-cache cases explicitly include rebuilding. Tiered compilation is disabled for stable
code generation. Reference cases check matching results before timing; they omit offscreen GUI
drawing and therefore do not predict Unity repaint cost.

Use the same machine, build mode, and quiet host for comparisons. Timing includes delegate and
loop overhead, and allocation counts cover the current thread. These .NET 8 helper timings do
not predict Unity/Mono frame time or texture performance. Benchmarks have no timing thresholds
and do not run during `make test`; `make bench` runs both language suites.
