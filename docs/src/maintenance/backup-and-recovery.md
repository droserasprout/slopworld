# Backup and recovery

Back up your data before running agents, including writable project directories,
worktrees, and other mounted data. See the [Security model](../sandbox/security.md)
for sandbox limits.

## Backup contents

These are native Linux default locations. See [Paths and files](../reference/paths.md)
for overrides, XDG locations, and sidecar/macOS paths.

| Data | Linux default location | Include |
| --- | --- | --- |
| Configuration and catalogs | `~/.config/slopworld/` | `config.toml`, project records, user libraries, presets, and the entire `agent_templates/` directory including its index and generations. |
| Workspace records and authority | `~/.local/share/slopworld/` | Agent and host-shell definitions, worktree/task records, grants, and any pending workspace recovery journal. |
| Agent private state | `~/.local/share/slopworld/sessions/` | Tool state, history, configuration copies, and recoverable trash. |
| Game profile | `~/.local/share/slopworld/profile/` | Saves and mod settings. |
| Jukebox | `~/.config/slopworld/jukebox/` and `~/.local/share/slopworld/jukebox.toml` | Stations and liked songs. |
| External data | Your project, worktree, cache-mount, and credential-source paths | These are not backed up by copying SlopWorld's roots. |

Preserve `config.toml`, which contains the authoritative daemon token.
The endpoint descriptor can be recreated; it cannot replace that configuration.
Daemon caches, including activity and prompt summaries, are disposable.

## Backup procedure

1. Stop agents, host shells, and other tools writing to the data being copied.
   Stopping the native daemon alone leaves tmux and its processes running.
2. Save and close the game so its profile stops changing.
3. Stop the native daemon with `systemctl --user stop slopd`. For a sidecar, run
   `./slopcar/slopcar stop` from the checkout; this also ends the container's tmux
   and agent processes.
4. Copy the locations listed above, including any path overrides, into one backup.
   Preserve permissions and symlinks. Keep all writers stopped until copying ends,
   then start the daemon or container again.

Keep any `workspace.save-journal` with the same backup's configuration and data
files. Do not delete the journal or combine files from different backup revisions.

## Machine and profile recovery

Stop writers, close the game, and stop the daemon or container as in
[Create a backup](#backup-procedure). Restore configuration, data, private state,
the game profile, and external data from the same backup to their configured
locations. Finish restoring projects before starting agents that reference them.

If `workspace.save-journal` is present, restore the original `SLOPD_CONFIG_ROOT` and
`SLOPD_DATA` mapping before starting the daemon. Startup
recovers interrupted workspace transactions before loading records. This recovery
covers process interruption, not power-loss durability.

For the native Linux service, start the daemon with:

```sh
systemctl --user start slopd
```

For containers, follow [Sidecar worker](../deployment/sidecar.md); on macOS, follow the
[macOS guide](../deployment/macos.md). Agents with autostart enabled start automatically.
Start other agents from the sidebar. The daemon recreates its endpoint descriptor
from the saved configuration.

## Agent private-state recovery

Resetting or deleting a configured agent moves its state to recoverable trash,
retained for at least 14 days. Temporary-agent state is removed instead.
See [Private state](../agents/configuring-agents.md#private-state) for the lifecycle of
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
