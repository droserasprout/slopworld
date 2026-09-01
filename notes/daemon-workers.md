# Task-owned workers

The root-only `POST /api/workers` route is the daemon's worker constructor. `slopctl spawn
[--durable] PARENT TASK...` calls it; `worker` is an alias. Existing agents keep using
`slopctl delegate AGENT TASK...`. `PARENT` must name an existing agent session whose project,
command (including an explicit command line), sandbox additions,
breadcrumbs, manifest and breadcrumb settings, private `/tmp`, network and DNS, resource limits,
mounts, and label. Worker autostart and auto-resume are disabled so a task never retries itself;
the endpoint returns the mailbox task and generated worker session name together. The caller from
`SLOPWORLD_SESSION` is the task sender and sidebar parent; `PARENT` only supplies the configuration
to clone. A root `host` caller leaves the worker at the top level.

The daemon writes the task to `tasks.toml` before creating or starting the child. A durable worker
is added to `config.toml` and remains inspectable if startup fails or the daemon restarts. A
one-shot worker is live-only and is removed when its process exits. Either kind is marked `failed`
with a daemon note when it cannot start, exits, is stopped, or is removed. A completed or failed
task is never overwritten by later cleanup. There is no automatic retry: inspect the task and
start a durable child manually, or create a new worker task for a retry.

Workers carry daemon-owned `worker`, `parent`, and `task_id` metadata. Running workers also
persist that identity in tmux, so a one-shot child that outlives a daemon redeploy can be
re-adopted under its parent. Ordinary session creation and editing cannot set it. The child
receives a fresh private-state identity, the exact
`SLOPWORLD_TASK_ID`, and the configured `[daemon.instructions] worker_prompt`, then uses
`slopctl task ID`, `accept`, `progress`, and `finish` against its own mailbox. The default prompt
describes that exact-task workflow; Settings > Integrations > Instructions can edit or reset it.
The `slopworld-worker` sandbox preset supplies
a run-scoped API credential through `SLOPD_URL` and `SLOPD_TOKEN`; it does not expose the daemon
config or root endpoint token. A network-capable parent is required so this API path works;
incompatible parents are rejected before launch.

Removing a parent does not cascade to its workers. A child with a missing parent remains a valid
session and is shown as a top-level row until it is removed or exits. The sidebar uses the
explicit metadata to nest visible workers as compact rows with a small grey robot mark (the
Agents tab icon) rather than the normal agent portrait; names are never parsed to infer
ownership.
