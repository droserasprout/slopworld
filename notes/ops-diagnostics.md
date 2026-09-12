# Diagnostics and logs

Agents survive daemon installation restarts: neither tmux nor the game is in the
daemon's cgroup - see [daemon-redeploy](daemon-redeploy.md).

## Logs

`slopctl logs` shows the last 200 game and daemon lines by default. Select
`game`, `daemon`, or `all`; add `--follow` and pipe the plain output as needed:

```sh
slopctl logs --follow | grep -iE 'error|exception' | head -50
slopctl logs game --lines 500
```

The game source defaults to the conventional Unity `Player.log` path and can
be overridden with `SLOPWORLD_GAME_LOG`. The daemon source reads the user
journal unit `slopd.service`, overridden with `SLOPWORLD_DAEMON_UNIT`. `--json`
emits newline-delimited objects for scripts.

## Runtime checks

```sh
curl -s localhost:7717/api/sessions | python3 -m json.tool
curl -s -X POST localhost:7717/api/sessions/NAME/start
tmux -L slopworld list-sessions
journalctl --user -u slopd -f
slopctl logs --follow
```

`SLOPD_LOG=slopd=debug`; `SLOPD_CONFIG` points at another config file.

Set `SLOPWORLD_DEBUG=1` before launching the game to enable the opt-in performance counters.
The mod emits one aggregate `perf` line per second, and terminal history emits one aggregate
`scroll-debug` timing line per second while active, plus a final line when it returns to live or
the window closes. These lines contain timings, row/cache counters, backlog and history state;
they never include terminal contents. The daemon uses the same `SLOPWORLD_DEBUG=1` switch and
reports its counters through the `slopd::perf` target.

`make trace-mod TRACE_LABEL=eco-closed TRACE_SECONDS=30` captures only new perf records
to `notes/trace-eco-closed.log`; it refuses to overwrite a capture and fails if no records
arrive. Start capture after loading and warmup. Restart with `SLOPWORLD_DEBUG=1 make
BUILD=release run` after `make BUILD=release install-mod` to load instrumented changes.

Compare Eco on/off and terminal open/closed with the same sessions, resolution, foreground
focus and display settings. Use a distinct label for each window and repeat each condition
three times. Discard the first record, whose aggregation can precede capture. Context records
include Eco, terminal coverage, session count, resolution, FPS and process-wide gen-0 GC
collections. They do not measure allocated bytes or input latency. Timed lanes overlap;
do not add root, sidebar, colonist-bar and terminal timings together.
`make trace-summary` groups captured records by observed state and prints lane milliseconds
per Root.Update call. `TRACE_FILES` selects specific captures; first and transition records
are excluded because their timings can include the previous condition.

In steady Eco, expect approximately one `solar-clock-samples` call per second and sidebar
presentation hits between age-bucket/session changes. A transition can add a clock sample.
Use `make bench-report` for three-run helper/daemon averages; those fixtures do not exercise
the Unity-bound row presentation or RealClock components.
