# Agent task mailboxes

`tasks/service.rs` owns policy and participant authority; `tasks/io.rs` owns serialized
persistence and accepted state. Session/API adapters resolve callers. Sandboxes use
APIs, never task files. Arrival does not wake an agent, and reads do not consume tasks.

Authority follows stable session IDs: rename preserves it, replacement under the same
name does not. It is separate from [terminal grants](agent-grants.md). Participants
can remove terminal tasks; root can remove unfinished work. Removal deletes the
shared record and history for both sides. IDs remain reserved until daemon exit.

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
