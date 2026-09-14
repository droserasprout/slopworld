# Backup and recovery

**Back up your data before using SlopWorld.** The sandbox is not a security boundary.
Project directories are read-write, and agents can modify anything inside them.

## What to back up

| State | Path | Notes |
| --- | --- | --- |
| Daemon configuration | `~/.config/slopworld/config.toml` | Agents, projects, settings, credentials paths. |
| User presets | `~/.config/slopworld/presets/*.toml` | Custom sandbox and command presets. |
| Agent private state | `~/.local/share/slopworld/sessions/` | Per-agent tool state, history, configuration copies. |
| Game profile | `~/.local/share/slopworld/profile` | Saves, screenshots, mod settings. |
| Jukebox data | `~/.local/share/slopworld/jukebox/` | User-defined radio stations. |
| Jukebox likes | `~/.local/share/slopworld/jukebox.toml` | Liked songs. |
| Task mailbox | `~/.config/slopworld/tasks.toml` | Delegated task state. |

The daemon token is stored authoritatively in `[daemon].token` in
`~/.config/slopworld/config.toml`. The daemon generates
`~/.config/slopworld/endpoint.toml` as a 0600 URL-and-token descriptor; if that descriptor
is missing, it is recreated from the configuration while the daemon starts. Back up
`config.toml`, not just the endpoint descriptor. Prompt summaries and session activity are
cached and can be regenerated.

## Restart vs. reset vs. delete

| Action | Effect |
| --- | --- |
| **Restart** | Kills the agent process and sandbox, starts fresh. Private state is preserved. The `pasta` network namespace is recreated. Use this after network changes. |
| **Reset Storage** | Moves the agent's private state to a 14-day trash directory and starts from a clean seed. Use this for damaged or intentionally fresh tool state. |
| **Delete** | Removes the agent from configuration. Private state moves to trash. |

## Trash and recovery

Reset and delete move private state to
`~/.local/share/slopworld/sessions/.trash/`. Directories there are reclaimed after 14
days. To recover, stop the agent, move the directory back from trash to
`sessions/<state-id>/`, and restart.

Configured-but-stopped agents are not age-pruned. Orphaned state directories (from
deleted agents) appear in the Settings > Configuration storage inventory and require
explicit deletion.

## Daemon restart

`make install-daemon` restarts the daemon when its binary changes. Tmux sessions and
the game survive because both run outside the daemon's cgroup. The daemon rebuilds
each running agent's terminal emulator from the surviving tmux pane, restoring
scrollback, alternate-screen mode, and pane titles.

State and timing metadata are carried through tmux's `@slopworld_*` session options.
When those are absent (full machine reboot), the daemon falls back to
`session-activity.toml`.

## Full recovery after reboot

After a machine reboot, start the daemon:

```sh
systemctl --user start slopd
```

Agents with autostart enabled will start automatically. Others can be started from the
sidebar. Private state, configuration, and the game profile are all
on disk and do not depend on the running daemon.
