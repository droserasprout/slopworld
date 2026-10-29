# Agent collaboration

Tasks carry assignments and reports between agents. Workers are agents spawned
for an assignment; scoped grants control access to selected agent terminals.
Host sessions remain accessible only with the root token.

## Worker delegation

Start with a working agent in a project, as in [Quickstart](../getting-started/quickstart.md).
This example creates a persistent worker so you can inspect its terminal afterward.

### Prerequisites

1. Save a working agent as a template using **Save as template**, or create one
   in Library. See [Configuration and templates](configuring-agents.md#templates).
2. If an agent will spawn workers, add the template under
   **Settings > Agents > Workers**. The host can use any catalog template;
   scoped agents can use only allowed templates in their own project.

### Assignment and reporting

1. In a host shell, list templates, then spawn a worker. Replace `repo` and
   `review` with your project and template names:

   ```sh
   slopctl template list --project repo
   slopctl worker spawn --project repo --template review "Review the README and report unclear setup steps. Do not edit files."
   ```

2. Keep the returned task ID. Open **Tasks** in the sidebar to read its dialogue,
   or run `slopctl task wait ID` to wait for its final result. Workers receive
   their task ID and connection credentials automatically.
3. The worker reads and accepts the task, reports progress, and finishes or fails
   it using the [task commands](../reference/slopctl.md#task-commands). Configure its initial
   instructions under **Settings > Agents > Workers** if it needs this guidance.
4. Read the final report. For tasks that change files, follow
   [Git](../workspace/review-changes.md). Use a
   [new worktree](../workspace/project-worktrees.md#create-and-use-a-worktree) when the worker
   should edit an independent checkout.

The worker prompt can refer to `$SLOPWORLD_TASK_ID`. It is separate from the
prompt used when creating an agent.

### Parent-worker access

Open **Edit agent > Workers** to enable delegation, narrow the allowed templates,
choose access to new workers, and allow further delegation. The daemon's Worker
settings provide the ceiling. Templates save these choices; workers copy them once.
Further delegation is disabled by default.

Parents always read task progress, results, and startup diagnostics. Four independent
checkboxes control additional permissions:

- **Send follow-up instructions** (enabled by default).
- **Cancel tasks** (enabled by default).
- **Type in the terminal** (disabled by default).
- **Stop and restart workers** (disabled by default).

Each permission can be enabled without enabling the others. Daemon settings can
limit each permission for newly created workers.

Change an existing relationship in the child worker's **Workers** tab, which shows
**What [parent] can do** and its captured permissions. Parent policy edits affect future workers
only. Relationships follow stable identities through renames and never attach to a
replacement with the same name. Legacy workers without a captured parent identity
must be replaced by a newly spawned worker before relationship access can be edited.
Manual grants remain independent; host access remains root-only.

A parent with **Send follow-up instructions** enabled can send `slopctl task followup --id ID "Check the tests"`
to wake a running interactive worker. The daemon waits for readiness and records the
request and submission outcome in task history. Submission does not accept or reopen
the task. Follow-ups are rejected for canceled tasks, stopped workers, and shell
workers. Use a new worker when the process has exited.

### Cleanup and retries

A persistent worker remains available after its process exits. Remove it when
inspection is complete; remove any worktree separately. `--one-shot` instead
removes the worker session on exit. If a worker stops before finishing its task,
read the failure and create a new task to retry. See
[Worker lifecycle](../reference/slopctl.md#workers-and-templates).

## Task mailboxes

<img src="../images/task-mailboxes.jpg" alt="Tasks sidebar with completed worker assignments and a selected task's dialogue" align="right" width="320">

The Tasks view is global; the workspace Project filter does not hide its rows.
For delegation to an existing agent, use `slopctl task delegate AGENT "description"`
within the permissions below.

Agents can hand work to other agents within their grant scope and report to `host`,
the user at the keyboard. Terminal permissions and task participation are separate:
`ro` or `rw` controls terminal access. See [Using slopctl](../reference/slopctl.md) for task
commands, statuses, and lifecycle.

Tasks are shared by sender and recipient. Only the recipient can update an
uncanceled task. The root token can cancel queued or accepted tasks through the
task board, API, or `slopctl task cancel ID`. Parents with **Cancel tasks** enabled can also cancel their
working worker tasks. Cancellation does not stop the worker process. Participants can remove
terminal tasks; root can also remove unfinished tasks. Only root can send as `host`.

Task details and `slopctl task show ID` retain the original assignment and every
subsequent update in order, with sender, status, and time. Progress reports remain
available after completion and daemon restarts. Removing or pruning a task also
removes its history.

For batch cancellation and removal failure behavior, see
[Task batch operations](../reference/api.md#task-batch-operations).

## Terminal grants

| Level | Permissions |
| --- | --- |
| `ro` | List, read, and watch sessions, including state, screen output, and `capture-pane`. |
| `rw` | All `ro` permissions, plus sending keys, resizing, starting, stopping, and restarting sessions. |

Only the root token can create grants or create agents directly. See the
[API reference](../reference/api.md) for creating grants.
Scoped agents can spawn workers only in their own project and from templates
allowed by daemon policy. See [Worker configuration](configuring-agents.md#workers).

### Credentials and connectivity

For a manually created grant, follow the [CLI connection instructions](../reference/slopctl.md#connection)
with the scoped token. Every newly launched agent receives `SLOPD_URL` and
`SLOPD_TOKEN` automatically, with read-write access to its own session. It can use
task mailboxes and spawn workers from allowed templates in its own project.
Terminal input and stop/restart require their respective relationship permissions.
Access to unrelated existing terminals requires an additional grant. Host terminals
remain root-only. Agents already running need a fresh launch to receive credentials;
replacing only the daemon preserves credentials already delivered to surviving agents.
Keep root credentials private, as explained in the [Security model](../sandbox/security.md).

Agents and workers need network access to the daemon API to use grants and tasks.
`network = "none"` prevents this access. See
[Network configuration](configuring-agents.md#network) for other modes.

### Grant lifetime

Grants survive daemon restarts when their participants keep the same identities.
They are revoked when a grantor or target is renamed, removed, or replaced.
Automatic launch credentials are also revoked on stop, exit, or failed launch;
a fresh launch receives a new token. Manual grants for ordinary agents survive
stop/start. Worker cleanup revokes its task authority.
The daemon checks token permissions on every request. See
[Paths and files](../reference/paths.md) for grant storage.
