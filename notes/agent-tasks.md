# Agent task mailboxes

`slopd/src/tasks.rs` owns durable mailbox records; manager task operations and HTTP
handlers enforce caller authority. Sandboxes use the API rather than editing storage.
Each task has one shared record; reads do not consume it.

Participants persist stable session state IDs separately from display names. Rename
retains mailbox authority; replacement under the same name does not. Legacy records
without IDs remain visible to root, while only their reserved host participant
retains authority. Failed persistence leaves memory unchanged for retry.

Participant removal affects both sides and requires a terminal task state. Root can
remove unfinished work. Task authority is separate from terminal-input authority;
[grants](agent-grants.md) and [Agent collaboration](../docs/src/guides/agent-collaboration.md)
own access rules.

Task summaries reuse the title provider/cache without session title policy; see
[title ownership](agent-titles.md). The mod host task board is integrated by
[the sidebar](mod-sidebar.md). Worker lifecycle belongs to [workers](daemon-workers.md),
notification limits to [discovery](agent-task-discovery.md), commands to
[Using slopctl](../docs/src/guides/slopctl.md), and backup requirements to
[Backup and recovery](../docs/src/guides/backup-and-recovery.md).
