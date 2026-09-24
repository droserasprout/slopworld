# Terminal latency tracing

Set `SLOPWORLD_LATENCY=1` in **both the daemon and game process environments** before
starting them. Both peers must use builds supporting this diagnostic extension.
Tracing is off by default and independent of `SLOPWORLD_DEBUG`.
A shell export does not change the environment of an already running service.
Use the normal service/launcher configuration for your installation.

After loading and warmup, capture a repeatable workload:

```sh
make trace-mod TRACE_LABEL=typing TRACE_SECONDS=60 TRACE_OUT=/tmp/typing.log
python3 bench/terminal-input/latency-summary.py /tmp/typing.log
```

`trace-mod` reads future `SlopWorld-trace.log` entries from the game save-data folder; it does not start the game. Exercise keys,
paste or application mouse input during the capture. Keep geometry, focus, frame
pacing, colony load and session count constant. Repeat idle typing, heavy output,
multiple active panes and Ctrl-C under load separately. Record build revisions,
hardware, terminal size and display refresh rate with the results. Compare with
tracing disabled to assess instrumentation overhead.

The report gives nearest-rank p50/p95/p99/max in microseconds, grouped by input kind.
Each observation is one **outgoing input request**, which may batch several characters,
not a batch-average timing and not necessarily one physical keypress.
Raw `[SlopWorld] latency` records contain opaque IDs, numerical timings and outcomes;
they contain no input text, screen text or session names. Retain the raw capture for
analysis. `python3 bench/terminal-input/latency-summary.py --json /tmp/typing.log` emits structured data.

## Stage boundaries

- **Input:** the client's socket-send handoff, before encoding/writing the request.
  OS input delivery, Unity input handling and preceding literal-key accumulation are
  outside this interval.
- **Daemon received:** decoded WebSocket command handler entry, before waiting for
  the session-operation boundary and authorization.
- **Tmux:** first ordered input-consumer dispatch into the tmux command call.
  `tmux_done_us` is the latest completed successful call observed at capture, or zero
  when no acknowledgement has yet been observed. A request can use several calls.
- **Capture:** start of the mirror render that produces a changed screen. The first
  capture sequence/time remains attached when newer screens replace it.
- **WebSocket send:** after acquiring the socket send lock, just before diagnostic
  encoding and transport submission. The stamp belongs to this client's send,
  not a shared cached broadcast.
- **Receive / dispatch:** arrival of a complete WebSocket payload before decoding,
  followed by main-thread event dispatch.
- **Draw / frame end:** repaint of that exact live sequence, followed by Unity's
  `WaitForEndOfFrame` callback in the same frame.

The endpoint is **before presentation**, not physical key-to-photon latency. Unity's
[WaitForEndOfFrame contract](https://docs.unity3d.com/ScriptReference/WaitForEndOfFrame.html)
runs after cameras/GUI and before display. GPU completion, compositor queuing and
scanout require external presentation instrumentation or hardware measurement.
No screenshot, GPU readback or forced synchronization is used. The logged Unity
frame number and client monotonic timestamps can accompany external profiling.

Correlation means **the next changed frame captured after input dispatch**, not proof
that an application echoed that input. Background output can satisfy the correlation;
a key that causes no changed screen may time out. A captured frame can also precede
tmux command completion or failure; `unacknowledged_at_capture` exposes that uncertainty. Use controlled echo workloads when
you need a causal input-response experiment. `tmux_done_us` is not an echo acknowledgement.

Daemon timestamps share one monotonic epoch. Client timestamps share another. The
report only subtracts values within their clock domain; it works across sidecars and
remote hosts without clock synchronization. The transport residual is client
input-to-receive minus the daemon span. It includes both network directions and
unmeasured client/codec work; it is not one-way network latency.

## Coalescing and incomplete observations

A bounded set of trace IDs repeats on subsequent live screens, preserving correlation
through daemon and client coalescing. `first_seq` versus `seq` identifies a superseding
frame. Only an exact dispatched/drawn sequence can complete an observation; history
replies and session-name mismatches cannot. Run IDs guard replacements, and disconnect
clears outstanding client observations.

Both peers retain at most 128 outstanding records per owner (client-wide; daemon per
session), expiring after ten seconds. The client reports timeout, overflow, disconnect,
and missed-frame-end outcomes separately. Traces evicted at the daemon appear as
client timeouts. No-output input, hidden panes and unobserved frame callbacks must not
be interpreted as zero-latency successes. Capture boundaries or process exit may leave
starts unfinished or completions without a captured start. The report exposes both.
Percentiles cover completed valid observations only; inspect censored counts before
comparing tails. Tiny samples do not establish reliable p99 latency.

Diagnostic metadata takes the client's conservative Protobuf fallback path. It adds
wire bytes, allocation and bookkeeping; ordinary traffic and input batching stay
unchanged when tracing is off. Log writes are buffered until after the measured
frame-end timestamp. A bounded log buffer reports dropped records explicitly.
Performance and latency records share `SlopWorld-trace.log` in the game save-data
folder, bypassing Verse's global message limit. The file is replaced at game startup;
copy captures before restarting. Writes flush after each frame. Disk errors disable
this sink and emit one game-log error; missing records invalidate a comparison.
`TRACE_LOG=/path/to/SlopWorld-trace.log` overrides the capture source; `PROFILE`
selects the save-data folder for Make. Otherwise the trace path follows launcher
profile overrides (`SLOPCAR_PROFILE`, then `SLOPWORLD_PROFILE`) and defaults to
`$XDG_DATA_HOME/slopworld/profile` (or `~/.local/share/slopworld/profile`).
The game log announces the actual path.

## Guided Linux desktop input benchmark

Start one **empty local host-shell tab** in SlopWorld, with the cursor at the
prompt and no selection. The automatic suite runs history, then Unicode typing,
then writes a Markdown report. It does not include htop by default. Restart the
updated game and daemon with both tracing flags enabled. For `devloop`, run
`SLOPWORLD_DEBUG=1 SLOPWORLD_LATENCY=1 make devloop`. Run on the **graphical host**:

```sh
python3 bench/terminal-input/terminal-input-bench.py
# Double the default event rate, allow 20 seconds to focus, and retain a separate run:
python3 bench/terminal-input/terminal-input-bench.py --multiplier 10 --prepare-seconds 20 \
  --output bench/terminal-input/runs/perf-suite-terminal-input-next.raw \
  --report bench/terminal-input/reports/perf-suite-terminal-input-next.md
```

Press Enter once in the runner, then focus the empty shell and put the pointer
over terminal text. Leave that window focused until the report is written; no
intermediate prompts or tab changes are needed. The countdown defaults to 10 seconds.

Setup pastes and executes one bounded Python command in the host shell. It reads
`/dev/urandom`, maps every byte to an ASCII letter or digit, and emits a fixed
number of newline-separated rows. It never writes raw random control sequences or
runs an unbounded `cat /dev/urandom` pipeline. The fixture targets the daemon's
10,000-line scrollback limit, checks that the tmux pane has the matching limit,
and emits enough rows to displace the viewport. The line width is at most 120
columns and one less than the pane width. This requires `python3` and tmux in the
local host shell; sandboxed/remote shells are not supported for automatic setup.

The runner waits until the setup paste reaches the client before sending Enter,
then waits for a private completion file. Failure stops the suite and produces a
report; it does not continue measuring a half-prepared tab. An 11-second settling
period excludes setup work from measurements. Each phase then runs for 60 seconds
and drains latency traces for 11 seconds. Typing appends at the returned prompt in
the same history-filled tab; its first Ctrl+V returns the pane to the live view.
No history or typed text is deleted. Keep geometry, game state and session count
constant. Allow roughly three minutes including setup and settling.

By default, artifacts go to `bench/terminal-input/runs/perf-suite-terminal-input.raw`
and the report goes to `bench/terminal-input/reports/perf-suite-terminal-input.md`.
Use `--output` and `--report` to choose other paths. The report is regenerated, but
existing selected-phase artifacts are refused. Use a new directory to repeat a run.
Raw traces and JSON retain all outcomes, including censored measurements. The report
withholds headline latency percentiles for invalid/censored runs.

Generate a report from existing artifacts without injecting input:

```sh
python3 bench/terminal-input/terminal-input-bench.py --report-only \
  --output bench/terminal-input/runs/slopworld-input-run-5 \
  --report bench/terminal-input/reports/perf-terminal-input-run-5.md
```

`--phase suite` is the automatic default. `history`, `typing`, and `htop` keep
the manually prepared individual-phase workflow, each with a prompt and countdown.
`all` retains the legacy three-tab workflow. This allows adding a missing phase to
an existing directory without overwriting other phases. For example:

```sh
python3 bench/terminal-input/terminal-input-bench.py --phase htop --multiplier 10 \
  --prepare-seconds 20 --output bench/terminal-input/runs/slopworld-input-run-5
```

| Phase | Default 5× pace / total | 10× pace / total | Pattern |
| --- | --- | --- | --- |
| History | 300/s / 18,000 | 600/s / 36,000 | Wheel up for one second, down for one second |
| Text append | 50/s / 3,000 | 100/s / 6,000 | Paste `Az漢字かな한글🙂🚀é ` each time |
| htop | 300/s / 18,000 | 600/s / 36,000 | Two wheel notches then one arrow; reverse each second |

Text operations append a fixed UTF-8 chunk containing Latin, CJK, Korean, emoji,
and a combining accent. **Measured typing sends no Backspace, Delete or Enter.**
The automatic setup sends Enter once to run the filler command. The runner
replaces the clipboard with the public fixture before typing; it
does not read or preserve existing clipboard contents. Each Ctrl+V is one logical
append event, not one character or physical keystroke. The shell line grows
throughout the run, intentionally increasing redraw work. This tests clipboard
paste through the normal client/tmux path, not IME composition. htop receives
navigation input only. Record keyboard/compositor settings and shell/htop versions.

On Wayland install ydotool, wl-clipboard, xdotool, libX11 and libXtst, with an
accessible running ydotoold. The game must use its launcher-default XWayland
window: both backends now require an X display to verify RimWorld focus.
If your distribution ships the user unit, use
`systemctl --user start ydotool.service`. Set `YDOTOOL_SOCKET` for custom sockets.
The 64-bit Linux runner connects directly to the installed ydotool 1.x daemon's
[native input-event datagram protocol](https://github.com/ReimuNotMoe/ydotool/blob/master/Client/ydotool.c),
with no process launch per input. On X11 install xdotool, xclip, libX11 and libXtst;
injection uses a persistent XTest connection. `--backend ydotool` or `--backend xdotool`
overrides automatic selection. Backend readiness is checked before countdowns.
No device permissions or services are changed by the runner.

At connection, the runner verifies the active window belongs to a RimWorld process.
Both transports check the captured X11/XWayland input focus before every event
and stop if it changes. There is still a race between checking and injection;
return to the runner and press Ctrl-C to abort. Native Wayland game windows
without this focus guard are refused. Interrupted injector key-downs are released.

Before typing, the fixture must be readable from the clipboard twice. Ctrl+V
transitions have 1 ms spacing so modifier/key transitions are not a single burst.
During typing, no new client paste request for one second stops injection and
marks the phase invalid. After draining, fewer than 90% as many client paste
requests as injected appends also invalidates the workload. This is a delivery
sanity check, not per-event correlation or proof of causal text echo. It prevents
a handful of successful pastes from validating thousands of ineffective chords.

Every phase saves raw traces, performance summaries, per-request latency JSON,
and event JSON containing pace, fixture, transport, counts and individual injector
lateness percentiles. Host monotonic injection timestamps are **not paired** with
client request IDs. Occasional lateness of a full slot shifts subsequent deadlines
instead of causing a catch-up burst. The report records `actual_seconds`,
`schedule_slip_seconds` and `rebased_deadlines`, and labels such runs
`complete_with_slip`. Event count stays fixed; the window can extend by at most
1% (0.6 seconds). A single stall over 100 ms or excess cumulative drift aborts,
retaining the failed deadline as well as sent events. Even failed runs drain final
trace records for 11 seconds. Higher rates may exceed host or application capacity.
Compare actual durations and slip counts; a shifted schedule is not an exact
60-second pacing success. Repeat into a new directory.

### History scroll latency

`history_scroll` records begin where SmoothScroll consumes movement, before it
changes the local position. This includes precise X11 input consumed on layout or
repaint passes, even when the subsequent Unity wheel event is suppressed as a
duplicate. `scroll_source=precise` identifies those samples; `wheel` identifies
fallback Unity packets. The reporter includes counts by source. A completed sample
requires repaint of a ready view for the
current scroll target, followed by Unity frame end. Waiting on uncached history
therefore remains in the interval; drawing an old fallback does not complete it.
This path includes no tmux input command, so it reports local input→draw and
input→frame-end distributions without inventing daemon timing stages.

If another wheel event replaces the target before it is drawn, the earlier event
is `superseded`, not a fast success. Events that cannot move the position are
`no_motion`; legacy duplicates are `deduplicated`; panel release is `cancelled`.
Duplicates do not cancel the original precise movement. Timeouts and other censoring still
apply. Inspect these counts alongside completed percentiles, especially at 300–600
injected events/s on a 60 Hz display. A low completed p99 with many superseded
observations is not a low latency bound for all input. OS/Unity event coalescing
can make observed counts differ from injected counts. In particular, the precise
X11 path supplies a per-frame accumulated movement, not one sample per physical
notch. Its completed percentiles describe these consumed movement samples. No
per-notch presentation latency is inferred from an aggregate or a duplicate.

The history phase requires completed `history_scroll` samples; performance counters
alone no longer pass. Other phases require completed request traces. All phases
require performance records. The tool cannot verify the selected tab or causal
text echo. Neither local scroll nor request tracing measures physical presentation.

On GNOME with XWayland, install `xclip` as well. The runner uses it to own and
verify the fixture, and the daemon uses `xclip` or `xsel` for clipboard operations.
The `wl-clipboard` fallback can map a helper window to acquire focus, causing a
popup and aborting input. Do not disable the focus guard to work around this.
Focus-abort errors include expected and observed X11 IDs in the events report.
