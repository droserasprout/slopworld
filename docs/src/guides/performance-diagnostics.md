# Performance diagnostics

These CPU measurements apply to Linux hosts, including the Linux sidecar.
Measure in the host or container that runs the process.

Use `pidstat -p PID 1` for process totals and `pidstat -t -p PID 1` for thread rows.
Do not add totals and thread rows together. Roughly 100% means one logical core.
Use the tmux pane PID to identify an agent's process tree; see
[Attaching from a terminal](terminal.md) for native Linux access.

For an agent with configured resource limits, read its systemd scope's `cpu.stat`
twice. Divide the change in `usage_usec` by elapsed microseconds and multiply by
100. This includes descendants. Agents without limits have no such scope;
a process tree alone does not establish cgroup ownership.

Use CPU samples and stacks to attribute work. Trace timers measure elapsed time,
which can help correlate samples. See [Terminal latency measurements](terminal-latency.md)
for tracing and desktop input benchmarks.
