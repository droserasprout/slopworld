# Terminal latency

`Client/Diagnostics/` owns request correlation, bounded samples, and the main-thread
log queue. `PerfTrace` is game-free; `PerfTraceReport` adds frame/game context and
publication. Transport uses only the collector. The frame hook writes
`SlopWorld-trace.log` after frame end. Daemon `latency.rs` supplies screen timestamps;
`perf.rs` writes separate daemon summaries. Capture/summary tools consume game records
only. Capture checks log identity/offsets and removes incomplete output on abort.

Correlation means the next changed frame after dispatch, not verified application
echo. Frame end precedes physical presentation. Client and daemon clocks have
separate monotonic epochs: subtract only within one peer. Percentiles exclude censored
outcomes, so compare failures and incomplete samples too. Traces contain no terminal text.

Memory counters overlap; never add RSS, Mono, and Unity totals. GC counts are not
allocated bytes. Eco's geometry-capacity estimate measures array payload before
layer disposal, not retained memory or an RSS delta. Atlas estimates cover RGBA only.
Destruction and GC may happen later.

`bench/terminal-input/` owns desktop injection and report validity. History scrolling
starts at consumed movement and completes at target repaint/frame end. Replacement
supersedes a sample; clamped movement is not motion. Precise X11 movement aggregates
per frame, so its sampling boundary differs from wheel fallback.

Desktop phases need performance records and valid latency samples; history also needs
a completed scroll sample. Compare actual duration and slip counts: `complete_with_slip`
retains events but shifts deadlines. Transport residual includes both network directions
and unmeasured client/codec work. Performance lanes overlap; helper timings cannot
predict Unity or end-to-end costs. See [diagnostics](ops-diagnostics.md) for CPU attribution.
