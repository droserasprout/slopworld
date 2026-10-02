# Using slopctl

`slopctl` is the command-line tool for delegating tasks, reading logs, and inspecting the daemon.

Use `slopctl sandbox inspect NAME` to view the sanitized launch plan and the live process tree
for a session. The saved plan remains available after the process exits or a daemon restart.
Live comparison is best-effort: it checks whether the saved command executable
appears in an observable pane process tree. Without a usable PID/tree or executable,
comparison is unavailable; it does not compare every field of the saved plan.

## Identity

`SLOPWORLD_SESSION` identifies the caller. When unset, `slopctl` acts as `host` (the
user at the keyboard), which the daemon accepts only from the root token. An agent in a
sandbox sets this variable to its session name.

## Connection

`slopctl` reads `endpoint.toml` for the daemon URL and token.
`SLOPD_ENDPOINT` selects a different endpoint file.
`SLOPD_URL` and `SLOPD_TOKEN` override it entirely.

## Task commands

Use task commands to send work, find tasks, update status, and remove tasks.
Send work once.
Keep the returned task ID:

```sh
slopctl task delegate AGENT "task description"
slopctl task wait ID
```

Recipients use this sequence:

1. Read the task with `task show ID`.
2. Accept it with `task accept ID`.
3. Report progress with `task progress ID "note"`.
4. Report the result with `task finish ID "result"` or `task fail ID "reason"`.

Task statuses are `queued`, `accepted`, `working`, `done`, `failed`, or `canceled`.
A task is terminal when its status is `done`, `failed`, or `canceled`.

Use `task list` to find work.
Use `task remove ID` to remove a terminal task.
Use `task prune` to remove multiple terminal tasks.
`task prune --include-active` requires the root token.
It also removes unfinished tasks.
CLI help lists the current filters and options.

When `SLOPWORLD_TASK_ID` is set, you may omit `ID` from worker lifecycle commands.
For `accept`, `progress`, `finish`, and `fail`, positional words become the note; use
`--id ID` to target another task explicitly. For example, `slopctl task progress Reviewing the code`
updates the injected task, while `slopctl task progress --id other-task Reviewing the code`
updates `other-task`. Use `--` before a note beginning with `--id` or a help flag.
Without an injected ID, updates also accept the positional `ID NOTE...` form.
Read, wait, and remove commands continue to accept an explicit positional ID.

`task list` shows unfinished tasks that you sent or received, newest first.
The daemon sets a queued or accepted task's status to `canceled` when you cancel it.
The daemon stores one task record for both participants.
Participants can remove only terminal tasks.
The root token can also remove unfinished tasks.

`wait` blocks until `done`, `failed`, or `canceled`, then prints the final task.
It polls internally.
Do not repeatedly call `task show`, `task list`, or `status` to wait for completion.
It prints the current state to stderr on the first pending response, on state changes,
and every 30 seconds while waiting. Keep the same command running.

Read its output.
Stdout (including `--json`) contains only the final task result.

`worker spawn [--one-shot] --project PROJECT --template TEMPLATE "task description"` creates the
task and child session in one daemon operation.
The daemon uses the selected template. It never copies an existing agent.

The host user can select any template in the catalog.
Scoped agents can select only templates that the daemon allows for worker creation.

The caller named by `SLOPWORLD_SESSION` owns the task and sidebar child.

`template list` shows the templates available to the caller.
`template show NAME` prints one template that the caller can use.

Agent, worker, and worktree value options accept `--option=VALUE` for values beginning
with a dash. A separate value must not be another option.

Insert `--` before task text that has an option as its first item.
For example, `worker spawn --project repo --template review -- --durable` sends the literal task
`--durable`.

The worker receives its exact task ID in `SLOPWORLD_TASK_ID`, and `slopctl` uses it when a lifecycle
command omits `ID`. Workers are persistent by default.
The daemon removes `--one-shot` worker sessions on exit.
Persistent workers remain as stopped sessions that you can inspect.

A worker exit, stop, removal, or startup failure marks its unfinished task as failed.
To retry, create a new task or manually launch the persistent worker.

Create a normal agent with `agent create NAME --project PROJECT --template TEMPLATE`.
This command uses the same template catalog.
It creates the agent but does not start it unless you add `--start`.
The daemon checks the template and assigns a new private identity to each worker and agent.

See [Project worktrees](project-worktrees.md) for independent checkouts, worker worktrees, and
manual removal.

## Diagnostics

```sh
slopctl status                                      # daemon version, sessions, uptime
slopctl peers                                       # sessions visible to this caller
```

## Logs

`slopctl logs` shows the last 200 lines from the game and daemon.
Select `game` or `daemon` to limit the source.
Use `--lines N` to set the line count.
Use `--follow` to show new output.

For game logs, `slopctl` reads `Player.log`. Set `SLOPWORLD_GAME_LOG` to use another file.
For daemon logs, it reads the `slopd.service` user journal.
Set `SLOPWORLD_DAEMON_UNIT` to use another unit.

## Machine-readable output

Every command accepts `--json` to print newline-delimited JSON instead of formatted text.
The JSON represents the result, such as a filtered task list, name list, or status object.
A script gets the same data as the text output.
