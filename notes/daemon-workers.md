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

Worker names are allocated under the registration guard before task creation. Automatic
names use the project for host-owned workers and the caller for agent-owned workers,
with numeric suffixes across live sessions, saved agents, and host tabs. An optional
custom name must be valid and unused; collisions fail rather than changing it.
Names do not determine project membership or parent authority.

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

`config/worker_policy.rs` owns portable delegation settings. The daemon allowlist,
maximum access, and recursion ceiling constrain spawn; the caller and template may
narrow them. Spawn captures the parent state ID, relationship access, and child policy.
Policy edits do not rewrite existing relationships. Recovery retains captured IDs;
legacy workers without one cannot gain authority through a name-only edit.
Stored named permission bundles decode to independent flags without widening access;
saving emits only the flag representation. This also applies to template snapshots.

`session/caps.rs` resolves direct relationships by stable identity. All parents can read diagnostics. Follow-ups, task cancellation, terminal input,
and stop/restart each have an independent permission. Terminal frames require
terminal permission; lifecycle permission does not imply it. None grants generic
file/configuration authority. Manual grants remain
independent. `api/ws/outbound.rs` rechecks current relationship access at serialized
publication, including buffered and scroll frames on existing connections. Writer contention
waits outside session admission; socket sends have a one-second deadline so stalled
clients cannot indefinitely block lifecycle changes or revocation.

The root-only relationship editor persists durable workers through configuration and
one-shot workers through their credential-free tmux snapshot. Edits compare the
worker task ID so a stale editor cannot modify a replacement with the same name. Follow-ups
prepare a run-bound destination under session admission, then wait for readiness without
the boundary. Commit revalidates credentials, participation, cancellation, relationship
permissions, and run identity. An owned exclusive boundary spans audit and submission;
lost readiness fails without waiting under admission. Delivery rejects shells, requires agent
readiness, and rechecks foreground command before queued paste and Enter. Submission
is recorded in task history without claiming acceptance or changing the task result.
