# IPC codec benchmark

Run `make BUILD=release bench-ipc` for one focused run. `make bench-report` includes all
IPC metrics in the main three-run averaged performance report, alongside daemon and C#
benchmarks. The current [processed report](../../notes/perf-suite.md) is replaced on each run.
Raw per-run logs and IPC CSVs stay local and ignored. No game or daemon service is started. Requires Mono, .NET 8,
Rust and protoc, plus the normal repository build dependencies.

The benchmark measures the production Protobuf transport. Its C# lane links production
generated messages, `ReceivedEvent`, queue and event batch. `Google.Protobuf` is pinned to 3.36.1;
Rust uses prost 0.14.4. Package lockfiles are checked in.

C# measures binary receive/parse/line traversal and eight unsolicited live screens for one
session (enqueue, coalesce, drain). Protobuf decodes every frame before coalescing. Queue
limits, error handling and WebSocket framing have separate tests. Fixtures cover plain text,
ANSI escape sequences, Unicode and a larger viewport. Both languages assert decoded data;
Rust re-encodes fixtures and Mono checks them.

Each lane warms up for 300 operations, then records 21 batches. C# uses 500 operations
per receive batch or 150 per burst; Rust uses 1,000. p50/p95 are percentiles of batch
averages, not individual-message tail latency. C# allocations use the runtime's thread
allocation counter. Fixture construction and forced GC are outside timed regions.
.NET tiered compilation is disabled. The main report averages three run p50s/p95s.

These are codec/queue measurements. They exclude kernel IPC, WebSocket framing, HTTP,
terminal capture/rendering and cold configuration projection costs. They establish neither
an FPS improvement nor an end-to-end latency claim. Raw CSV files under `results/`, generated fixtures and compiler output are ignored.

## Maintenance cost

| Component | Choice / cost |
| --- | --- |
| C# codec | Google.Protobuf 3.36.1, net472 for Unity Mono |
| Rust codec | prost/prost-build 0.14.4 |
| Authored contract | 811-line `.proto` plus route/type inventory |
| Generated C# | About 38,000 lines; regenerate, never edit |
| Runtime package | Five DLLs, about 0.8 MiB total |
| Remaining editor support | 83-line schema-reflection helper for draft leaf diffs and explicit patch paths |
| Remaining Rust adapter | Cold domain projections use Serde values in memory; no JSON text in IPC |

The benchmark follows the single typed protocol used by the daemon and mod. It does not
measure serialization used for persistence or third-party APIs.
