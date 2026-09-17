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

Send work once and keep its returned task ID:

```sh
slopctl delegate AGENT "task description"
slopctl wait ID
```

Recipients use `task ID`, `accept ID`, `progress ID "note"`, then
`finish ID "result"` or `fail ID "reason"`. Use `inbox` to discover work,
`rm ID` to remove a terminal task, and `prune` to remove terminal tasks in bulk.
`prune --all` is root-only and also removes unfinished tasks. CLI help lists the
current filters and options.

`inbox` shows unfinished work in both directions, newest first. Cancellation marks queued or
accepted work as `canceled`; removal is shared:
the store holds one copy of a task, and a participant can only drop tasks that have
stopped moving. The root token can remove tasks still in flight.

`wait` blocks until `done`, `failed`, or `canceled`, then prints the final task.
It polls internally; do not loop over `task`, `inbox`, or `status`.

`spawn [--durable] --project PROJECT --template TEMPLATE "task description"` creates the task
and child session in one daemon operation. Workers are instantiated from the selected, enabled
template; an existing agent is never cloned. The caller named by `SLOPWORLD_SESSION` owns the
task and sidebar child. `templates` lists the catalog, while an agent caller sees only templates
enabled for worker spawning; `template show NAME` prints one accessible definition.
Insert `--` before task text that begins with an option, for example
`spawn --project repo --template review -- --durable` sends the literal task `--durable`.
The worker receives its exact task id in `SLOPWORLD_TASK_ID`, so it should run `slopctl task ID`,
accept it, and report progress with the normal lifecycle commands. One-shot workers disappear
on exit; durable workers remain as stopped, inspectable sessions. Exit, stop, removal, or startup
failure marks an unfinished worker task failed, and retries require a new task or a manual
durable start.

Create a normal agent from the same catalog with
`agent create NAME --project PROJECT --template TEMPLATE`; creation does not start it unless
`--start` is supplied. Both worker and ordinary-agent creation use the daemon's template
validation and fresh private identity allocation.

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
