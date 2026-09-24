# Terminal latency experiment — 2026-09-24

This dated experiment identifies scheduling delays and tracing limits. It is not a
native-terminal comparison or evidence of current performance after later changes.

## Setup and measurement boundary

- Instrumented revision: `43d4a045676a7d5c5b6990690d83314769b70990`.
- Host inspection confirmed `SLOPWORLD_DEBUG=1` and `SLOPWORLD_LATENCY=1` in both
  daemon PID 787570 and RimWorld PID 788986 after the user restarted devloop.
- Linux, RimWorld 1.6.4850, Unity 2022.3.35f1, OpenGL. Game startup reported an
  AMD Radeon Graphics renderer and a 3072×1728 desktop at 60 Hz.
- Captured performance context: Eco enabled, terminal covering the workspace,
  51 sessions, reported UI dimensions 1499×812.
- Input means one outgoing request, possibly several batched characters. Completion
  means the matching live frame was drawn and reached Unity frame end, **before
  presentation**. Correlation identifies a subsequent changed frame, not a verified
  application echo. No physical key-to-photon latency was measured.
- Percentiles use nearest rank on individual completed requests, grouped by input
  kind. Clock differences stay within their respective processes. Stage percentiles
  are not additive. No tracing-disabled control or same-host native baseline was run.

See the [tracing guide](../../../notes/terminal-latency.md) for stage definitions
and reproduction commands.

## User-driven phases

| Phase | Start (UTC) | Capture interval | Actual workload / qualification |
| --- | --- | ---: | --- |
| Quiet typing | 03:57:52.736 | 96.8 s | User instructed to type normally in a quiet shell |
| Scrolling | 03:59:29.564 | 111.2 s | Includes the pause while clarifying the next phase; not isolated continuous scrolling |
| Active output | 04:01:20.788 | 85.2 s | User subsequently described spamming events “like bullet hell”; treat as a stress test |

The specific application used for the stress test was not recorded. Local scrollback
scrolling does not create these correlated input samples. The key requests recorded
in the scrolling interval must not be labeled local scroll latency.

## Request-to-frame-end results

Times below are milliseconds. These describe completed observations only.

| Phase / input kind | Completed | p50 | p95 | p99 | Maximum |
| --- | ---: | ---: | ---: | ---: | ---: |
| Quiet typing / keys | 168 | 53.381 | 54.414 | 70.011 | 70.574 |
| Quiet typing / paste | 1 | 64.003 | 64.003 | 64.003 | 64.003 |
| Scrolling interval / keys | 98 | 53.495 | 998.339 | 1112.938 | 1112.938 |
| Scrolling interval / paste | 1 | 64.242 | 64.242 | 64.242 | 64.242 |
| Active-output stress / keys | 63 | 53.169 | 171.691 | 1354.340 | 1354.340 |
| Active-output stress / mouse | 2632 | 84.718 | 162.765 | 609.172 | 641.215 |

The one-request paste rows and small key samples cannot establish stable tail
percentiles. The stress distributions are additionally biased by censored requests
and a truncated log, as detailed below.

## Selected stage timings

Each cell is **p50 / p95 milliseconds**, calculated independently. The stress columns
cover only the surviving completed requests.

| Stage | Quiet keys (168) | Stress keys (63) | Stress mouse (2,632) |
| --- | ---: | ---: | ---: |
| Daemon handler → tmux dispatch | 0.661 / 0.704 | 0.684 / 0.778 | 3.404 / 14.683 |
| Tmux dispatch → observed acknowledgement | 5.019 / 5.220 | 4.658 / 5.228 | 5.868 / 10.827 |
| Tmux dispatch → first capture | 22.715 / 23.005 | 22.060 / 23.025 | 12.976 / 86.588 |
| Tmux dispatch → drawn-frame capture | 22.715 / 23.005 | 22.064 / 23.037 | 19.508 / 102.802 |
| Drawn-frame capture → WebSocket send | 1.263 / 1.397 | 1.105 / 9.008 | 24.863 / 62.334 |
| Client receive → dispatch | 11.766 / 13.601 | 13.347 / 16.790 | 10.223 / 24.256 |
| Client dispatch → draw | 16.336 / 17.649 | 15.800 / 124.505 | 10.962 / 21.982 |
| Draw → frame end | 0.062 / 0.074 | 0.066 / 0.082 | 0.100 / 0.126 |
| Transport and client-send residual | 0.959 / 1.079 | 0.958 / 1.188 | 2.247 / 29.378 |

Acknowledgement observations cover 168 quiet keys, 62 stress keys and 2,463 stress
mouse requests. Missing acknowledgements are omitted from that lane, not recorded as
zero. A tmux acknowledgement does not prove the application reacted to the input.

## Completeness and instrumentation failure

| Outcome | Quiet typing | Scrolling interval | Active-output stress |
| --- | ---: | ---: | ---: |
| Completed frame-end observations | 169 | 99 | 2695 |
| Completed on a superseding sequence | 0 | 1 | 709 |
| Client tracking overflow | 0 | 0 | 1661 |
| Starts without recorded completion | 0 | 0 | 113 |
| Completed samples without observed tmux acknowledgement | 0 | 0 | 170 |
| Explicit trace-buffer drops | 0 | 0 | 0 |
| Malformed records | 0 | 0 | 0 |

The stress interval contained 4,469 request starts: 2,695 completed, 1,661 were evicted
by the tracer's 128-outstanding-request bound, and 113 lacked a recorded outcome.
Overflow is **tracer eviction**, not evidence that terminal input was dropped.

Only 25.1 seconds of completion timestamps and 25 performance records were retained
from the 85.2-second stress capture. Host inspection found this final game-log message:

> Reached max messages limit. Stopping logging to avoid spam.

The game process remained present. A follow-up read 36 seconds after the capture
boundary found no additional completions or performance records. The 113 unfinished
requests therefore cannot be classified as hangs or timeouts: logging had stopped.
Zero explicit trace-buffer drops does not mean the overall logging pipeline was lossless.

## Rendering context

`make trace-summary` excludes initial and transition records. The retained stable
windows reported:

| Phase | Stable windows | Mean reported FPS | Gen-0 collections | Terminal-window ms per Root.Update |
| --- | ---: | ---: | ---: | ---: |
| Quiet typing | 95 | 59.52 | 11 | 3.321 |
| Scrolling interval | 110 | 57.97 | 21 | 4.482 |
| Active-output stress, truncated | 24 | 56.49 | 6 | 5.468 |

The terminal-window lane is aggregate timed work divided by Root.Update calls, not
GPU time or an individual-frame tail percentile. These overlapping lanes must not
be added. GC counts cover different-length windows and are not allocation rates.

## Conclusions and next experiment

Quiet typing's repeatable ~53 ms median is useful evidence. Its largest measured
median intervals were tmux dispatch → capture (22.7 ms), receive → dispatch
(11.8 ms), and dispatch → draw (16.3 ms). Investigate capture scheduling and client
frame scheduling before prioritizing microsecond-level codec work. The capture
interval includes application response time. This experiment does not isolate that
from daemon scheduling.

The event flood exposed diagnostic capacity limits. It does not establish reliable
worst-case terminal latency. Likewise, ~1-second key tails during the scrolling
interval could reflect delayed subsequent output rather than delayed input echo.

Before repeating the stress test, move high-volume traces out of Unity's capped
logger and define bounded sampling/admission with explicit loss accounting. Repeat
quiet typing and controlled-output tests separately, add a tracing-disabled control,
and use a controlled echo workload for causal response latency.

## Artifacts

Processed values above come from `bench/terminal-input/latency-summary.py` and `make trace-summary`.
Raw captures and machine-readable summaries remain local and ignored under
`bench/terminal-input/runs/perf-suite-terminal-latency-2026-09-24.raw/`:
`quiet-typing.log`, `scrolling.log`, `active-output.log`, and `summary.json`.
Only this processed report is committed. Raw captures are not durable repository
artifacts. Preserve them separately if needed for future reanalysis.
