# Agent task mailboxes

For commands and lifecycle, see [Using slopctl](../docs/src/guides/slopctl.md).
For access rules, see [Agent collaboration](../docs/src/guides/agent-collaboration.md).

`slopd` owns durable `tasks.toml` beside `config.toml`; sandboxes never edit it.
Each task has one shared record. Participant removal affects both sides and is
limited to terminal tasks; root can remove unfinished work.

`host` is a reserved principal, accepted only with the root token. A session named
`host` does not gain that identity through a grant. Recipient validation permits
reports to the host without putting host sessions in a grant's scope.

The store returns tasks visible to the caller. CLI filters shape that result;
`--json` serializes the same shaped answer as human-readable output.
Task authority is separate from terminal-input authority; the grant's session
scope is the delegation allowlist.

Root-only worker creation returns both task and session identity. See
[daemon-workers](daemon-workers.md) for bootstrap, sidebar metadata, and exit/retry
policy, and [agent-task-discovery](agent-task-discovery.md) for arrival notices.
