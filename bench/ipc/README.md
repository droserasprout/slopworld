# IPC codec benchmark

Run `just BUILD=release BENCH_RUN=<name> bench-ipc` for one focused run.
`just BUILD=release BENCH_RUN=<name> bench` includes IPC, daemon, and C# helpers
in one three-run result. `just BENCH_RUN=<name> bench-report` renders saved CSVs;
`BENCH_BASELINE=<older> BENCH_MODE=relative` compares them. Raw IPC CSVs and the
normalized metrics live under ignored `bench/results/<name>/`. The benchmark
starts no game or daemon service. You need Mono, .NET 8, Rust and protoc, plus
the normal repository build dependencies.

The benchmark measures the production Protobuf transport. Its C# lane links production
generated messages, `ReceivedEvent`, queue, and event batch. `Google.Protobuf` is pinned to 3.36.1.
Rust uses prost 0.14.4. This repository checks in package lockfiles.

C# measures binary receive/parse/line traversal and eight unsolicited live screens for one
session (enqueue, coalesce, drain), plus queued single frames. The receiver validates
canonical live frames before coalescing. It decodes retained frames. Other encodings use
the generated parser. Queue
limits, error handling, and WebSocket framing have separate tests. Fixtures cover plain text,
ANSI escape sequences, Unicode, and a larger viewport. Both languages assert decoded data.
Rust re-encodes fixtures. Mono checks them.
The four tracked `.textproto` fixtures are encoded with protoc into the run's
temporary directory. They reproduce the previous binary fixtures byte for byte;
Rust's round-trip files are written there too. Successful runs remove that scratch
directory.

Each lane warms up for 300 operations, then records 21 batches. C# uses 500 operations
per receive batch or 150 per burst. Rust uses 1,000 operations per batch. p50/p95 are percentiles of batch
averages, not individual-message tail latency. C# allocations use the runtime's thread
allocation counter. Fixture construction and forced GC are outside timed regions.
CoreCLR (.NET 8) runs without tiered compilation. The main report builds once. It reports the median and min–max range of three run p50s/p95s.

These are codec/queue measurements. They exclude kernel IPC, WebSocket framing, HTTP,
terminal capture/rendering and cold configuration projection costs. They establish neither
an FPS improvement nor an end-to-end latency claim. Git ignores the shared results directory, generated fixtures, and compiler output.

## Maintenance cost

| Component | Choice / cost |
| --- | --- |
| C# codec | Google.Protobuf 3.36.1, net472 for Unity Mono |
| Rust codec | prost/prost-build 0.14.4 |
| Authored contract | 811-line `.proto` file and route/type inventory |
| Generated C# | About 38,000 lines. Regenerate these files instead of editing them. |
| Runtime package | Five DLLs, about 0.8 MiB total |
| Remaining editor support | 83-line schema-reflection helper for draft leaf diffs and explicit patch paths |
| Remaining Rust adapter | Cold domain projections use in-memory Serde values. IPC uses no JSON text. |

The benchmark follows the single typed protocol used by the daemon and mod. It does not
measure serialization used for persistence or third-party APIs.
