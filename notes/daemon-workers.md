# Task-owned workers

`manager/workers.rs` constructs workers.
`manager/lifecycle/start.rs` supplies runtime credentials.
See [slopctl](../docs/src/guides/slopctl.md) for commands.

The selected template supplies the worker configuration. The caller owns its task and appears as
its parent in the sidebar. Do not infer either value from the generated name.

Scoped callers can use only templates in `[daemon].worker_templates` and only their own
projects. The host caller is not subject to this allowlist. The project must exist. The worker
needs a command and network access to the daemon API.

Give each worker a fresh private identity and a scoped credential. Do not copy the caller's
private state or expose the root endpoint token.

Persist the task before you start the worker. Failed starts and premature exits fail unfinished
tasks. Cleanup must preserve final task results. Workers do not autostart or auto-resume. This
prevents automatic task retries.

Durable workers persist by default and remain available for inspection. One-shot workers
normally disappear on exit. Existing tmux metadata can still permit adoption after redeployment.

Removing a parent does not remove its children. Orphaned children become top-level rows.
Every new worker receives the initial worker prompt through explicit paste-and-submit delivery.
Prompt construction belongs to the caller; delivery waits for readiness, pastes the completed
text, then queues a delay and Enter. It does not consume hidden pending breadcrumbs.

Task completion and worker removal never commit or remove [project worktrees](daemon-worktrees.md).
