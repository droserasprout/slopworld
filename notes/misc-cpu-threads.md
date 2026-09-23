# CPU attribution

Daemon PID totals include Tokio, capture and audio threads, not pane processes. Tokio workers
are shared. A busy thread does not identify an agent. Tmux server/control clients and each
agent's bwrap/pasta/process tree are separate. The game mixes mod and vanilla work on Unity's
main thread, so OS accounting cannot separate those costs.

`pidstat -p PID` reports process totals.
The `-t` option separates results by thread. Do not add aggregate and thread
rows. Linux 100% is roughly one logical core, not the whole machine. A process tree alone
also does not identify service or cgroup ownership.
See [redeploy](daemon-redeploy.md).

Tmux pane PIDs map agents to process trees. Where limits create a systemd scope, sample its
`cpu.stat` usage delta over elapsed time to include descendants. Unlimited sessions do not
have that accounting boundary. Named trace counters or sampled stacks are needed to map
runtime CPU to operations.
See [diagnostics](ops-diagnostics.md).
