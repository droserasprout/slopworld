# CPU and thread accounting

## Process model

- `slopd` is one PID under the user `slopd.service`. Its Tokio runtime runs HTTP,
  WebSocket, retick, usage, tmux capture, emulator and other async work. `tokio-rt-worker`
  names are shared pool threads, not session ownership; tasks can move between workers.
  See [`main.rs`](../slopd/src/main.rs) and [`perf.rs`](../slopd/src/perf.rs).
- Audio adds named `slopd audio` and `slopd audio feed` threads, plus cpal/ALSA/PipeWire
  backend threads. The feed is playback/decode work, not agent work.
- Each tracked session has a tmux `-C attach` control client and a daemon-side capture
  reader/task. The persistent tmux server owns panes and normally survives a daemon restart;
  it may be in the daemon service cgroup without being a child of the `slopd` PID. See
  [`capture_reader.rs`](../slopd/src/manager/capture_reader.rs), [`capture.rs`](../slopd/src/manager/capture.rs),
  and [`slopd.service`](../slopd/slopd.service).
- Pane processes are separate from the daemon PID: shell, `bwrap`, optional `pasta`, and
  the configured agent form a per-session tree. Non-empty session limits wrap that tree in a
  transient `systemd-run --user --scope`; `CPUQuota` is percentage of one core. See
  [`mounts.rs`](../slopd/src/sandbox/bind/mounts.rs).
- `slopworld` is a launcher process that waits for `RimWorldLinux`. The game is one Unity
  process with a main/game thread, graphics threads, RimWorld `Job.Worker` threads, runtime
  thread-pool threads, and SlopWorld WebSocket threads. Most mod and Harmony work runs on
  Unity's main thread, so OS accounting cannot separate mod CPU from vanilla game CPU.

## Accounting boundaries

- `pidstat -p PID` reports a process total including its threads, but not child processes.
- `pidstat -t -p PID` splits that total by TID. Do not add the aggregate process row to its
  thread rows. `htop` can show both rows when user threads are enabled.
- On Linux, `100%` is approximately one fully occupied logical CPU; a 16-CPU host can reach
  roughly `1600%` total.
- A busy Tokio worker identifies daemon CPU, not the Rust operation or session responsible.
  Use named counters or sampled call stacks for that mapping.

## Tracking commands

```sh
pidstat -p "$(pgrep -xo slopd),$(pgrep -xo RimWorldLinux)" 1
pidstat -t -p "$(pgrep -xo slopd),$(pgrep -xo RimWorldLinux)" 1
pstree -alp "$(pgrep -xo slopd)"
tmux -L slopworld list-panes -a -F '#{session_name} pid=#{pane_pid} cmd=#{pane_current_command}'
```

Map pane PIDs to their transient scopes with:

```sh
systemctl --user list-units --type=scope --all | rg 'tmux-spawn-'
systemctl --user show tmux-spawn-…scope \
  -p Description -p CPUUsageNSec -p TasksCurrent
```

For a live per-session percentage, sample each scope's `cpu.stat` `usage_usec` twice. The
delta divided by elapsed microseconds, multiplied by 100, is percentage of one core and
includes every process/thread in that pane's scope.

## Code-level diagnostics

Set `SLOPWORLD_DEBUG=1` before launching the daemon/game. The daemon emits named aggregate
metrics to the `slopd::perf` target; the mod emits aggregate `perf` lines and terminal
`scroll-debug` lines. These explain subsystem work but are not OS CPU percentages. For exact
native stacks, use `perf top -g -p PID` or `perf record -g -p PID`; Tokio task-to-session
ownership still requires explicit instrumentation around the session operation.
