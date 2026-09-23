# Telling agents about delegation

Use a short supplied breadcrumb when the session has working task credentials:

> Delegate work once with `slopctl task delegate AGENT TASK...`.
> Keep the returned ID.
> Use `slopctl task wait ID` for the result. It waits until the task reaches a terminal state.
> Do not poll `task list` or `status`.
> End assigned work with `slopctl task finish ID` or `slopctl task fail ID`.

Task-owned workers receive `SLOPWORLD_TASK_ID`.
Inside a worker, `slopctl` accepts omitted IDs for the task lifecycle commands (`show`, `wait`, `accept`, `progress`, `finish`, `fail`, and
`remove`).

The root caller can create a task-owned child with
`slopctl worker spawn [--durable] --project PROJECT --template TEMPLATE TASK...`.
The caller owns the child in the sidebar.
The selected template supplies its configuration.

Task and worker commands use their explicit command trees.
There are no root-level shorthand aliases. Task lifecycle commands use the `task` command tree.

Keep transport, grants, and policy out of the prompt. Project instruction files do not
own this capability because it belongs to a live SlopWorld session.

The task body remains in the mailbox until the recipient checks it.
Task arrival does not wake a running agent with a notification. Do not inject a task into a running TUI or submit it blindly.

`slopctl peers` and `slopctl status` let a caller find valid recipients and check identity,
reachability, and pending count.
