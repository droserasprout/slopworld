# Agent task mailboxes

`tasks/service.rs` owns policy and participant authority; `tasks/io.rs` owns serialized
persistence and accepted state. Session/API adapters resolve callers. Sandboxes use
APIs, never task files. Arrival does not wake an agent, and reads do not consume tasks. Explicit worker
follow-ups use the worker delivery path, with audit notes that preserve task status.

Authority follows stable session IDs: rename preserves it, replacement under the same
name does not. It is separate from [terminal grants](agent-grants.md). Participants
can remove terminal tasks; root can remove unfinished work. Removal deletes the
shared record and history for both sides. IDs remain reserved until daemon exit.
Root requests using the host identity can read any individual Task, matching the
operator's all-Tasks list. Scoped callers and root requests acting as an agent retain
participant visibility; this read policy does not change mutation authority.

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
Every task record requires an explicit history array, including an empty array for
a task without updates.

`application/task_summaries.rs` uses the shared [title provider](agent-titles.md) with
separate task policy. See [workers](daemon-workers.md) for startup and failure,
[sidebar](mod-sidebar.md) for the board, and [slopctl](../docs/src/reference/slopctl.md)
for commands.

The mod's `TaskStore` owns HTTP polling deadlines. The visible sidebar board and
open task readers independently demand polling; readers release their registration
on close or replacement, so hiding or switching the sidebar cannot freeze a reader.

`application/planner/` owns the manual todo.txt backlog and explicit Task/Worker links.
The mod's Planner view retains endpoint-scoped drafts; saves compare document revisions.
Only root may read or mutate the Planner API. Agents update existing Tasks, whose status
is projected on links independently of Planner completion.
See [Planner](../docs/src/customization/settings.md#planner).

The Planner's `TodoText` projection owns task-field edits, completion dates, filtering,
and sorting. It retains unrelated line content and opaque metadata; source mode is the
explicit escape hatch for raw edits. Planner navigation exposes projects and contexts;
quick-add parses inline `+project` and `@context` tags and merges them with the selected
filters without duplicates.
The view acquires a TaskStore reader demand for
worker progress and releases it on close. Drafts and asynchronous callbacks are scoped
to the endpoint; task selection retires text-field lifetimes. In-flight requests belong
to the cached endpoint draft and settle even when the view closes. Window navigation
has a separate lifetime, so a late task-reader response cannot reopen a closed view.
Both read and save replies project live worker links and status without rewriting the document.

Planner links live in `planner/links/<sw-id>.json` under the daemon data root, with
independent revision checks. Stored links contain typed association identities and fallback
labels; task status is projected from the Task store and never persisted with the links.
An explicit empty list suppresses inferred `sw-task:` and
worker-body associations. Worker links store private state IDs; client navigation resolves
the current session by ID. Removing links never mutates Tasks or worker processes.
Planner worker spawning commits both links after Task creation and before registration/
startup, under the owned session boundary, so failed startup remains inspectable.

Planner Markdown descriptions live under the daemon data root in
`planner/descriptions/<sw-id>.md`. The description store validates saved task membership
under the backlog gate and checks its own content revision. Task edits/completion preserve
sidecars; deletion retains them for recovery. Description drafts and in-flight replies
belong to endpoint/task identity, independently of selection. Planner Save writes the
backlog and then dirty descriptions, retaining failed drafts and accepted revisions
for retry. Reload description retries failed reads and adopts remote text for clean drafts;
for dirty drafts it preserves local text while updating the accepted text and revision
used by Save or Discard. Description request state owns these transitions independently
of rendering. Discard restores both from their last accepted values. Refresh retains
visible rows and field state when task text is unchanged, while updating link metadata.
Background refresh leaves controls enabled; replies cannot overwrite edits made while
the request was pending or supersede a newer save.

Supervised cancellation carries a checked selection into the task store. It applies
only to the sender's worker tasks and may cancel working tasks; ordinary recipient
cancellation retains its queued/accepted policy. Cancellation preserves the latest
result note and prevents subsequent recipient updates. It does not stop the process.

The host task reader and task menus share `TaskInfo.CanCancel`: unfinished worker tasks
include the working state, while ordinary mailbox tasks remain queued/accepted only.
