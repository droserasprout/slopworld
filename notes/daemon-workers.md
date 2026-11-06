# Task-owned workers

`application/workers.rs` owns project/template policy, checkout allocation/rollback,
and task/startup coordination. `session/workers.rs` supplies caller identity,
reservations, and live registration. Application commits durable records before
reconciliation. Registration and spawn guards remain held through task creation,
startup, and cleanup, including after requester cancellation.

Templates supply configuration copies and snapshots. Workers receive fresh private
identities and identity credentials; callers supply parent and task ownership. Root may use
any registered project and template. Scoped callers stay in their own project and
use templates allowed by their agent policy. Host terminals cannot own workers. See
[templates](daemon-agent-templates.md) and [grants](agent-grants.md).

A root spawn with a Planner item ID saves its Task and Worker links before
registration. If saving fails, the Task fails and startup is withheld. Planner owns
link storage and removal.

## Startup and recovery

Tasks persist before startup. `session/worker_failures.rs` owns failure policy;
callers check delayed run/reader identity and capture task identity before leaving
live state. Runtime-owned pending outcomes retain captured task/session identities before
retirement, independently of live rows and private state. Maintenance retries failed
writes; cancellation leaves pending evidence available for retry. Explicit stop reports
persistence failure after resource cleanup. The store preserves terminal results and
detailed exit evidence across repeated cleanup. Pending evidence lasts for the daemon
lifetime; startup recovery handles unfinished tasks after a daemon restart. Lifecycle
owns retention, resource cleanup, and credential revocation.
Startup/delivery failure, stop, removal, or confirmed exit can fail unfinished work.
After startup adoption, recovery fails unfinished worker tasks whose panes are absent
from a successful tmux listing, including one-shot workers without saved sessions.
Survival requires an adopted live worker with matching task and stable identity; a
pane reusing the task’s original session label does not establish ownership.
Readerless saved workers require matching pane worker metadata before adoption.
Listing failures during adoption or recovery, and task persistence failures, abort
startup for retry. Ordinary configuration reconciliation must not run task recovery
while a worker is being registered.

Worker names are reserved across live sessions, saved agents, and host tabs under the
registration guard, before task creation. Names confer neither project membership nor
parent authority. Naming options belong to [slopctl](../docs/src/reference/slopctl.md).

Workers are durable by default; one-shot workers disappear on exit. Neither autostarts
nor auto-resumes, and parent deletion does not cascade. Checkout lifetime is
[independent](daemon-worktrees.md). Configured prompts are delivered asynchronously
through `session/delivery.rs`; timeout may withhold them.
For direct Codex commands, `sandbox/codex.rs` supplies launch-only trust for the
allocated project/worktree path so fresh private state does not block startup on
the folder-trust dialog. Host configuration and template snapshots are not edited.

Recovery rejects a name reassigned to another identity and revokes credentials before
adoption. Durable workers use saved configuration; one-shot workers use credential-free
tmux snapshots. Invalid identity or missing configuration retires the pane.
Restart requires saved launch configuration and rejects one-shot workers and runtime-only
viewers before stopping them. Stop, forget, and restart retain guards through cleanup
after disconnect.

See [redeploy](daemon-redeploy.md) for adoption, [sidebar](mod-sidebar.md) for hierarchy,
[Workers settings](../docs/src/customization/settings.md#workers) for prompt policy,
and [slopctl](../docs/src/reference/slopctl.md) for commands.

## Delegation and permissions

`config/worker_policy.rs` owns portable delegation settings. Delegation is opt-in
per agent or template. The caller's policy controls spawning;
the child template may narrow inherited delegation, permissions, and template selection.
The daemon owns only the shared initial worker prompt. Spawn captures the parent
state ID and the child’s initial policy.
`config/permissions.rs` owns current CLI/API action scopes. Permission edits apply to
existing workers immediately. Parent identity remains captured; relationship flags
are legacy input only. Worker spawning and template restrictions remain independent.

`session/caps.rs` checks current caller policy. Own workers means direct children;
project agents excludes host terminals. The root-only worker editor updates the
worker's own permissions and spawning policy, comparing the task ID to reject stale
editors. Durable changes use configuration; one-shot changes use the credential-free
tmux snapshot. WebSocket publication rechecks current terminal access before sending.

`application/worker_tasks.rs` owns follow-up and cancellation authorization and
follow-up auditing. Session owns identity and delivery; the task store owns mutation
policy. Authorized follow-ups support both worker assignments and ordinary tasks
addressed to existing agents. Follow-ups prepare under session admission, wait for readiness outside it,
then revalidate authority, task state, and run identity. Exclusive admission spans
audit and submission; lost readiness fails without another wait. Delivery rechecks
the foreground command before paste and Enter to avoid sending input to a shell
after agent exit. History records submission, not acceptance, without changing the
task result.
