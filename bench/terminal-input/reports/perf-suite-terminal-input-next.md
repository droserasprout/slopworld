# Terminal input performance suite

Generated: 2026-09-24T13:26:04-03:00

One run per phase. Percentiles are nearest-rank statistics over completed individual request/movement traces, not batch averages. Injection completion and measurement completeness are separate. Censored or invalid runs have no headline latency percentiles.

| Phase | Injection | Measurement | Sent / expected | Samples | Duration (s) | Slip (ms) |
| --- | --- | --- | ---: | ---: | ---: | ---: |
| history | complete_with_slip | partial (superseded) | 36000 / 36000 | 2664 | 60.004 | 4.297 |
| typing | complete | complete | 6000 / 6000 | 6000 | 60.000 | 0.000 |

## Preparation

Status: **complete**. Target history: 10000 lines; generated: 10050 lines × 120 alphanumeric columns.
The bounded /dev/urandom fixture emits only ASCII letters, digits and line separators. Preparation and settling are excluded from phase measurements. Typing appends to the returned shell prompt in the same history-filled tab.

## Latency

| Phase / kind | n | p50 (ms) | p95 (ms) | p99 (ms) | max (ms) |
| --- | ---: | ---: | ---: | ---: | ---: |
| history (partial (superseded); percentiles withheld) | — | — | — | — | — |
| typing / paste | 6000 | 93.710 | 135.952 | 165.410 | 215.424 |

## Performance by observed context

| Phase / context | Mean reported FPS | Gen0 collections | Terminal ms/update |
| --- | ---: | ---: | ---: |
| history: eco=1 terminal=1 sessions=37 size=1429x774 windows=58 | 59.47 | 17 | 6.922 |
| typing: eco=1 terminal=1 sessions=37 size=1429x774 windows=58 | 26.96 | 66 | 32.427 |

## Outcomes and artifacts

### history

- Started: 2026-09-24T13:23:40.913347-03:00; backend: ydotool; rate: 600/s.
- Runner revision: `e8237232` (not proof of the running mod/daemon revision).
- Outcomes: deduplicated=18606, frame_end=2664, superseded=11667, no_motion=4911, unfinished=0, completion_without_start=0, malformed=0, dropped_records=0, samples=2664.
- Raw artifacts: [.log](<../runs/perf-suite-terminal-input-next.raw/history.log>), [-events.json](<../runs/perf-suite-terminal-input-next.raw/history-events.json>), [-latency.json](<../runs/perf-suite-terminal-input-next.raw/history-latency.json>), [-performance.txt](<../runs/perf-suite-terminal-input-next.raw/history-performance.txt>).

Injector lateness p50/p95/p99 (µs): 67/91/104; deadline rebases: 1.

### typing

- Started: 2026-09-24T13:24:52.964070-03:00; backend: ydotool; rate: 100/s.
- Runner revision: `e8237232` (not proof of the running mod/daemon revision).
- Outcomes: frame_end=6000, unfinished=0, completion_without_start=0, malformed=0, dropped_records=0, samples=6000, unacknowledged_at_capture=538, coalesced_samples=1551.
- Raw artifacts: [.log](<../runs/perf-suite-terminal-input-next.raw/typing.log>), [-events.json](<../runs/perf-suite-terminal-input-next.raw/typing-events.json>), [-latency.json](<../runs/perf-suite-terminal-input-next.raw/typing-latency.json>), [-performance.txt](<../runs/perf-suite-terminal-input-next.raw/typing-performance.txt>).

Injector lateness p50/p95/p99 (µs): 70/81/100; deadline rebases: 0.

Append fixture: `Az漢字かな한글🙂🚀é `; observed paste requests: 6000.

## Interpretation limits

- Typing starts at client socket handoff, after clipboard retrieval, and ends at Unity frame end before physical presentation.
- History latency covers consumed accumulated movements that changed position, not each injected wheel tick. Deduplicated and no-motion events are not latency samples.
- Input correlation observes the next eligible changed frame, not verified causal echo. Injected events are not individually paired with trace IDs.
- Overflow and timeout censor the distribution; zero logging drops does not make the surviving percentiles representative.
- Coalesced samples and captures without an observed tmux acknowledgement are reported separately; neither alone proves input loss.
- FPS is an average of reported windows, not a frame-time percentile. Context transitions are excluded by trace-summary; work lanes overlap.
