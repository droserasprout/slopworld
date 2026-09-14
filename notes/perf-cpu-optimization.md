# Performance work

Use `make bench-mod BUILD=release` and `make bench-daemon BUILD=release` for repeatable helper
costs; [diagnostics](ops-diagnostics.md) covers live tracing. Benchmarks do not measure Unity
rendering, end-to-end latency, or total daemon CPU. Cached fan-out is not JSON serialization.

The useful optimization boundaries are revision-based reuse, visible-row work, independent
paint/layout invalidation, and slow maintenance while the map is covered. Preserve snapshot
immutability and input/event processing when skipping paint. Do not maintain a prose list of
individual optimizations; production code and benchmark fixtures own that inventory.

The [September baseline](misc-perf-suite-20260910-192731.md) measured a large sparse-URL parsing
penalty and decoding cost for coalesced socket batches. Those measurements explain the open
[CPU plan](plan-cpu-fixes.md); they are not claims about the current build. Keep benchmark
comparisons tied to revision, build mode, machine and fixture boundaries.
