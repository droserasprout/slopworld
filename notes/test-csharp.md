# C# tests

`make test-mod` runs the C# suite without the game.
`make test` includes this suite, daemon tests, supporting-tool tests, and pager checks. `mod/Tests/SlopWorld.Tests.csproj` links selected production files
into a .NET 8 executable using NUnit and NUnitLite instead of loading the game-bound mod assembly.
`NUnitTestHarness.cs` discovers static test classes by their `Tests` suffix, exposing their
`Cases()` entries and public parameterless void methods as individually named NUnit cases.
The fixture runs without parallel execution.
Discovery sorts classes by name and methods by declaration metadata order. `AssertEx.cs` delegates assertions to NUnit.
The project file lists linked sources and
`TestSupport/` supplies narrow game, transport, and environment substitutes.

Coverage includes settings persistence and drafts, TOML and Protobuf wire models, terminal
parsing/history, transport buffering and reconnects, session rename reconciliation,
task-board polling, mutations and serialized cancellation/removal batches,
pager lifecycle, and pure layout/repaint policies. The test project links `Pager` and `Sgr` directly.
Their external dependencies use test substitutes. `HubCatalogTests` and transport tests
control callback order to exercise stale replies and lifecycle changes.
Preset and command tests cover copy isolation and wire round-trips, including daemon-owned
source metadata. Pager lifecycle tests cover failed and delayed starts, stale callbacks,
pinned-reader dismissal, missing session snapshots and replacement after process death.
Markdown tests cover supported HTML and Markdown parsing, project-local resource paths,
image geometry without loading images, quote/table layout, reflow invalidation and highlighted
code copy fidelity. Reference-definition metadata must not produce visible quote blocks.
Sandbox editor interaction tests drive selection, edits, confirmation and catalog callbacks
through recorded controls. Unchanged fields must preserve literal paths and environment values.
Settings tests cover delayed dirty flushing, profile fallback and live preference normalization.

`Program.cs` runs NUnitLite.
Its default work directory is the executable's output directory under `mod/Tests/bin/`.
It keeps `TestResult.xml` there even when started from the repository root. The make targets retain quiet summaries and nonzero failure status.
Coverage excludes test and substitute sources and the protoc-generated
`Client/Generated/Slopworld.cs`, measuring linked production code. Handwritten wire
conversions remain measured, and serialization tests still run against the generated types.

Keep test output under `mod/Tests/`, never `mod/Assemblies/`: RimWorld loads every DLL
in that directory. Linking production helpers avoids duplicating their behavior in tests,
but substitutes do not check Unity drawing, input dispatch, or Harmony patch binding.
When game testing is requested, check `Player.log` for runtime patch failures.

## Benchmarks

`make bench-mod BUILD=release` runs the same linked production helpers through the test
executable's separate `--perf-bench` mode. It reports warmed batch p50/p95 microseconds and
managed bytes per operation for tree/list traversal at three sizes, project totals, routed
sessions, terminal parsing/repaint decisions, and cold/warm history view assembly. History cases
exclude daemon capture and network latency. Setup is outside measurements.
Changed-revision and cold-cache cases rebuild before measurement. Tiered compilation is disabled for stable
code generation. Reference cases check matching results before timing.
They omit offscreen GUI drawing and therefore do not predict Unity repaint cost.

Use the same machine, build mode, and quiet host for comparisons. Timing includes delegate and
loop overhead, and allocation counts cover the current thread. These .NET 8 helper timings do
not predict Unity/Mono frame time or texture performance. Benchmarks have no timing thresholds
and do not run during `make test`.
`make bench` runs both language suites.

JSON fixtures live only in test support. Production socket tests exercise
binary fragmentation, ping/pong and masked writes without the game. `make bench-ipc` adds
Mono measurements (eager receive, queued single frames, and coalesced eight-frame bursts) and
C#/Rust binary fixture roundtrips. Process-runner tests use `/bin/sh`
and `/bin/sleep` to check pipe draining, exit codes, cancellation, and timeout cleanup.
They do not invoke SongRec or capture audio.
