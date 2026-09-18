# IPC codec benchmark

Run `make bench-ipc` for one run, or `make bench-ipc-report` for three serial runs and
[the report](REPORT.md). No game or daemon service is started. Requires Mono, .NET 8,
Rust and protoc, plus the normal repository build dependencies.

The JSON baseline is frozen from commit `7251189d` (the transport before migration).
`legacy/` contains its parser, envelope scanner, bounded queue and event batch. Only
transport type names were prefixed to let both implementations run in one process.
These files never ship in the mod. The Protobuf lanes link production generated messages,
`ReceivedEvent`, queue and event batch. `Google.Protobuf` is pinned to 3.36.1;
Rust uses prost 0.14.4. Package lockfiles are checked in.

C# measures UTF-8 receive/parse/line traversal and eight unsolicited live screens for one
session (enqueue, coalesce, drain). JSON scans every frame and parses the retained screen;
Protobuf decodes every frame before coalescing. This intentionally preserves each production
algorithm. Queue limits, error handling and WebSocket framing have separate tests.
Rust measures serde_json versus prost encoding/decoding of the same screen values.
Fixtures cover plain text, ANSI escape sequences, Unicode and a larger viewport.
Both languages assert equivalent decoded data; Rust re-encodes fixtures and Mono checks them.

Each lane warms up for 300 operations, then records 21 batches. C# uses 500 operations
per receive batch or 150 per burst; Rust uses 1,000. p50/p95 are percentiles of batch
averages, not individual-message tail latency. C# allocations use the runtime's thread
allocation counter. Fixture construction and forced GC are outside timed regions.
.NET tiered compilation is disabled. Report ratios use medians of three run p50s.

These are codec/queue measurements. They exclude kernel IPC, WebSocket framing, HTTP,
terminal capture/rendering and cold configuration projection costs. They establish neither
an FPS improvement nor an end-to-end latency claim. Raw CSV files and environment metadata
are retained under `results/`; generated fixtures and compiler output are ignored.
