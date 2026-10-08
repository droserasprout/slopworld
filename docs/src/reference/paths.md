# Paths and files

See [Environment variables](environment.md) for which process reads each override
and for connection, logging, and runtime controls.

These tables show default paths and supported overrides. Most Linux SlopWorld paths use XDG directories. Unity logs and the macOS profile use the locations shown. Config paths use `$XDG_CONFIG_HOME` (fallback `~/.config`), data paths use
`$XDG_DATA_HOME` (fallback `~/.local/share`), and caches use `$XDG_CACHE_HOME`
(fallback `~/.cache`). On Unix, mode `0600` gives read and write access only to the owner.

Daemon application roots can be overridden independently with `SLOPD_CONFIG_ROOT`,
`SLOPD_DATA`, and `SLOPD_CACHE`. Settings and catalogs live under the configuration
root; private session state lives under `SLOPD_DATA/sessions`. Endpoint
discovery and launcher/game paths retain the independent overrides below.

## Daemon configuration

| Path | Override | Description |
| --- | --- | --- |
| `$XDG_CONFIG_HOME/slopworld/config.toml` | under `SLOPD_CONFIG_ROOT` | Machine settings, seeded on first run. |
| `$XDG_CONFIG_HOME/slopworld/endpoint.toml` | `SLOPD_ENDPOINT` | Effective URL and token while the daemon is running. Mode `0600`. |
| `$XDG_CONFIG_HOME/slopworld/projects/<id>.toml` | under `SLOPD_CONFIG_ROOT` | Project definitions. |
| `$XDG_CONFIG_HOME/slopworld/prompts/<name>.toml` | under `SLOPD_CONFIG_ROOT` | Personal prompt library items, one definition per file. |
| `$XDG_CONFIG_HOME/slopworld/breadcrumbs/<name>.toml` | under `SLOPD_CONFIG_ROOT` | Personal breadcrumb library items, one definition per file. |
| `$XDG_CONFIG_HOME/slopworld/file_actions/<name>.toml` | under `SLOPD_CONFIG_ROOT` | Personal file-action library items, one definition per file. |
| `$XDG_CONFIG_HOME/slopworld/shell_scripts/<name>.toml` | under `SLOPD_CONFIG_ROOT` | Personal shell-script library items, one definition per file. |
| `$XDG_CONFIG_HOME/slopworld/agent_templates/` | under `SLOPD_CONFIG_ROOT` | Personal agent templates; copy the entire directory for backup. |
| `$XDG_CONFIG_HOME/slopworld/sandbox_presets/<name>.toml` | under `SLOPD_CONFIG_ROOT` | User sandbox definitions, one definition per file. |
| `$XDG_CONFIG_HOME/slopworld/app_presets/<name>.toml` | under `SLOPD_CONFIG_ROOT` | User app definitions, one definition per file. |
| `$XDG_CONFIG_HOME/slopworld/jukebox/<name>.toml` | under `SLOPD_CONFIG_ROOT` | User-defined radio stations. SlopWorld includes no stations. |
| `$XDG_CACHE_HOME/slopworld/mounts/<project-id>/` | under `SLOPD_CACHE` | Shared managed cache data. |

## Launcher configuration

| Path | Override | Description |
| --- | --- | --- |
| `$XDG_CONFIG_HOME/slopworld/game.toml` | `--game`, then `SLOPWORLD_GAME` | Default Linux game directory, remembered after mod installation succeeds. |

With no `XDG_CONFIG_HOME`, the file lives at `~/.config/slopworld/game.toml`.
It contains one `path` string. Installing the mod into a Linux game's `Mods`
directory stores the canonical game directory path.

## Data

| Path | Override | Description |
| --- | --- | --- |
| `$XDG_DATA_HOME/slopworld/agents/<state-id>.toml` | under `SLOPD_DATA` | Durable agent definitions. |
| `$XDG_DATA_HOME/slopworld/host_shells/<id>.toml` | under `SLOPD_DATA` | Host-shell definitions and remembered directories. |
| `$XDG_DATA_HOME/slopworld/worktrees/<id>.toml` | under `SLOPD_DATA` | Registered checkout records and phases. |
| `$XDG_DATA_HOME/slopworld/tasks/<id>.toml` | under `SLOPD_DATA` | Complete task records; no runtime task journal. |
| `$XDG_DATA_HOME/slopworld/grants.toml` | under `SLOPD_DATA` | Scoped credentials, mode `0600`. |
| `$XDG_DATA_HOME/slopworld/workspace.save-journal` | under `SLOPD_DATA` | Pending transaction recovery; do not delete. |
| `$XDG_DATA_HOME/slopworld/sessions/<state-id>/` | under `SLOPD_DATA` | Per-agent private state. The daemon assigns the opaque state ID at creation. |
| `$XDG_DATA_HOME/slopworld/sessions/.trash/` | under `SLOPD_DATA` | Recoverable private-state trash. |
| `<project_path>/.worktrees/<worktree-name>/` | `project.worktree_root` | Default managed checkout path. An explicit root uses `<root>/<project-name>/<worktree-name>/`. |
| `$XDG_DATA_HOME/slopworld/jukebox.toml` | `XDG_DATA_HOME` | Jukebox likes (`[[like]]` tables). |
| `$XDG_DATA_HOME/slopworld/profile` | `--profile`, then `SLOPCAR_PROFILE`, then `SLOPWORLD_PROFILE` | Game profile: saves, screenshots, and `Config/`. |

`just run` passes `PROFILE` as the launcher's `--profile`. See
[Game profiles](../maintenance/game-profiles.md) for initialization and
[Backup and recovery](../maintenance/backup-and-recovery.md) for backup scope.

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
[Attaching to tmux](../terminals/terminal.md) for native Linux commands.

## Sidecar worker

just recipes use the `slopworld-car` paths. The `slopcar` script uses the `slopworld` paths. Both follow XDG variables when set.

| Path | Used by | Override | Description |
| --- | --- | --- | --- |
| `~/.config/slopworld-car/` | just recipes | `SLOPCAR_CONFIG` | Sidecar configuration and endpoint descriptor. |
| `~/.local/share/slopworld-car/` | just recipes | `SLOPCAR_DATA` | Sidecar state. |
| `~/.local/share/slopworld-car/profile` | just recipes | `SLOPCAR_PROFILE` | Sidecar game profile. |
| `$XDG_CONFIG_HOME/slopworld/` | `slopcar` script | `SLOPCAR_CONFIG_DIR` | Sidecar configuration and endpoint descriptor. |
| `~/.local/share/slopworld/` | `slopcar` script | `SLOPCAR_DATA_DIR` | Sidecar state. |

## macOS game profile

| Path | Used by | Override | Description |
| --- | --- | --- | --- |
| `~/Library/Application Support/SlopWorld/sidecar-profile` | macOS just recipes | `MAC_PROFILE` | Separate profile used by the native macOS game. |
