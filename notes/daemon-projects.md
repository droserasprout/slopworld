# Projects

Projects supply working directories and shared mounts; agents own launch settings.
`application/projects.rs` coordinates catalog policy and relocation through
[configuration transactions](daemon-config-stores.md). Session adapters supply identity,
attachment checks, and guards through commit or rollback. `config/resolution.rs`
resolves launch settings; `sandbox/` constructs mounts.

Temporary mode is fixed at creation. Rename updates saved agent, host-shell, and
library references in one transaction, checking library revisions before record writes.
Captured definitions and mount shortcuts hold copied values, not project references.
Removal is blocked while any saved reference or [worktree record](daemon-worktrees.md)
remains, including stopped sessions.

Configured temporary projects persist until removed. Library errands use in-memory
project records discarded with the session. Their directories follow the host's `/tmp`
cleanup policy rather than session deletion.

Projects and worktrees allocate opaque IDs under the session boundary and worktree
gate. Allocation accounts for retained storage and references; edits preserve identity.
The format belongs to `storage_id.rs`. Mount safety belongs to
[sandbox isolation](sandbox-isolation.md); editing to
[Configuring agents](../docs/src/agents/configuring-agents.md).
