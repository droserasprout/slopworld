# Telling agents about delegation

Use a short shipped breadcrumb when the session has working task credentials:

> Other SlopWorld agents are available for delegated work. Use `slopctl peers`,
> `slopctl delegate AGENT TASK...`, `slopctl inbox`, and `slopctl --help`.
> Finish assigned work with `slopctl finish`.

Keep transport, grants, and policy out of the prompt. Project instruction files do not
own this capability because it belongs to a live SlopWorld session.

The optional `slopworld_md` session setting mounts the generated project-root `SLOPWORLD.md`
and, when `[daemon.instructions] breadcrumb_enabled` is true, adds a separate discovery
breadcrumb. That file can summarize the current project sandbox, configured collaborators,
and the `slopctl` entry points; it is a generated runtime snapshot, not a replacement for
this task-discovery message or for project instructions.

The startup breadcrumb cannot wake an agent for a task arriving later. No task-arrival
notification exists, so the body remains in the mailbox until the recipient checks it.
Do not inject a task into a running TUI or submit it blindly.

`slopctl peers` and `slopctl status` let a caller find valid recipients and check identity,
reachability, and pending count.
