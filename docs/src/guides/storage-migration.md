# Migrating daemon storage

Daemon settings and workspace records now live in separate stores. Existing inline
projects, agents and host shells, task snapshots/journals, worktree catalogs and
config-root grants require an explicit offline migration. Startup reports legacy
stores and stops; it does not convert them automatically.

1. Keep a complete backup of configuration, data and private state. See
   [Backup and recovery](backup-and-recovery.md).
2. Stop the daemon, including any service or container that would restart it.
3. With the same path overrides used by that daemon, run `just migrate-storage`
   from the repository. The installed equivalent is `slopd --migrate-storage`.
4. Start the updated daemon and use the matching mod build. Check projects, agents,
   host tabs, worktrees, task history and grants before discarding backups.

The command reserves the configured daemon port for its duration and fails if that
port is occupied. It does not launch agents, move Git checkouts or change agent
private state. New IDs are assigned only to projects or host shells lacking them;
existing agent, worktree, task and participant identities remain intact.

If a custom `SLOPD_CONFIG` previously kept catalogs beside a settings file outside
`SLOPD_CONFIG_ROOT`, move those complete catalog directories into the configuration
root while stopped before migration. The command reports any such directory rather
than silently ignoring it; resolve conflicting catalogs without merging indexes or
template generations.

Migration validates all sources and destinations before committing. Conflicting
legacy/new stores, malformed records, unreadable files and invalid or incomplete
task-journal tails abort with an error. There is no implicit salvage mode: repair
from the backup before retrying. Root comments, credentials and record extensions
are retained.

An interrupted attempt leaves a private `workspace.save-journal` under the data
root. Recovery restores the original files and removes the attempt's new records
before another plan can allocate IDs. Do not delete this journal. If a root override
or settings filename changed, restore the original `SLOPD_CONFIG_ROOT`, `SLOPD_DATA`
and `SLOPD_CONFIG` mapping before retrying. Rerunning after successful migration
validates the new stores without changing their IDs.

For a sidecar, stop its daemon and run the migration executable inside a container
with the same config and data mounts and user identity. Both mounts are required;
copying only the config volume no longer copies workspace state.

Settings and workspace files are API-owned while the daemon runs. For manual repair,
stop it first. Independent library items still support live reload.
See [Paths and files](../reference/paths.md) for the layout.
