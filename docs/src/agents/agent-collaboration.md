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

### Cleanup and retries

A persistent worker remains available after its process exits. Remove it when
inspection is complete; remove any worktree separately. `--one-shot` instead
removes the worker session on exit. If a worker stops before finishing its task,
read the failure and create a new task to retry. See
[Worker lifecycle](../reference/slopctl.md#workers-and-templates).

## Task mailboxes

The Tasks view is global; the workspace Project filter does not hide its rows.
For delegation to an existing agent, use `slopctl task delegate AGENT "description"`
within the permissions below.

Agents can hand work to other agents within their grant scope and report to `host`,
the user at the keyboard. Terminal permissions and task participation are separate:
`ro` or `rw` controls terminal access. See [Using slopctl](../reference/slopctl.md) for task
commands, statuses, and lifecycle.

Tasks are shared by sender and recipient. Only the recipient can update an
uncanceled task. The root token can cancel queued or accepted tasks through the
task board or API; `slopctl task` has no cancel subcommand. Participants can remove
terminal tasks; root can also remove unfinished tasks. Only root can send as `host`.

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
with the scoped token. Workers receive their connection credentials automatically;
see [Using slopctl](../reference/slopctl.md) for spawning them.
Keep root credentials private, as explained in the [Security model](../sandbox/security.md).

Agents and workers need network access to the daemon API to use grants and tasks.
`network = "none"` prevents this access. See
[Network configuration](configuring-agents.md#network) for other modes.

### Grant lifetime

Grants survive daemon restarts when their participants keep the same identities.
They are revoked when a grantor or target is renamed, removed, or replaced.
The daemon checks token permissions on every request. See
[Paths and files](../reference/paths.md) for grant storage.
