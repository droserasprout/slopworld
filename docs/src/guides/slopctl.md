# Using slopctl

`slopctl` is the host-side CLI for delegating tasks, reading logs, and inspecting the
daemon.

## Identity

`SLOPWORLD_SESSION` identifies the caller. When unset, `slopctl` acts as `host` (the
user at the keyboard), which the daemon accepts only from the root token. An agent in a
sandbox sets this variable to its session name.

## Connection

`slopctl` reads `endpoint.toml` for the daemon URL and token. `SLOPD_ENDPOINT` selects
a different endpoint file; `SLOPD_URL` and `SLOPD_TOKEN` override it entirely.

## Task commands

```sh
slopctl delegate AGENT "review the auth module"   # create a task
slopctl spawn PARENT "inspect the build"          # root-only one-shot child worker
slopctl spawn --durable PARENT "run the checks"   # keep the child in config after exit
slopctl spawn --project repo --template codex PARENT "review the diff"
slopctl inbox                                       # unfinished work, both directions
slopctl inbox --all                                 # include finished and failed
slopctl inbox --sent                                # only tasks you sent
slopctl inbox --received                            # only tasks sent to you
slopctl inbox --status pending                      # filter by state
slopctl task ID                                     # show one task
slopctl wait ID                                     # wait for done or failed
slopctl accept ID                                   # accept a pending task
slopctl accept ID "starting now"                    # accept with a note
slopctl progress ID "halfway done"                  # mark in progress
slopctl finish ID "done, see commit abc123"         # complete a task
slopctl fail ID "blocked on missing config"         # mark failed
slopctl rm ID                                       # remove a finished task
slopctl prune                                       # remove all finished tasks
slopctl prune --all                                 # remove all tasks (root only)
```

`inbox` shows unfinished work in both directions, newest first. Removal is shared:
the store holds one copy of a task, and a participant can only drop tasks that have
stopped moving. The root token can remove tasks still in flight.

`wait` polls the task until it reaches the terminal `done` or `failed` state, then
prints that final task. It is useful after delegating work when the caller needs to
continue only once the result is available.

`spawn` creates the task and child session in one daemon operation. The worker receives its exact
task id in `SLOPWORLD_TASK_ID`, so it should run `slopctl task ID`, accept it, and report progress
with the normal lifecycle commands. Spawning is root-only. One-shot workers disappear on exit;
durable workers remain as stopped, inspectable sessions. Exit, stop, removal, or startup failure
marks an unfinished worker task failed, and retries require a new task or a manual durable start.

## Diagnostics

```sh
slopctl status                                      # daemon version, sessions, uptime
slopctl peers                                       # sessions visible to this caller
```

## Logs

```sh
slopctl logs                                        # last 200 lines, game + daemon
slopctl logs game                                   # game log only (Player.log)
slopctl logs daemon                                 # daemon journal only
slopctl logs all                                    # both, prefixed by source
slopctl logs --lines 500                            # more lines
slopctl logs --follow                               # tail mode
slopctl logs --follow | grep -iE 'error|exception'  # filter live output
```

The game source reads `Player.log` (override with `SLOPWORLD_GAME_LOG`). The daemon
source reads the `slopd.service` user journal (override with `SLOPWORLD_DAEMON_UNIT`).

## Machine-readable output

`--json` is accepted on any command and prints the answer as newline-delimited JSON
instead of formatted text. It renders the shaped answer (filtered task list, name list,
status object), so a script gets the same data the reader saw.
