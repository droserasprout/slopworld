# Performance work

Use `make bench-mod BUILD=release` and `make bench-daemon BUILD=release` for repeatable helper
costs; [diagnostics](ops-diagnostics.md) covers live tracing. Benchmarks do not measure Unity
rendering, end-to-end latency, or total daemon CPU. Cached fan-out is not codec serialization.

The useful optimization boundaries are revision-based reuse, visible-row work, independent
paint/layout invalidation, and slow maintenance while the map is covered. Preserve snapshot
immutability and input/event processing when skipping paint. For socket batches, Protobuf frames
decode once before live-screen coalescing; the IPC burst benchmark includes discarded-frame
decoding costs. Production code and benchmark fixtures own the optimization inventory.

Keep benchmark comparisons tied to revision, build mode, machine and fixture boundaries.
The [suite report](perf-suite.md) records daemon, C# and IPC measurements from
`make bench-report`; its revision identifies the measured build.
