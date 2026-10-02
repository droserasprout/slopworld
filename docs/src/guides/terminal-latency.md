# Terminal latency measurements

Enable `SLOPWORLD_DEBUG=1` in both the daemon and game environments before starting
supported builds. An export does not change an already running service. Use the
normal service and launcher configuration; for the development loop, use
`SLOPWORLD_DEBUG=1 make devloop`. Tracing is off by default.

## Choose a capture

After loading and warmup, capture a repeatable workload:

```sh
make trace-mod TRACE_LABEL=typing TRACE_SECONDS=60 TRACE_OUT=/tmp/typing.log
python3 bench/terminal-input/latency-summary.py /tmp/typing.log
```

`trace-mod` reads future game trace entries and does not start the game. Exercise
keys, paste, or application mouse input while capturing. `make trace-summary`
summarizes game-side performance records. Daemon performance summaries use the
daemon log and need a separate log capture; they are not in this file.

Keep geometry, focus, frame pacing, colony load, and session count constant. Repeat
workloads separately and record build revisions, hardware, terminal size, and display
refresh rate. Compare with tracing disabled to assess instrumentation overhead.

The game replaces `SlopWorld-trace.log` at startup, so copy captures before restarting.
`TRACE_LOG` overrides the source for `trace-mod`. The desktop benchmark uses
`BENCH_TRACE_LOG` or its `--log` option instead. Without an override, Make's capture
uses `PROFILE`; the desktop runner follows `SLOPCAR_PROFILE`, then `SLOPWORLD_PROFILE`,
then the XDG SlopWorld profile. The game log announces the actual path.

## Guided Linux desktop benchmark

On Wayland, install ydotool, wl-clipboard, xdotool, libX11, and libXtst, with an
accessible running ydotoold. Use the launcher's default XWayland game window;
native Wayland windows lack the required focus guard. Set `YDOTOOL_SOCKET` for a
custom socket. On X11, install xdotool, xclip, libX11, and libXtst. On GNOME with
XWayland, also install xclip for clipboard ownership and verification. The runner
checks readiness and does not change permissions or services.

Start one empty local host-shell tab, with the cursor at the prompt and no selection.
The shell needs Python 3 and tmux. Automatic setup does not support sandboxed or
remote shells. Run on the graphical host:

```sh
make bench-terminal BENCH_RUN=terminal-baseline
make bench-terminal BENCH_RUN=terminal-next BENCH_MULTIPLIER=10 BENCH_PREPARE_SECONDS=20
```

Press Enter once in the runner, then focus the empty shell and put the pointer over
terminal text. Leave it focused until the report is written. The default countdown
is ten seconds. Focus changes stop injection; return to the runner and press Ctrl-C
to abort. Do not disable the focus guard to work around clipboard popups.

Setup runs one bounded Python filler command, waits for completion, and settles
before measuring. It writes printable ASCII rows rather than raw random control
sequences. The automatic suite measures history and then Unicode typing in the same
filled tab. It takes roughly three minutes, including setup and settling, and does
not include htop by default.

| Phase | Default 5× pace / total | 10× pace / total | Pattern |
| --- | --- | --- | --- |
| History | 300/s / 18,000 | 600/s / 36,000 | Wheel up one second, down one second |
| Text append | 50/s / 3,000 | 100/s / 6,000 | Paste a fixed Unicode chunk |
| htop | 300/s / 18,000 | 600/s / 36,000 | Two wheel notches then an arrow, reversing each second |

Typing replaces the clipboard with the public fixture and does not preserve its
previous contents. Measured typing sends no Backspace, Delete, or Enter; setup
sends Enter once for the filler. Each Ctrl+V is one logical append event, not a
physical keypress or IME composition test. The shell line grows throughout the run.
No history or typed text is deleted.

Use `make bench-terminal-typing` for automatic filling followed by typing only.
For history only, set `BENCH_PHASE=history` and `BENCH_FILL_HISTORY=1`; check `make`
and `make/bench.mk` for current runner options. Manually prepared individual phases
use their own prompt/countdown. Add htop with a separate phase:

```sh
make bench-terminal BENCH_RUN=terminal-htop BENCH_PHASE=htop BENCH_MULTIPLIER=10 BENCH_PREPARE_SECONDS=20
```

## Artifacts and validity

Results live in ignored `bench/results/<run>/`: `metrics.csv`, `outcomes.csv`,
`run.csv`, and `report.md`. Raw traces and JSON remain under `raw/terminal/`.
Existing selected-phase artifacts are refused; use a new run name to repeat a phase.
Regenerate reports without injecting input:

```sh
make bench-report BENCH_RUN=terminal-next
make bench-report BENCH_RUN=terminal-next BENCH_BASELINE=terminal-baseline BENCH_MODE=relative
```

The runner verifies clipboard delivery and stops ineffective typing. All phases
require performance records and completed traces; history requires completed
`history_scroll` samples. Performance counters alone do not validate history latency.
Compare history runs only when their observed input sources match: precise X11
movement is accumulated per frame, while fallback wheel packets have different
sampling boundaries. The tool cannot verify the selected tab or causal text echo.

`complete_with_slip` means injection deadlines shifted while event count stayed
fixed. Compare actual durations and slip counts; it is not an exact pacing success.
Invalid or censored phases withhold latency percentiles. Retain raw outcomes and
repeat questionable runs into new directories.

## Interpreting latency

Reports use nearest-rank p50/p95/p99/max in microseconds. Each request observation
may batch characters. Correlation means the next changed frame after dispatch,
not proof that the application echoed input; background output can satisfy it.
No-output input and hidden panes can time out. History samples measure consumed
local movement through a ready repaint and frame end, including uncached-history waits.
Superseded movement is not a fast success.

Frame end is before display presentation, so these results are not physical
key-to-photon latency. GPU, compositor, and scanout need external instrumentation.
Never subtract client and daemon clocks. Transport residual includes both network
directions and unmeasured client/codec work, not one-way network latency.

Completed percentiles exclude failures and incomplete observations. Inspect those
counts before comparing tails; tiny samples do not establish reliable p99 results.
Performance lanes overlap, memory counters overlap, and GC collection counts do not
measure allocated bytes. Do not add them together. Helper benchmarks do not measure
Unity CPU or end-to-end latency. Ownership details are in
[terminal latency](../../../notes/terminal-latency.md).
