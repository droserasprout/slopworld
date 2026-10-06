# Backup and recovery

Back up your data before running agents, including writable project directories,
worktrees, and other mounted data. See the [Security model](../reference/security.md)
for sandbox limits.

## What to back up

These are native Linux default locations. See [Paths and files](../reference/paths.md)
for overrides, XDG locations, and sidecar/macOS paths.

| Data | Linux default location | Include |
| --- | --- | --- |
| Configuration and catalogs | `~/.config/slopworld/` | `config.toml`, project records, user libraries, presets, and the entire `agent_templates/` directory including its index and generations. |
| Agent private state | `~/.local/share/slopworld/sessions/` | Tool state, history, configuration copies, and recoverable trash. |
| Game profile | `~/.local/share/slopworld/profile/` | Saves and mod settings. |
| Jukebox | `~/.config/slopworld/jukebox/` and `~/.local/share/slopworld/jukebox.toml` | Stations and liked songs. |
| External data | Your project, worktree, cache-mount, and credential-source paths | These are not backed up by copying SlopWorld's roots. |

Preserve `config.toml`, which contains the authoritative daemon token.
The endpoint descriptor can be recreated; it cannot replace that configuration.
Daemon caches, including activity and prompt summaries, are disposable.

## Recover a machine or profile

Restore configuration, private state, the game profile, and external data to their
configured locations. Restore projects before starting agents that reference them.

For the native Linux service, start the daemon with:

```sh
systemctl --user start slopd
```

For containers, follow [Sidecar worker](sidecar.md); on macOS, follow the
[macOS guide](macos.md). Agents with autostart enabled start automatically.
Start other agents from the sidebar. The daemon recreates its endpoint descriptor
from the saved configuration. See [Updating](../updating.md) for daemon restart behavior.

## Recover one agent's private state

Resetting or deleting a configured agent moves its state to recoverable trash,
retained for at least 14 days. Temporary-agent state is removed instead.
See [Private state](configuring-agents.md#private-state) for the lifecycle of
restart, reset, and deletion.

To restore a trash entry:

1. Stop the agent.
2. If it has fresh private state, reset it.
3. Open **Settings > Storage**.
4. Select **Restore** on the agent's trash entry.

A deleted agent can be restored when its name is available and its saved project
and mounts remain valid. Restore the project configuration first if needed.
Configured agents retain their private state while Down. Remove unwanted orphaned
entries through **Settings > Storage**.

Include the data root (`SLOPD_DATA`, normally `~/.local/share/slopworld`) in the
backup: it contains agent and host-shell definitions, worktree and task records,
grants, and any pending workspace recovery journal. Stop the daemon before copying
these files together. See [Storage migration](storage-migration.md).
