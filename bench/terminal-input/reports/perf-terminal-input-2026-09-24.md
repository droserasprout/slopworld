# Guided terminal input experiment, 2026-09-24

A person prepared the history, empty shell, and htop tabs. The host used ydotool injection with
`python3 bench/terminal-input/terminal-input-bench.py --output /tmp/slopworld-input-run-1`.
The runner used revision fc7e0c40. The person reported launching game revision d9a0ce7b.
Both tracing flags enabled. All phases lasted 60 seconds with 11 seconds of
latency drain. Context remained Eco=1, terminal=1, 53 sessions, 1429×804.
Starts: 04:49:58, 04:51:50 and 04:53:24 UTC respectively.

The 12 raw/report artifacts were copied from the host into ignored directory
`bench/terminal-input/runs/perf-suite-terminal-input-2026-09-24.raw/`. Originals remain in the host's
`/tmp/slopworld-input-run-1`. These measurements describe this run, not current
performance guarantees. See [measurement semantics](../../../notes/terminal-latency.md)
and [the earlier manual experiment](perf-terminal-latency-2026-09-24.md).

## Results

| Phase | Sent / expected | Completed request traces | Mean reported FPS | Input→frame end p50 / p95 / p99 / max, ms |
| --- | --- | --- | --- | --- |
| History, 60 wheel events/s | 3,600 / 3,600 | Not instrumented | 59.78 | — |
| Typing, 10 key presses/s | 600 / 600 | 600 | 59.79 | 51.013 / 52.645 / 67.442 / 68.865 |
| htop, 60 mixed events/s | 3,600 / 3,600 | 3,600 | 59.77 | 52.114 / 83.664 / 133.784 / 170.042 |

All injection schedules completed. Injection lateness p99 was 0.109, 0.539 and
0.118 ms, respectively. Maximum lateness was 0.745, 0.662, and 0.303 ms.
These timestamps precede backend calls and do not measure OS event delivery.
No reported dropped records, malformed records, unfinished traces, unmatched
completions, overflow or timeout outcomes. History has no request traces by design.

Typing had no coalesced observations and all captures observed a tmux acknowledgement.
htop had 11 coalesced samples and 322 samples (8.94%) whose capture had not yet
observed tmux acknowledgement. All htop traces are labeled `keys`, consistent
with the alternate-screen wheel-to-arrow fallback in TerminalMouseInput. This run
does not benchmark the terminal mouse-reporting protocol.

## Where latency accumulates

| Stage | Typing p50 / p99, ms | htop p50 / p99, ms |
| --- | --- | --- |
| Daemon handler→tmux dispatch | 0.733 / 3.675 | 0.721 / 5.907 |
| Tmux dispatch→first changed capture | 22.795 / 23.374 | 17.205 / 94.384 |
| Visible capture→WebSocket send | 1.485 / 1.658 | 6.567 / 14.218 |
| Client receive→dispatch | 8.192 / 11.732 | 7.078 / 19.429 |
| Dispatch→draw | 16.419 / 18.209 | 17.886 / 26.196 |
| Draw→frame end | 0.061 / 0.082 | 0.082 / 0.131 |

Stage percentiles belong to different observations and must not be summed.
Quiet typing spends roughly one frame waiting between dispatch and draw, plus
about 23 ms between tmux dispatch and capture. These are useful optimization
candidates. Under htop, the changed-capture interval has a much larger tail.
Application output cadence and keys that do not change output can contribute,
so it is not proof of daemon execution stalls. Capture-to-send and client frame
scheduling also have larger tails under load.

Average FPS staying near 60 does not imply responsive input or stable individual
frame times. This report does not contain frame-time percentiles. Terminal-window
work per Root.Update averaged 5.220 ms for history, 1.742 ms for typing and
6.476 ms for htop. Lanes overlap and must not be summed. Gen0 collections were
11, 2 and 27 across 58, 59 and 58 retained performance windows respectively.

## Limits and next comparison

The interval starts at client socket handoff and ends at Unity frame end before
presentation. The runner does not pair injected events with request IDs. Matching
counts do not prove individual event delivery. Next-frame correlation does not
prove causal echo, especially in htop. History scrolling has only performance
counters. The trace file avoided the earlier manual experiment's Verse logging
cap, and this run has no reported censored request outcomes.

Use this fixed workload as a baseline for capture scheduling and client dispatch/
draw changes. Repeat runs with the same geometry/session count and compare full
request distributions and censored counts. Native-terminal comparisons still
require equivalent input and presentation endpoints. These numbers are not
physical key-to-photon measurements.

## Aborted 10× follow-up, 14:45 UTC

The user copied `slopworld-input-run-2/` into the checkout. These supplied artifacts
remain untouched. The 600/s history phase sent 2,048 of 36,000 planned events
before a missed deadline at index 2,048, roughly 3.4 seconds into the run. Issued
events had 0.111 ms p99 lateness and at most 0.110 ms backend-call duration. The
missed deadline's own timestamp was not retained by that runner revision, so
these data do not establish the duration or cause of the terminating stall.

The report contains 2,036 `no_motion` outcomes and no completed history samples.
This is not evidence that the terminal never scrolled: the observer watched Unity
wheel passes, while SmoothScroll can consume precise X11 movement on another GUI
pass and suppress the legacy wheel duplicate. The observer now runs at actual
movement consumption and records duplicates separately. Precise samples are
movement accumulated per frame. They do not establish latency for each physical notch.
The old runner also stopped capture immediately on failure, without draining traces.
The difference between injected and logged counts does not prove lost delivery.
Only two stable performance windows were retained, too few for comparison.

The corrected runner preserves count and rebases deadlines for small OS stalls,
reporting actual duration and slip. It rejects a single delay above 100 ms or
cumulative schedule extension above 0.6 seconds. Failed runs also drain traces.
A new live experiment is required to validate the corrected scroll measurements.

## Unicode paste delivery diagnosis

The subsequent 100-pastes/s run stopped after 101 injections with no paste starts.
A coordinated host probe observed all 14 injected V key-downs in RimWorld's X11
window with Ctrl held. Thirteen client paste starts appeared within the probe's
per-group drains. One group included a late request from the previous group.
Manual Ctrl+V also worked. These observations point beyond desktop chord delivery,
but do not prove that Unity consumed every event.

Clipboard HTTP reads use the mod's shared `DaemonClient`. A standalone system-Mono
reproduction of its blocking-worker implementation completed zero of 100 requests
after 6.5 seconds, while direct concurrent daemon clipboard reads completed in
37–147 ms. This reproduces worker starvation without the desktop injector.
It does not establish that every earlier failure had this cause.

The production-source regression (`python3 bench/terminal-input/test_http_transport_mono.py`)
bounds Mono's worker pool
at 16 and sends 64 concurrent requests to a local controlled HTTP server. The old
transport completed zero within six seconds. Async I/O alone also stalled. Limiting
admission to eight active requests completed the burst in about 660 ms. Upload,
error parsing, oversized responses, response/error-body deadlines, queued expiry,
and a further 100 requests paced at 100/s passed. Callbacks stayed on the test's
main-thread pump. This is system Mono, not an in-game Unity validation.

Repeat the typing phase after restarting the updated mod, then history and htop.
Existing latency samples begin at socket handoff, after clipboard retrieval. They
exclude this clipboard wait and do not measure physical presentation. Keep injection
counts, delivered request counts and invalid runs alongside the percentile report.

The next in-game retry (`slopworld-input-run-4`) stopped on focus loss at 110 ms:
11 injections, 10 completed paste traces. The user observed a wl-clipboard popup.
The daemon previously invoked wl-paste for each paste. Upstream documents its
[focus-acquiring fallback surface](https://github.com/bugaevc/wl-clipboard/issues/12).
This is a separate blocker from HTTP scheduling. GNOME/XWayland now uses X11
clipboard tools for both fixture ownership and daemon access, preserving the
focus guard. This short, invalid run is not a sustained-performance result.

## Completed Unicode typing storm, 15:46 UTC

User ran `slopworld-input-run-5` after the HTTP fix and installation of the
GNOME/XWayland clipboard fix (daemon revision 74a4bcfc). The supplied artifacts
remain in that checkout directory. Ydotool injected the Unicode fixture at 100
pastes/s for 60.000066 seconds: 6,000 sent, 6,000 client paste starts, 6,000 completed
traces. No scheduling rebases, unfinished/malformed/unmatched traces or dropped
records. Injection lateness p50/p95/p99: 70/84/102 microseconds.

| Stage | p50 / p95 / p99 / max, ms |
| --- | --- |
| Client socket handoff → Unity frame end | 86.030 / 122.420 / 144.959 / 192.135 |
| Client receive → dispatch | 22.466 / 33.193 / 38.655 / 42.364 |
| Dispatch → draw | 33.218 / 48.221 / 54.942 / 62.097 |
| Tmux dispatch → visible capture | 17.659 / 46.649 / 66.060 / 89.930 |

Mean reported FPS was 29.35 over 58 retained performance windows; 55 Gen0
collections. Context: Eco=1, terminal=1, 37 sessions, 1429×774. Terminal-window
work averaged 29.997 ms per Root.Update. Sidebar work averaged 2.694 ms, and colonist bar work averaged 3.036 ms.
Lanes overlap and must not be summed. RSS first/last/peak: 934.06/956.23/995.86 MiB.
Managed memory used, first/last/peak: 261.69/293.96/296.50 MiB. These snapshots do not establish a leak.

There were 1,198 coalesced samples (19.97%) and 564 captures without an observed
tmux acknowledgement (9.4%). Neither count is itself a lost-input count. Counts
agree, but the runner does not pair injection events with client IDs. Traces
correlate the next eligible changed frame, not verified echo. Clipboard retrieval
and physical presentation remain outside the latency interval.

This validates sustained paste delivery after the fixes, not native-terminal-like
responsiveness. The ~30 ms terminal-window lane and frame scheduling waits warrant
profiling. Compare history and htop next. The original 10-key/s baseline used a
different workload, geometry and session count, so it is not a controlled regression
comparison with this 100-Unicode-pastes/s run.

## Completed history storm, 15:50 UTC

Run 5 history injected all 36,000 wheel events at 600/s. Two scheduler rebases
added 5.621 ms. Actual duration was 60.005684 seconds (`complete_with_slip`).
Injection lateness p50/p95/p99: 67/91/103 microseconds.

Unity's precise path consumed 3,583 accumulated scroll observations: 2,353
completed moving samples and 1,230 `no_motion` outcomes. All 36,000 legacy wheel
observations were marked `deduplicated` because the precise path owned scrolling.
This does not show that all injected scrolling was discarded. No unfinished,
unmatched, malformed or dropped trace records were reported.

Consumed movement → Unity frame end p50/p95/p99/max:
16.127/17.975/22.608/47.696 ms (2,353 samples). This is latency per consumed
accumulated movement that changed position, not per injected wheel event. The
1,230 no-motion observations are excluded from that distribution. The history
path measures local scrolling and repaint. It does not measure the typing path through
the daemon and tmux. Physical presentation remains outside the measurement.

Context matched typing (Eco=1, terminal=1, 37 sessions, 1429×774). Mean reported
FPS was 59.74 across 59 retained windows. There were 17 Gen0 collections. Terminal-window work
averaged 5.798 ms per Root.Update, compared with 29.997 ms during the Unicode
append storm. Sidebar averaged 2.258 ms, and colonist bar averaged 2.522 ms. These overlapping
lanes must not be summed. History sustained near-60 FPS in this workload while
typing fell to ~29 FPS. This motivates profiling the append and redraw path, but does
not by itself isolate a root cause. htop remains the third comparison phase.

## htop storm: injection completed, correlation failed, 15:52 UTC

Run 5 injected 36,000 events at 600/s (24,000 wheel events and 12,000 arrow keys)
in 60.002173 seconds. One scheduling rebase added 2.121 ms. Injection lateness
p50/p95/p99 was 67/84/102 microseconds. `complete_with_slip` describes the injector,
not successful latency measurement.

The trace contains 35,979 starts and outcomes, 21 fewer than injected events. These
streams are not individually mapped, so this difference alone cannot locate loss.
Only 48 traces completed (16 keys, 32 mouse). All refer to the same early screen
sequence, 2226, with input starts spanning 79.104 ms. The remaining 35,931 outcomes
were 35,803 overflows and 128 ten-second timeouts, all without daemon timing
metadata attached at client dispatch. There were no unfinished/unmatched/malformed
trace records or reported logging drops. Unlike the original htop baseline, this
run used terminal mouse requests for wheel input, not wheel-to-arrow fallback.

The 128-entry client correlation buffer cannot retain more than roughly 213 ms
of pending requests at 600/s without completions. Overflow means tracing evicted
pending samples. It does not prove that application input was dropped. These data
cannot distinguish stalled daemon/capture delivery from missing correlation on
later frames. Do not increase buffer size and treat that as a performance fix.

Mean reported FPS was 59.43 over 58 windows. Terminal-window work averaged 8.020 ms
per Root.Update, with 24 Gen0 collections. Context matched the other run 5 phases:
Eco=1, terminal=1, 37 sessions, 1429×774. The UI continued updating, but that does not
prove terminal output continued refreshing. Survivor percentiles from only 48
traces are not representative of this workload and are excluded from comparisons.

All three injection phases are now recorded. Typing and local history produced
usable distributions under their documented endpoints. htop is a failed
correlation/overload experiment requiring diagnosis before another comparison.

## htop investigation: dispatch capacity and trace eviction

The user confirmed htop kept refreshing. The daemon journal for
15:52:44–15:53:46 UTC reports 465 changed-screen events and 417 WebSocket sends
across sessions. These are not session-specific counts. The 31 warnings in that
window concern preset TOML reloads, not input dispatch. They do not locate an
input bottleneck.

`queue_input` registers traces before the ordered consumer reaches tmux. Both the
daemon cache and client timeline retain 128 IDs. At 600 requests/s that is only
~213 ms of arrivals. The deterministic test
`queued_arrivals_can_evict_input_before_dispatch_despite_new_frames` reproduces
admitting 129 inputs, dispatching the oldest, and capturing multiple changed
frames with no eligible timing: the oldest trace has already been evicted and
the retained traces have not dispatched. Continued terminal animation cannot
restore those lost associations.

The actual htop mix alternates two byte-encoded mouse reports with one named arrow
key. `merge_input` merges adjacent bytes or compatible keys, but never across the
kind boundary. Thus even with all 600 events already queued, it requires 400
serial tmux subprocess commands. The isolated host diagnostic initially took
1,418.612 ms (~422.9 events/s) to dispatch this batch. A keys-only control merged
600 events into six commands and took 29.522 ms. Both used an isolated tmux server
and a raw input sink. The diagnostic did not use desktop input or live sessions.

Reproduce via:
`make test-daemon TEST_ARGS='benchmark_mixed_input_dispatch -- --ignored --nocapture'`.
This is a best-case prequeued batching diagnostic using production code, not a
full paced game benchmark or a stable throughput guarantee. It establishes a
concrete mixed-input capacity problem below the requested 600/s and a compatible
trace-eviction mechanism. The historical log does not record input queue depth or
queue wait, so the precise live backlog and contributions of frame cadence remain
unmeasured. Exhausting htop's process list alone is insufficient to establish the
cause: unrelated changed frames can correlate retained, dispatched traces.

The next performance change should reduce subprocess round trips for mixed input
while preserving ordering. Additional diagnostics should report queue wait and
whether eviction happened before dispatch or capture. Raising the trace limit
alone would hide the capacity problem rather than resolve it.

A second isolated run, explicitly propagating any tmux command failure, completed
the mixed batch in 1,371.335 ms (437.5 events/s), and keys-only in 26.840 ms.
The daemon eviction regression and formatting/Clippy checks passed.
