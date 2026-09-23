# Agent task mailboxes

For commands and lifecycle, see [Using slopctl](../docs/src/guides/slopctl.md).
For access rules, see [Agent collaboration](../docs/src/guides/agent-collaboration.md).

`slopd` owns persistent `tasks.toml` beside `config.toml`.
Progress, summaries, and worker-failure updates append to `tasks.journal`; loading replays
complete entries for the snapshot generation. Creation and removal write a new snapshot and
retire older journal entries. Keep both files together when backing up or moving task state.
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
