# Task-owned workers

`application/workers.rs` owns project/template policy, checkout allocation/rollback,
and task/startup coordination. `session/workers.rs` supplies caller identity,
reservations, and live registration. Application commits durable records before
reconciliation. Registration and spawn guards remain held through task creation,
startup, and cleanup, including after requester cancellation.

Templates supply copied configuration/snapshots. Workers receive fresh private
identity and scoped grants; the caller supplies parent/task ownership. Root may use
any registered project and template; scoped callers use enabled templates in their
own project. Host terminals cannot own workers. See [templates](daemon-agent-templates.md)
and [grants](agent-grants.md).

Tasks persist before startup. `session/worker_failures.rs` owns failure policy;
callers check delayed run/reader identity and capture task identity before leaving
live state. The store preserves terminal results and detailed exit evidence across
repeated cleanup. Lifecycle owns retention, resource cleanup, and grant revocation.
Startup/delivery failure, stop, removal, or confirmed exit can fail unfinished work.
After startup adoption, recovery fails unfinished worker tasks whose panes are absent
from a successful tmux listing, including one-shot workers without saved sessions.
Listing or persistence failure aborts startup for retry; ordinary configuration
reconciliation must not run this check while a worker is being registered.

Workers are durable by default; one-shot workers disappear on exit. Neither autostarts
nor auto-resumes, and parent deletion does not cascade. Checkout lifetime is
[independent](daemon-worktrees.md). Configured prompts are delivered asynchronously
through `session/delivery.rs`; timeout may withhold them.

Recovery rejects a name reassigned to another identity and revokes grants before
adoption. Durable workers use saved configuration; one-shot workers use credential-free
tmux snapshots. Invalid identity or missing configuration retires the pane.
Restart requires saved launch configuration and rejects one-shot workers and runtime-only
viewers before stopping them. Stop, forget, and restart retain guards through cleanup
after disconnect.

See [redeploy](daemon-redeploy.md) for adoption, [sidebar](mod-sidebar.md) for hierarchy,
[Workers settings](../docs/src/customization/settings.md#workers) for prompt policy,
and [slopctl](../docs/src/reference/slopctl.md) for commands.
