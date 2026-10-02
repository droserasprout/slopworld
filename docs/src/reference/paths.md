# Paths and files

These tables show default paths and supported overrides. Most Linux SlopWorld paths use XDG directories. Unity logs and the macOS profile use the locations shown. Config paths use `$XDG_CONFIG_HOME` (fallback `~/.config`), data paths use
`$XDG_DATA_HOME` (fallback `~/.local/share`), and caches use `$XDG_CACHE_HOME`
(fallback `~/.cache`). On Unix, mode `0600` gives read and write access only to the owner.

## Daemon configuration

| Path | Override | Description |
| --- | --- | --- |
| `$XDG_CONFIG_HOME/slopworld/config.toml` | `SLOPD_CONFIG` | Main daemon configuration, seeded on first run. |
| `$XDG_CONFIG_HOME/slopworld/endpoint.toml` | `SLOPD_ENDPOINT` | Effective URL and token while the daemon is running. Mode `0600`. |
| `$XDG_CONFIG_HOME/slopworld/grants.toml` | beside `SLOPD_CONFIG` | Active scoped bearer grants. Mode `0600`. |
| `$XDG_CONFIG_HOME/slopworld/prompts/<name>.toml` | beside `SLOPD_CONFIG` | Personal prompt library items, one definition per file. |
| `$XDG_CONFIG_HOME/slopworld/breadcrumbs/<name>.toml` | beside `SLOPD_CONFIG` | Personal breadcrumb library items, one definition per file. |
| `$XDG_CONFIG_HOME/slopworld/file_actions/<name>.toml` | beside `SLOPD_CONFIG` | Personal file-action library items, one definition per file. |
| `$XDG_CONFIG_HOME/slopworld/shell_scripts/<name>.toml` | beside `SLOPD_CONFIG` | Personal shell-script library items, one definition per file. |
| `$XDG_CONFIG_HOME/slopworld/agent_templates/` | beside `SLOPD_CONFIG` | Personal agent templates; copy the entire directory for backup. |
| `$XDG_CONFIG_HOME/slopworld/sandbox_presets/<name>.toml` | `SLOPD_PRESETS` root | User sandbox definitions, one definition per file. |
| `$XDG_CONFIG_HOME/slopworld/app_presets/<name>.toml` | `SLOPD_PRESETS` root | User app definitions, one definition per file. |
| `$XDG_CONFIG_HOME/slopworld/tasks.toml` | beside `SLOPD_CONFIG` | Task mailbox snapshot. |
| `$XDG_CONFIG_HOME/slopworld/tasks.journal` | beside `SLOPD_CONFIG` | Task mailbox updates. |
| `$XDG_CONFIG_HOME/slopworld/worktrees.toml` | beside `SLOPD_CONFIG` | Registered worktrees. |
| `$XDG_CACHE_HOME/slopworld/mounts/<project-id>/` | under `SLOPD_CACHE` | Shared managed cache data. |

## Data

| Path | Override | Description |
| --- | --- | --- |
| `$XDG_DATA_HOME/slopworld/sessions/<state-id>/` | `SLOPD_STATE` | Per-agent private state. The daemon assigns the opaque state ID at creation. |
| `$XDG_DATA_HOME/slopworld/sessions/.trash/` | under `SLOPD_STATE` | Recoverable private-state trash. |
| `<project_path>/.worktrees/<worktree-name>/` | `project.worktree_root` | Default managed checkout path. An explicit root uses `<root>/<project-name>/<worktree-name>/`. |
| `$XDG_CONFIG_HOME/slopworld/jukebox/<name>.toml` | `SLOPD_JUKEBOX` | User-defined radio stations. SlopWorld includes no stations. |
| `$XDG_DATA_HOME/slopworld/jukebox.toml` | `XDG_DATA_HOME` | Jukebox likes (`[[like]]` tables). |
| `$XDG_DATA_HOME/slopworld/profile` | `--profile`, then `SLOPCAR_PROFILE`, then `SLOPWORLD_PROFILE` | Game profile: saves, screenshots, and `Config/`. |

`make run` passes `PROFILE` as the launcher's `--profile`. See
[Game profiles](../guides/game-profiles.md) for initialization and
[Backup and recovery](../guides/backup-and-recovery.md) for backup scope.

## Mod settings

| Path | Description |
| --- | --- |
| `<profile>/Config/SlopWorld.toml` | Mod settings. |
| `<profile>/Config/ModsConfig.xml` | Mod list, seeded by the launcher when absent. |

## Logs

| Path | Used by | Override | Description |
| --- | --- | --- | --- |
| `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log` | Linux game | Use `SLOPWORLD_GAME_LOG` for `slopctl logs`. | Unity writes Harmony and mod exceptions here. They do not appear in the terminal that launched the game. |
| `journalctl --user -u slopd` | Daemon | `SLOPWORLD_DAEMON_UNIT` for `slopctl logs` | Daemon log when it runs as a systemd user service. |

## tmux

The default named socket is `slopworld`. `SLOPD_TMUX_SOCKET` overrides the name;
`TMUX_TMPDIR` changes the socket directory. See
[Attaching from a terminal](../guides/terminal.md) for native Linux commands.

## Sidecar worker

Makefile targets use the `slopworld-car` paths. The `slopcar` script uses the `slopworld` paths. Both follow XDG variables when set.

| Path | Used by | Override | Description |
| --- | --- | --- | --- |
| `~/.config/slopworld-car/` | Makefile targets | `SLOPCAR_CONFIG` | Sidecar configuration and endpoint descriptor. |
| `~/.local/share/slopworld-car/` | Makefile targets | `SLOPCAR_DATA` | Sidecar state. |
| `~/.local/share/slopworld-car/profile` | Makefile targets | `SLOPCAR_PROFILE` | Sidecar game profile. |
| `$XDG_CONFIG_HOME/slopworld/` | `slopcar` script | `SLOPCAR_CONFIG_DIR` | Sidecar configuration and endpoint descriptor. |
| `~/.local/share/slopworld/` | `slopcar` script | `SLOPCAR_DATA_DIR` | Sidecar state. |

## macOS game profile

| Path | Used by | Override | Description |
| --- | --- | --- | --- |
| `~/Library/Application Support/SlopWorld/sidecar-profile` | macOS Makefile targets | `MAC_PROFILE` | Separate profile used by the native macOS game. |
