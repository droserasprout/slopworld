# Performance work

Use `make bench-mod BUILD=release` and `make bench-daemon BUILD=release` for repeatable helper
costs.
[Diagnostics](ops-diagnostics.md) describes live tracing. Benchmarks do not measure Unity
rendering, end-to-end latency, or total daemon CPU. Cached fan-out is not codec serialization.

The useful optimization boundaries are revision-based reuse, visible-row work, independent
paint/layout invalidation, and slow maintenance while the map is covered. Preserve snapshot
immutability and input/event processing when skipping paint. For socket batches, Protobuf frames
decode once before live-screen coalescing.
The IPC burst benchmark includes the cost of decoding discarded frames. Production code and benchmark fixtures own the optimization inventory.

Keep benchmark comparisons tied to revision, build mode, machine and fixture boundaries.
The [suite report](perf-suite.md) records daemon, C# and IPC measurements from
`make bench-report`.
Its revision identifies the measured build.

The [bottleneck investigation](perf-bottlenecks.md) records measured scaling costs,
lock contention risks, and suggested follow-up work.
