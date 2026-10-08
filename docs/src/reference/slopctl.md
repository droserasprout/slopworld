# Using slopctl

`slopctl` is the command-line tool for delegating tasks, reading logs, and inspecting the daemon.

## Identity

`SLOPWORLD_SESSION` identifies the caller. When unset, `slopctl` acts as `host` (the
user at the keyboard), which the daemon accepts only from the root token. An agent in a
sandbox sets this variable to its session name.

## Connection

The [environment reference](environment.md#connection-and-identity)
covers connection overrides and their precedence.

`slopctl` reads `endpoint.toml` for the daemon URL and token.
`SLOPD_ENDPOINT` selects a different endpoint file.
`SLOPD_URL` and `SLOPD_TOKEN` override it entirely. Use scoped credentials for
agents; see [Agent collaboration](../agents/agent-collaboration.md) for permissions and
[Paths and files](paths.md) for endpoint locations.

## Task commands

Use task commands to send work, find tasks, update status, and remove tasks.
See [Agent collaboration](../agents/agent-collaboration.md#task-mailboxes) for permissions.
Keep the returned task ID:

```sh
slopctl task delegate AGENT "task description"
slopctl task wait ID
```

Recipients use this sequence:

1. Read the task with `slopctl task show ID`.
2. Accept it with `slopctl task accept ID`.
3. Report progress with `slopctl task progress ID "note"`.
4. Report the result with `slopctl task finish ID "result"` or `slopctl task fail ID "reason"`.

Task statuses are `queued`, `accepted`, `working`, `done`, `failed`, or `canceled`.
A task is terminal when its status is `done`, `failed`, or `canceled`.

Use `slopctl task list` to find work.
Use `slopctl task remove ID` to remove a terminal task.
Use `slopctl task prune` to remove multiple terminal tasks. If a disk operation fails,
completed removals remain committed. The result reports committed, failed, and
unattempted task IDs; the command exits unsuccessfully. Retry to remove the remaining
terminal tasks.
`slopctl task prune --include-active` requires the root token.
It also removes unfinished tasks.
CLI help lists the current filters and options.

When `SLOPWORLD_TASK_ID` is set, you may omit `ID` from task lifecycle commands.
For `accept`, `progress`, `finish`, and `fail`, positional words become the note; use
`--id ID` to target another task explicitly. For example, `slopctl task progress Reviewing the code`
updates the injected task, while `slopctl task progress --id other-task Reviewing the code`
updates `other-task`. Use `--` before a note beginning with `--id` or a help flag.
Without an injected ID, updates also accept the positional `ID NOTE...` form.
Read, wait, and remove commands continue to accept an explicit positional ID.

`slopctl task list` shows unfinished tasks that you sent or received, newest first.
`wait` blocks until `done`, `failed`, or `canceled`, then prints the final task.
Stdout (including `--json`) contains only the final task result.

## Workers and templates

The following are abbreviated examples; CLI help lists all options.

`slopctl worker spawn [--one-shot | --durable] --project PROJECT --template TEMPLATE "task description"` creates the
task and child session in one daemon operation.
The worker uses the selected template.

The host user can select any template in the catalog.
Scoped agents can select only templates that the daemon allows for worker creation.

`slopctl template list [--project PROJECT]` shows the templates available to the caller.
`slopctl template show NAME [--project PROJECT]` prints one template that the caller can use.

Agent, worker, and worktree value options accept `--option=VALUE` for values beginning
with a dash. A separate value must not be another option.

Insert `--` before task text that has an option as its first item.
For example, `slopctl worker spawn --project repo --template review -- --durable` sends the literal task
`--durable`.

The worker receives its exact task ID in `SLOPWORLD_TASK_ID`, and `slopctl` uses it when a lifecycle
command omits `ID`. Workers are persistent by default.
The daemon removes `--one-shot` worker sessions on exit.
Persistent workers remain as stopped sessions that you can inspect.

A worker exit, stop, removal, or startup failure marks its unfinished task as failed.
To retry, create a new task or manually launch the persistent worker.

Create a normal agent with `slopctl agent create NAME --project PROJECT --template TEMPLATE`.
This command uses the same template catalog.
It creates the agent but does not start it unless you add `--start`.

See [Project worktrees](../workspace/project-worktrees.md) for independent checkouts, worker worktrees, and
manual removal.

## Diagnostics

Use `slopctl sandbox inspect NAME` to view the saved sanitized launch plan and,
when available, the live process tree. The saved plan remains available after
process exit or daemon restart; it records intended settings, not successful startup.
Host shells have no sandbox plan. Live comparison is best-effort and may be unavailable.

```sh
slopctl status                                      # caller, endpoint, reachability/version, pending tasks
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
The structured output shape varies by command.
