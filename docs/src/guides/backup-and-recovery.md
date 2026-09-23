# Backup and recovery

**Before you use SlopWorld, back up your data.** The sandbox is not a security boundary.
Agents can edit any file in a project directory that the sandbox mounts read-write.

## Data to include in backups

| State | Path | Notes |
| --- | --- | --- |
| Daemon configuration | `~/.config/slopworld/config.toml` | Agents, projects, settings, credentials paths. |
| User library and catalogs | `~/.config/slopworld/{prompts,breadcrumbs,file_actions,shell_scripts,agent_templates,sandbox_presets,app_presets}/*.toml` | Custom library entries, templates, sandboxes, and apps. |
| Agent private state | `~/.local/share/slopworld/sessions/` | Per-agent tool state, history, configuration copies. |
| Game profile | `~/.local/share/slopworld/profile` | Saves, screenshots, mod settings. |
| Jukebox stations | `~/.config/slopworld/jukebox/` | User-defined radio stations. |
| Jukebox likes | `~/.local/share/slopworld/jukebox.toml` | Liked songs. |
| Task mailbox | `~/.config/slopworld/tasks.toml` | Delegated task state. |

If you set path overrides, back up those locations too. See [Paths and files](../reference/paths.md).

The daemon reads its authoritative token from `[daemon].token` in `config.toml`.
At startup, it writes `endpoint.toml` with mode `0600`. This file contains the URL and token.
If `endpoint.toml` is missing, the daemon creates it from `config.toml`.
Include `config.toml` in your backup. The endpoint file cannot replace it.

The daemon can regenerate prompt-summary titles.
It uses `~/.cache/slopworld/session-activity.toml` as a fallback when tmux metadata is unavailable.
Include this file if you need its saved activity ages after a full reboot.

## Restart vs. reset vs. delete

| Action | Effect |
| --- | --- |
| **Restart** | Stops the process and sandbox. Starts a new process with the same private state. Private network mode gets a new `pasta` namespace. Restart after network changes. |
| **Reset private state** | Moves state to trash for 14 days. The next start copies host files and configured seeds. Use it to clear damaged state or tool data. |
| **Delete** | Deletes the agent from configuration. If private state exists, the daemon moves it to trash for 14 days. |

## Trash and recovery

Reset and delete move private state to
`~/.local/share/slopworld/sessions/.trash/`. The daemon keeps entries for at least 14 days.
It deletes expired entries during cleanup.
To restore private state:

1. Stop the agent.
2. If the agent has fresh private state, reset it.
3. Open **Settings > Storage**.
4. Select **Restore** on the agent's trash entry.

If you deleted the agent, **Restore** can add it back to the configuration when its name is free.
Configured agents keep their state while they are Down. The daemon does not delete this state because of age.
Orphaned state from deleted agents appears in **Settings > Storage**.
Delete these entries when you do not need them.

## Daemon restart

`make install-daemon` restarts the daemon when its binary or service file changes.
Tmux sessions and the game continue because they run outside the daemon's cgroup.
After restart, the daemon rebuilds each terminal emulator from its existing tmux pane.
It restores scrollback, alternate-screen mode, and pane titles.

Tmux session options named `@slopworld_*` preserve state and timing metadata.
After a full reboot, tmux loses these options. The daemon then uses `session-activity.toml` as a fallback.

## Full recovery after reboot

After a reboot, start the daemon:

```sh
systemctl --user start slopd
```

Agents with autostart enabled start automatically. Start other agents from the sidebar.
Private state, configuration, and the game profile remain on disk while the daemon is stopped.
