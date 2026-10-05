# Terminal latency ownership

Client diagnostics in `Client/Diagnostics/` own request IDs, completion, performance
records, and the bounded main-thread file queue. The frame hook writes game-side
performance and latency records to `SlopWorld-trace.log` after frame end. Daemon
`latency.rs` carries timestamps in screen metadata; `perf.rs` emits separate
performance summaries through tracing to the daemon log. `trace-mod` and
`trace-summary` consume game-side records only.
The capture tool checks log identity and byte offsets during polling and at
completion. An aborted capture removes its incomplete output so summaries cannot
mistake it for a completed window.

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

Desktop phases require performance records and at least one valid latency sample;
history also requires a completed `history_scroll` sample. Precise X11 and wheel
fallback sampling boundaries differ. `complete_with_slip` retains event count but
shifts deadlines. Compare actual duration and slip counts. Transport residual
includes both network directions and unmeasured client/codec work. Performance
lanes overlap; helper timings do not predict Unity or end-to-end costs.
