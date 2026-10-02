# Terminal latency ownership

Client diagnostics in `Client/Diagnostics/` own request IDs, completion, performance
records, and the bounded main-thread file queue. The frame hook writes game-side
performance and latency records to `SlopWorld-trace.log` after frame end. Daemon
`latency.rs` carries timestamps in screen metadata; `perf.rs` emits separate
performance summaries through tracing to the daemon log. `trace-mod` and
`trace-summary` consume game-side records only.

Correlation is the next changed frame after input dispatch, not verified application
echo. Unity frame end is before physical presentation. Client and daemon clocks
have separate monotonic epochs; subtract timestamps only within one peer. Completed
percentiles exclude censored outcomes, so inspect failures and incomplete samples
before comparing tails. Trace counters contain no terminal contents.

`MemoryTrace` contributes periodic RSS, Mono, Unity memory, and GC samples. These
counters overlap and must not be added; GC counts are not allocated bytes. Eco's
`geometryCapacityBytes` estimates submesh array payload capacity immediately before
layer disposal, not retained memory or an RSS delta. `atlasColorBytes` estimates
RGBA storage only. Destruction and GC may occur later.

The runner in `bench/terminal-input/` owns desktop injection and report validity.
Operating procedures and trace-path overrides belong to the
[latency guide](../docs/src/guides/terminal-latency.md). Local history-scroll
observations begin at consumed movement and complete only on ready-target repaint
and frame end. Replaced targets are superseded; clamped movement is not motion.
Precise X11 movement is aggregated per frame rather than per physical notch. Input
ownership belongs to [terminal](mod-terminal.md); broader ownership is
indexed in [diagnostics](ops-diagnostics.md).
