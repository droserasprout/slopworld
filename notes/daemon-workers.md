# Task-owned workers

`slopd/src/session/manager/workers.rs` owns the typed worker request, preflight,
checkout allocation and rollback, and task/session creation. HTTP decodes caller
credentials and the existing/new checkout choice before invoking that operation.
Lifecycle start owns launch credentials; lifecycle stop and capture exit own termination. [Grants](agent-grants.md)
own authority, and [templates](daemon-agent-templates.md) own portable copied settings.

The selected template supplies configuration and snapshots. The worker receives a
fresh private identity and scoped grant; the caller supplies task ownership and
parent identity, never inherited sandbox state. Creation validates project, command,
and network choices. Root callers can use any catalog template and registered
project; scoped callers use policy-enabled templates in their own project.
Ordinary host terminals cannot own a worker.

The task persists before startup. An owned operation retains the session and spawn
guards through startup or failure cleanup after requester cancellation. Startup failure, stop, removal, confirmed process
exit, or initial-prompt timeout can fail unfinished work; terminal task results are
preserved. Workers are durable by default. One-shot workers are removed on exit.
Workers do not autostart or auto-resume, and deleting a parent does not cascade to
children.

Worker policy uses `[daemon].worker_templates` and
`[daemon.instructions].worker_prompt`. The prompt can refer to `$SLOPWORLD_TASK_ID`.
The [Workers settings](../docs/src/customization/settings.md#workers) page edits both.
The daemon queues the configured worker prompt after startup; asynchronous delivery
can time out without sending it. Delivery belongs to `session/manager/delivery.rs`
(see [library](daemon-library.md)),
hierarchy presentation to [sidebar](mod-sidebar.md), independent checkout lifetime
to [worktrees](daemon-worktrees.md), and adoption to [redeploy](daemon-redeploy.md).
Commands and operational behavior belong to [Using slopctl](../docs/src/reference/slopctl.md).

Recovery rejects a surviving worker whose saved name now belongs to a different
identity and revokes its grants before adoption. Durable workers recover their own
saved configuration; one-shot workers retain a credential-free configuration snapshot
in tmux metadata. Missing configuration snapshots or invalid identities reject
adoption and retire the pane.
Restart requires saved launch configuration and rejects runtime-only viewers and
one-shot workers before stopping them. Stop, forget, and restart retain the operation
boundary through cleanup even after the requesting caller disconnects.
