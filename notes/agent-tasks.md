# Agent task mailboxes

`tasks/service.rs` owns policy and participant authority; `tasks/io.rs` owns serialized
persistence and accepted state. Session/API adapters resolve callers. Sandboxes use
APIs, never task files. Arrival does not wake an agent, and reads do not consume tasks.
Explicit follow-ups use [worker delivery](daemon-workers.md) without changing task status.

Authority follows stable session IDs: rename preserves it, replacement under the same
name does not. It is separate from [terminal grants](agent-grants.md). Participants
can remove terminal tasks; root can remove unfinished work. Removal deletes the
shared record and history for both sides. IDs remain reserved until daemon exit.
Root acting as host can read all tasks. Scoped callers and root acting as an agent
can read only participating tasks. Mutation authority is separate.

Application and runtime share one store. Its I/O worker retains the authorization
lease through disk writes and publication after requester cancellation. Failed writes
leave accepted state unchanged. Summary and worker-exit completions check task and
session identity and cannot recreate deleted records. Summaries cannot replace accepted
summaries; exit updates preserve terminal results.
See [I/O tests](../slopd/src/tasks/io_tests.rs).

Bulk changes validate the whole selection before writing, then stop at the first I/O
failure. Partial completion returns per-item outcomes; clients apply confirmed changes
and can retry. Task batches are not one workspace transaction. History follows commit
order, independently of timestamps, with sender labels captured at update time.
Every task record requires a history array, empty when there are no updates.

`application/task_summaries.rs` uses the shared [title provider](agent-titles.md) with
separate task policy. See [workers](daemon-workers.md) for startup and failure,
[sidebar](mod-sidebar.md) for the board, and [slopctl](../docs/src/reference/slopctl.md)
for commands.

The mod's `TaskStore` owns HTTP polling deadlines. The visible sidebar board and
open task readers independently demand polling; readers release their registration
on close or replacement, so hiding or switching the sidebar cannot freeze a reader.

## Planner

`application/planner/` owns the manual todo.txt backlog and explicit Task/Worker links.
The mod's `PlannerDocumentState` owns endpoint-scoped backlog and description drafts,
revisions, and request settlement; saves compare document revisions. `PlannerView` owns
selection, its row field editor, polling demand, and window presentation.
Only root may read or mutate the Planner API. Agents update existing Tasks, whose status
is projected on links independently of Planner completion.
See [Planner](../docs/src/customization/settings.md#planner).

`TodoText` owns field edits and preserves unrelated text and metadata. The view holds
TaskStore polling demand while open. Drafts and requests belong to the endpoint and
stable task ID; replies settle after selection changes or closure. Navigation has a
separate lifetime so late replies cannot reopen a closed view.

Links have independent revisions and store identities and fallback labels, never live
status. An explicit empty list suppresses inferred associations. Worker navigation uses
private state IDs. Removing a link leaves its Task or worker intact. Spawn ordering
belongs to [workers](daemon-workers.md).

Description writes check saved task membership under the backlog gate and compare
content revisions. Deletion retains descriptions for recovery. Save writes the backlog
before dirty descriptions; partial failures retain drafts and accepted revisions for
retry. An endpoint switch stops further writes but lets accepted replies settle.
Backlog saves cannot remove rows with dirty descriptions. Refresh must preserve pending
edits and cannot supersede a newer save. UI procedures and storage paths belong to the
[Planner guide](../docs/src/customization/settings.md#planner).

## Cancellation

Supervised cancellation carries a checked selection into the task store. It applies
only to the sender's worker tasks, including working tasks. Ordinary recipients
can cancel only queued or accepted tasks, including through generic task updates.
Cancellation preserves the latest result note and prevents subsequent recipient
updates. It does not stop the process.

The host task reader and task menus share this policy through `TaskInfo.CanCancel`.
