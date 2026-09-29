# Agent task mailboxes

For commands and lifecycle, see [Using slopctl](../docs/src/guides/slopctl.md).
For access rules, see [Agent collaboration](../docs/src/guides/agent-collaboration.md).

`slopd` owns persistent `tasks.toml` beside `config.toml`.
Creation appends full records to `tasks.journal`. Progress, summaries, and worker-failure
updates append only mutable fields. Loading replays supported complete entries for the snapshot
generation and stops at an incomplete, invalid, or unsupported entry. Unreadable
journals and failed tail repairs prevent new writes until recovery. After the journal reaches
1 MiB, the next successful append compacts it into a snapshot. If compaction
fails, the daemon logs the error and retries on the next append. Removal, prune, and bulk
cancellation still write snapshots. Snapshot replacement advances the generation before
the daemon retires the journal. This prevents stale creation or update replay after
interrupted cleanup.
Keep both files together when backing up or moving task state. This is process-crash recovery,
not an fsync-backed power-loss guarantee. Older daemon versions cannot read the new journal operations.
Sandboxes never edit either file.
Each task has one shared record. Participant removal affects both sides and is
limited to tasks in terminal states. Root can remove unfinished work.

`host` is a reserved principal, accepted only with the root token. A session named
`host` does not gain that identity through a grant. Recipient validation permits
reports to the host without putting host sessions in a grant's scope.

The store returns tasks visible to the caller. CLI filters select entries from that result.
`--json` serializes the same result as the text output.
The CLI parser recognizes help immediately after the command name. Task text
preserves help words. Spawn options end before the task body.
Task authority is separate from terminal-input authority.
The grant's session scope is the delegation allowlist.

Worker creation returns both task and session identity. Scoped callers must use an allowlisted
template. Root callers can use any catalog template. See
[daemon-workers](daemon-workers.md) for bootstrap, sidebar metadata, and exit/retry
policy, and [agent-task-discovery](agent-task-discovery.md) for task discovery and its notification limits.

Task participants persist stable session state IDs separately from display names.
Renames retain mailbox authority; replacing a session with the same name does not.
Legacy records without IDs remain visible through root's global listing; only their
reserved host participant retains authority. Failed persistence leaves the in-memory
record unchanged so callers can retry.

A successful snapshot does not clear journal poisoning until journal cleanup succeeds.
Snapshot mutations may commit while appends remain blocked awaiting repair.
