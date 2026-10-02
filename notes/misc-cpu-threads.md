# CPU attribution

Daemon CPU includes shared Tokio workers running capture/input tasks and dedicated
OS audio threads; pane processes are separate. A busy runtime thread does not
identify an agent. Tmux server/control clients and agents' bwrap/pasta/process trees
have separate accounting. [Redeploy](daemon-redeploy.md) owns the service boundary.
The game mixes mod and vanilla work on Unity's main thread, which OS accounting
cannot separate.

Tmux pane PIDs identify agent process trees. Sessions with configured resource limits
use systemd scopes whose accounting includes descendants; sessions with no configured
limits lack that boundary. See [sandbox isolation](sandbox-isolation.md).

Named trace timers measure elapsed time, and counters measure work. They can help
correlate CPU samples but do not measure per-thread CPU time. CPU samples and stacks
attribute runtime work. Measurement procedures belong in
[CPU troubleshooting](../docs/src/reference/troubleshooting.md#cpu-usage);
trace ownership belongs to [terminal latency](terminal-latency.md).
