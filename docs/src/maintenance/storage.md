# Storage and caches

Open **Settings > Storage** to inspect agent private state and shared caches,
restore trash, or remove unused state.

## Private state

Agent private state holds tool configuration, history, and private copies of
files. It is separate from the project checkout. Restarting an agent reuses its
private state; resetting or deleting a configured agent moves that state to trash.
Temporary-agent state is removed instead. See
[Session lifecycle](../agents/session-lifecycle.md) for the full action table and
[Backup and recovery](backup-and-recovery.md#agent-private-state-recovery) for restoration.

## Shared caches

Cache mounts let checkouts share build data. **Settings > Storage** shows their
paths and sizes. Removing a cache mount removes checkout links but keeps the
cached files. See [Project mounts](../workspace/project-mounts.md#shared-cache-mounts) for setup,
conflicts, and removal.

## Files and saves

Projects, worktrees, external mounts, credentials, and game saves can live outside
agent private state. Resetting private state does not undo project edits. See
[Paths and files](../reference/paths.md) for locations and
[Backup contents](backup-and-recovery.md#backup-contents) for what to preserve.
