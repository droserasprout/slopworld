# Agent task mailboxes

`slopd/src/tasks.rs` defines mailbox records; `tasks/service.rs` owns policy, accepted
ID/order indexes and participant authority. Persistence belongs to the selected disk
adapter. SessionRuntime task operations and HTTP handlers resolve caller identities.
Sandboxes use the API rather than editing storage.
Each task has one shared record; reads do not consume it.
The body records the original request; `history` retains every accepted update in
commit order, including status-only changes, cancellation, and worker-exit failure.
Each entry records its sender label, status, optional note, and timestamp. The
sender label comes from the caller resolved at update time; authorization still uses
the stable participant identity. Earlier entries retain their recorded labels. The latest
`note` remains a snapshot for consumers; task details, copy, and CLI output show the
chain. Records without history seed only their surviving latest update on load.
Removing or pruning a task removes its history with the record.

Participants persist stable session state IDs separately from display names. Rename
retains mailbox authority; replacement under the same name does not. Records require
participant IDs. Failed persistence leaves memory unchanged for retry.

Participant removal affects both sides and requires a terminal task state. Root can
remove unfinished work. Task authority is separate from terminal-input authority;
[grants](agent-grants.md) and [Agent collaboration](../docs/src/agents/agent-collaboration.md)
own access rules.

`application/task_summaries.rs` owns background task summaries, using the shared
title provider/cache and accepted daemon settings. Task stamps protect publication
after provider work; session adapters only schedule the request. Summaries do not
use session title policy; see
[title ownership](agent-titles.md). The mod host task board is integrated by
[the sidebar](mod-sidebar.md). Worker lifecycle belongs to [workers](daemon-workers.md),
notification limits to [discovery](agent-task-discovery.md), commands to
[Using slopctl](../docs/src/reference/slopctl.md), and backup requirements to
[Backup and recovery](../docs/src/maintenance/backup-and-recovery.md).

`tasks/io.rs` owns task startup reads and serialized disk operations. Application
services and session runtime share cloned handles to one task store; the session adapter resolves participants and explicitly
supplies the active authorization lease for each operation. The blocking worker
retains that lease under the task-store mutex,
so requester cancellation cannot release it before disk and accepted-state publication.
Worker creation retains its entire startup/cleanup operation after requester cancellation.
Summary completions check an accepted task incarnation and cannot overwrite an
already accepted summary; worker-exit updates check the
recipient's stable identity and retain terminal results. Neither recreates a deleted task.

Bulk cancel/remove/prune prevalidate all existing selected tasks, then commit in stored
listing order, stopping at the first I/O failure. `TaskBatchResult` reports `committed`,
`unchanged`, `absent`, `failed` (ID/error), `unattempted`, and current cancellation `tasks`;
`removed` counts committed retirements. Partial completion is an HTTP success response,
not an all-or-nothing error. Validation errors commit nothing. Authorized cancellation
of an already canceled task and bulk removal of an absent task are safe retries.
A disconnect may leave the owned batch running; clients may retry the whole selection.
The mod applies confirmed changes, reports relevant failures, and refreshes the board.
CLI prune prints the result and exits unsuccessfully when work failed or was unattempted.

New task IDs use the shared 16-character allocator. Accepted IDs and configured/live
worker references reserve identities; deletion retains an in-memory reservation for
this daemon lifetime. Full records live in
`SLOPD_DATA/tasks/`; updates replace one record and removals unlink selected records.
There is no runtime journal or polling. Persisted `storage_order` remains separate
from timestamps and random identity.
