# Telling agents about delegation

Use a short shipped breadcrumb when the session has working task credentials:

> Delegate work once with `slopctl task delegate AGENT TASK...`, keep the returned ID,
> and use `slopctl task wait ID` for the result. It blocks until terminal; do not poll
> `task list` or `status`. Assigned work ends with `slopctl task finish ID` or
> `slopctl task fail ID`.

The root caller can create a task-owned child with
`slopctl worker spawn [--durable] --project PROJECT --template TEMPLATE TASK...`; the caller owns
the child in the sidebar, while the selected template supplies its configuration.

The documented `delegate`, `spawn`, and `wait` spellings remain shortcuts for these task and
worker commands. Existing lifecycle verbs and `task ID` continue to work as compatibility
aliases.

Keep transport, grants, and policy out of the prompt. Project instruction files do not
own this capability because it belongs to a live SlopWorld session.

The task body remains in the mailbox until the recipient checks it; no task-arrival notification
wakes a running agent. Do not inject a task into a running TUI or submit it blindly.

`slopctl peers` and `slopctl status` let a caller find valid recipients and check identity,
reachability, and pending count.
