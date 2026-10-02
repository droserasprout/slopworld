# Projects

Projects supply working directories and shared mounts; agents own launch settings.
`session/manager/projects.rs` owns project catalog mutations. Effective launch
resolution belongs to `config/resolution.rs`, and mount construction to `sandbox/`.
See [configuration stores](daemon-config-stores.md) for saved and effective values.

Temporary mode is fixed at project creation. Rename updates saved session references
in the same configuration write. Removal is blocked by configured sessions or
[worktree records](daemon-worktrees.md), including stopped sessions.

Configured temporary projects persist until removed. Library errands instead use
in-memory temporary project records that are forgotten during session cleanup.
Both use directories under `/tmp/slopworld`; directory cleanup follows the host's
`/tmp` policy.

Mount rules belong to [sandbox isolation](sandbox-isolation.md). Project editing
and next-start behavior are described in [Configuring agents](../docs/src/guides/configuring-agents.md).
