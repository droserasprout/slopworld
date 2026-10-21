# Projects

Projects supply working directories and shared mounts; agents own launch settings.
`application/projects.rs` owns project catalog policy and relocation coordination.
It uses `application/configuration.rs` for transactions and workspace persistence directly.
`session/identity.rs`, `session/worktrees.rs`, and `session/boundary.rs` supply
identity allocation, live attachment checks, and operation guards through commit or rollback. Effective launch
resolution belongs to `config/resolution.rs`, and mount construction to `sandbox/`.
See [configuration stores](daemon-config-stores.md) for saved and effective values.

Temporary mode is fixed at project creation. Rename updates saved agent, host-shell, and library project references
in one journaled transaction. Library revision conflicts reject the rename before
record writes. Captured command/sandbox definitions and mount
shortcuts contain copied values, not project-name references. Removal is blocked by configured agents, saved host shells, or
[worktree records](daemon-worktrees.md), including stopped sessions.

Configured temporary projects persist until removed. Library errands instead use
in-memory temporary project records that are forgotten during session cleanup.
Both use directories under `/tmp/slopworld`; directory cleanup follows the host's
`/tmp` policy.

Mount rules belong to [sandbox isolation](sandbox-isolation.md). Project editing
and next-start behavior are described in [Configuring agents](../docs/src/agents/configuring-agents.md).

New projects and registered worktrees allocate 16-character opaque IDs while holding
the session boundary and worktree mutation guard. Persisted identities must use
16 lowercase hexadecimal characters. Project allocation checks retained managed-cache
directories and worktree
project references; worktree allocation checks configured/live attachments and record
destinations. Edits retain identity.
