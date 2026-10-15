# Terminal latency ownership

Client diagnostics in `Client/Diagnostics/` own request IDs, completion, performance
records, and the bounded main-thread file queue. The frame hook writes game-side
performance and latency records to `SlopWorld-trace.log` after frame end. Daemon
`latency.rs` carries timestamps in screen metadata; `perf.rs` emits separate
performance summaries through tracing to the daemon log. `trace-capture` and
`trace-summary` consume game-side records only. Capture takes sidecar mode and an optional log path, then forwards CLI options
such as `--seconds`, `--label`, and `--output`; summary accepts log paths directly.
With `SLOPWORLD_DEBUG=1`, `ReaderLaunchTrace` emits `reader-launch` records for
diff requests, the run reply, session-list readiness, first live screen, first
repaint, and the pager's first alternate-screen frame/repaint. A launch ID spans
pre-session work; the returned session handle links it to daemon `launch-stage`
checkpoints. The first record includes the URI-escaped reader key for path comparison.
Its elapsed time starts at the Git action, including any cold status fetch before
the run request. Appearance restarts without a Git action start at the run request.
First screen/repaint can still be blank; the pager milestones distinguish that
startup baseline from less opening. Custom pagers without an alternate screen do
not complete that milestone. Unsubscribed, failed, or expired samples
are incomplete. No commands or file contents are logged.

Launch transport records split socket receipt, lazy-decode start, and main-thread
dispatch for the first screen and first alternate-screen frame, with the screen
sequence for daemon correlation. `queue_ms` ends at decode start; `decode_batch_ms`
includes decoding and other work in that event batch before dispatch, not just the
one screen's parser. Receive timestamps can precede run-reply callback delivery;
these records do not advance the ordered milestone clock. Frames coalesced before
dispatch have no launch transport record.

Daemon `reader-stage` records use the same daemon-monotonic `at_us` epoch as
`launch-stage`. They cover subscription receipt/guard acquisition, the first 32
control output records per attachment, render/commit/publication for the first eight
screen sequences, frame-pump admission, encoding/socket-lock wait, and successful
socket writes. Output `received_us` means control-loop dequeue before parsing/feed,
not the tmux process's write time. Render records carry the prior sequence; the
following publication supplies the committed sequence. Unchanged captures can render
without publishing. Direct subscription snapshots bypass frame-pump coalescing but
still carry send stages. These diagnostics log no terminal contents and stop after
the startup bounds; they do not time individual Git/delta/bat/less child processes.

Git performance counters distinguish cold/cached all/path opens, file/directory
selection, untracked diffs, and session reuse. Their durations end at cached status
resolution, before pager launch. Daemon `launch-*` counters separate boundary-inclusive
run/start totals from inner work, configuration reload, reservation, preparation,
tmux creation/metadata, reader attachment, command launch, and session publication.
`tmux-command` measures individual subprocesses. Nested timers overlap; percentile
differences cannot measure lock wait for a particular request.
The capture tool checks log identity and byte offsets during polling and at
completion. An aborted capture removes its incomplete output so summaries cannot
mistake it for a completed window.

Correlation is the next changed frame after input dispatch, not verified application
echo. Game frame end is before physical presentation. Client and daemon clocks
have separate monotonic epochs; subtract timestamps only within one peer. Completed
percentiles exclude censored outcomes, so inspect failures and incomplete samples
before comparing tails. Trace counters contain no terminal contents.

`MemoryTrace` contributes periodic RSS, managed heap, graphics memory, and GC samples. These
counters overlap and must not be added; GC counts are not allocated bytes. Eco's
`geometryCapacityBytes` estimates submesh array payload capacity immediately before
layer disposal, not retained memory or an RSS delta. `atlasColorBytes` estimates
RGBA storage only. Destruction and GC may occur later.

The runner in `bench/terminal-input/` owns desktop injection and report validity.
Local history-scroll observations begin at consumed movement and complete only on
ready-target repaint and frame end. Replaced targets are superseded; clamped movement
is not motion.
Precise X11 movement is aggregated per frame rather than per physical notch. Input
ownership belongs to [terminal](mod-terminal.md); broader ownership is
indexed in [diagnostics](ops-diagnostics.md).

Desktop phases require performance records and at least one valid latency sample;
history also requires a completed `history_scroll` sample. Precise X11 and wheel
fallback sampling boundaries differ. `complete_with_slip` retains event count but
shifts deadlines. Compare actual duration and slip counts. Transport residual
includes both network directions and unmeasured client/codec work. Performance
lanes overlap; helper timings do not predict rendering or end-to-end costs.
