# Using slopctl

`slopctl` is the host-side CLI for delegating tasks, reading logs, and inspecting the
daemon.

Use `slopctl sandbox inspect NAME` to view the sanitized launch plan and the live process tree
for a session. The saved plan remains available after the process exits or a daemon restart.

## Identity

`SLOPWORLD_SESSION` identifies the caller. When unset, `slopctl` acts as `host` (the
user at the keyboard), which the daemon accepts only from the root token. An agent in a
sandbox sets this variable to its session name.

## Connection

`slopctl` reads `endpoint.toml` for the daemon URL and token. `SLOPD_ENDPOINT` selects
a different endpoint file; `SLOPD_URL` and `SLOPD_TOKEN` override it entirely.

## Task commands

The canonical task command tree keeps delegation, discovery, lifecycle updates, and cleanup
together. Send work once and keep its returned task ID:

```sh
slopctl task delegate AGENT "task description"
slopctl task wait ID
```

Recipients use `task show ID`, `task accept ID`, `task progress ID "note"`, then
`task finish ID "result"` or `task fail ID "reason"`. Use `task list` to discover work,
`task remove ID` to remove a terminal task, and `task prune` to remove terminal tasks in bulk.
`task prune --include-active` is root-only and also removes unfinished tasks. CLI help lists the
current filters and options.

When `SLOPWORLD_TASK_ID` is set, worker lifecycle commands may omit `ID`; an explicit ID still
takes precedence. For example, a worker can run `slopctl task show`, `slopctl task accept`, and
`slopctl task finish`.

`task list` shows unfinished work in both directions, newest first. Cancellation marks queued or
accepted work as `canceled`; removal is shared:
the store holds one copy of a task, and a participant can only drop tasks that have
stopped moving. The root token can remove tasks still in flight.

`delegate`, `spawn`, and `wait` remain documented shortcuts for `task delegate`,
`worker spawn`, and `task wait`. Existing root task verbs, `inbox`, `templates`,
`rm`, `task ID`, bare `worker` spawning, and `prune --all` remain compatibility aliases.

`wait` blocks until `done`, `failed`, or `canceled`, then prints the final task.
It polls internally; do not loop over `task show`, `task list`, or `status`.
It prints the current state to stderr on the first pending response, on state changes,
and every 30 seconds while waiting. Keep the same command running and read its output;
stdout (including `--json`) contains only the final task result.

`worker spawn [--durable] --project PROJECT --template TEMPLATE "task description"` creates the
task and child session in one daemon operation. `spawn` is its documented shortcut. Workers are
instantiated from the selected, enabled template; an existing agent is never cloned. The caller
named by `SLOPWORLD_SESSION` owns the task and sidebar child. `template list` lists the catalog,
while an agent caller sees only templates
enabled for worker spawning; `template show NAME` prints one accessible definition.
Insert `--` before task text that begins with an option, for example
`worker spawn --project repo --template review -- --durable` sends the literal task `--durable`.
The worker receives its exact task id in `SLOPWORLD_TASK_ID`, and `slopctl` uses it when a lifecycle
command omits `ID`. One-shot workers disappear
on exit; durable workers remain as stopped, inspectable sessions. Exit, stop, removal, or startup
failure marks an unfinished worker task failed, and retries require a new task or a manual
durable start.

Create a normal agent from the same catalog with
`agent create NAME --project PROJECT --template TEMPLATE`; creation does not start it unless
`--start` is supplied. Both worker and ordinary-agent creation use the daemon's template
validation and fresh private identity allocation.
Use `template list` to discover available templates; `templates` remains its compatibility alias.

## Diagnostics

```sh
slopctl status                                      # daemon version, sessions, uptime
slopctl peers                                       # sessions visible to this caller
```

## Logs

`slopctl logs` shows the last 200 lines from the game and daemon. Select `game` or
`daemon` to narrow the source; `--lines N` controls the count and `--follow` tails
new output.

The game source reads `Player.log` (override with `SLOPWORLD_GAME_LOG`). The daemon
source reads the `slopd.service` user journal (override with `SLOPWORLD_DAEMON_UNIT`).

## Machine-readable output

`--json` is accepted on any command and prints the answer as newline-delimited JSON
instead of formatted text. It renders the shaped answer (filtered task list, name list,
status object), so a script gets the same data the reader saw.
