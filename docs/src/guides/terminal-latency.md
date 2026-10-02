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
refresh rate.

With diagnostics enabled, the game truncates `SlopWorld-trace.log` at startup.
Copy captures before restarting. Capture output is created exclusively; use a new
`TRACE_OUT` or label for each capture.
`TRACE_LOG` overrides the source for `trace-mod`. The desktop benchmark uses
`BENCH_TRACE_LOG` or its `--log` option instead. Without an override, Make's capture
and the desktop runner follow `PROFILE`, then `SLOPCAR_PROFILE`, then
`SLOPWORLD_PROFILE`, then the XDG SlopWorld profile. The game log announces the actual path.

## Guided Linux desktop benchmark

On Wayland, install ydotool 1.x, xdotool, libX11, and libXtst, with an
accessible running ydotoold. Use the launcher's default XWayland game window;
native Wayland windows lack the required focus guard. Set `YDOTOOL_SOCKET` for a
custom socket. On X11, install xdotool, libX11, and libXtst. Automatic setup and typing also
require wl-clipboard on Wayland and xclip on X11 (including GNOME/XWayland).
Manually prepared history-only and htop-only phases do not need clipboard tools. The runner
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
For history only, set `BENCH_PHASE=history` and `BENCH_FILL_HISTORY=1`.
 `BENCH_PHASE`, `BENCH_FILL_HISTORY`, `BENCH_MULTIPLIER`, and
`BENCH_PREPARE_SECONDS` select phase, setup, pace, and focus countdown. Manually prepared individual phases
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

The runner verifies clipboard delivery and stops ineffective typing. Every phase
requires performance records and at least one valid completed latency sample;
history additionally requires a completed `history_scroll` sample. Performance
counters alone do not validate history latency.
Compare history runs only when observed input sources match. The tool cannot verify
the selected tab or causal text echo.

`complete_with_slip` means injection deadlines shifted while event count stayed
fixed. Compare actual durations and slip counts; it is not an exact pacing success.
Invalid or censored phases withhold latency percentiles. Retain raw outcomes and
repeat questionable runs into new directories.

## Interpreting latency

Reports give latency percentiles for completed observations. Correlation is not
proof of application echo. Incomplete or partial samples are withheld; inspect
outcome counts before comparing tails. Frame end precedes display presentation,
so physical key-to-photon latency requires external instrumentation.
