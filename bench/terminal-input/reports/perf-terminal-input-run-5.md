# Terminal input performance suite

Generated: 2026-09-24T13:19:09-03:00

One run per phase. Percentiles are nearest-rank statistics over completed individual request/movement traces, not batch averages. Injection completion and measurement completeness are separate. Censored or invalid runs have no headline latency percentiles.

| Phase | Injection | Measurement | Sent / expected | Samples | Duration (s) | Slip (ms) |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| history | complete_with_slip | complete | 36000 / 36000 | 2353 | 60.006 | 5.621 |
| typing | complete | complete | 6000 / 6000 | 6000 | 60.000 | 0.000 |
| htop | complete_with_slip | censored | 36000 / 36000 | 48 | 60.002 | 2.121 |

## Latency

| Phase / kind | n | p50 (ms) | p95 (ms) | p99 (ms) | max (ms) |
| --- | ---: | ---: | ---: | ---: | ---: |
| history / history_scroll | 2353 | 16.127 | 17.975 | 22.608 | 47.696 |
| typing / paste | 6000 | 86.030 | 122.420 | 144.959 | 192.135 |
| htop (censored; percentiles withheld) | — | — | — | — | — |

## Performance by observed context

| Phase / context | Mean reported FPS | Gen0 collections | Terminal ms/update |
| --- | ---: | ---: | ---: |
| history: eco=1 terminal=1 sessions=37 size=1429x774 windows=59 | 59.74 | 17 | 5.798 |
| typing: eco=1 terminal=1 sessions=37 size=1429x774 windows=58 | 29.35 | 55 | 29.997 |
| htop: eco=1 terminal=1 sessions=37 size=1429x774 windows=58 | 59.43 | 24 | 8.020 |

## Outcomes and artifacts

### history

- Started: 2026-09-24T12:50:05.265394-03:00. Backend: ydotool. Rate: 600/s.
- Runner revision: `not recorded` (not proof of the running mod/daemon revision).
- Outcomes: deduplicated=36000, frame_end=2353, no_motion=1230, unfinished=0, completion_without_start=0, malformed=0, dropped_records=0, samples=2353.
- Raw artifacts: [.log](<../runs/slopworld-input-run-5/history.log>), [-events.json](<../runs/slopworld-input-run-5/history-events.json>), [-latency.json](<../runs/slopworld-input-run-5/history-latency.json>), [-performance.txt](<../runs/slopworld-input-run-5/history-performance.txt>).

Injector lateness p50/p95/p99 (µs): 67/91/103. Deadline rebases: 2.

### typing

- Started: 2026-09-24T12:46:37.953432-03:00. Backend: ydotool. Rate: 100/s.
- Runner revision: `not recorded` (not proof of the running mod/daemon revision).
- Outcomes: frame_end=6000, unfinished=0, completion_without_start=0, malformed=0, dropped_records=0, samples=6000, unacknowledged_at_capture=564, coalesced_samples=1198.
- Raw artifacts: [.log](<../runs/slopworld-input-run-5/typing.log>), [-events.json](<../runs/slopworld-input-run-5/typing-events.json>), [-latency.json](<../runs/slopworld-input-run-5/typing-latency.json>), [-performance.txt](<../runs/slopworld-input-run-5/typing-performance.txt>).

Injector lateness p50/p95/p99 (µs): 70/84/102. Deadline rebases: 0.

Append fixture: `Az漢字かな한글🙂🚀é `. Observed paste requests: 6000.

### htop

- Started: 2026-09-24T12:52:44.171055-03:00. Backend: ydotool. Rate: 600/s.
- Runner revision: `not recorded` (not proof of the running mod/daemon revision).
- Outcomes: frame_end=48, overflow=35803, timeout=128, unfinished=0, completion_without_start=0, malformed=0, dropped_records=0, samples=48, unacknowledged_at_capture=0, coalesced_samples=0.
- Raw artifacts: [.log](<../runs/slopworld-input-run-5/htop.log>), [-events.json](<../runs/slopworld-input-run-5/htop-events.json>), [-latency.json](<../runs/slopworld-input-run-5/htop-latency.json>), [-performance.txt](<../runs/slopworld-input-run-5/htop-performance.txt>).

Injector lateness p50/p95/p99 (µs): 67/84/102. Deadline rebases: 1.

## Interpretation limits

- Typing starts at client socket handoff, after clipboard retrieval, and ends at Unity frame end before physical presentation.
- History latency covers consumed accumulated movements that changed position, not each injected wheel tick. Deduplicated and no-motion events are not latency samples.
- Input correlation observes the next eligible changed frame, not verified causal echo. Injected events are not individually paired with trace IDs.
- Overflow and timeout censor the distribution. Zero logging drops do not make the surviving percentiles representative.
- The report lists coalesced samples and captures without an observed tmux acknowledgement separately. Neither alone proves input loss.
- FPS averages reported windows. It is not a frame-time percentile. Trace-summary excludes context transitions. Work lanes overlap.
