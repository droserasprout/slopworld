# Paths and files

See [Environment variables](environment.md) for which process reads each override
and for connection, logging, and runtime controls.

The application roots have these environment overrides and Linux defaults:

| Environment variable | Default directory |
| --- | --- |
| `SLOPD_DATA` | `~/.local/share/slopworld` |
| `SLOPD_CONFIG_ROOT` | `~/.config/slopworld` |
| `SLOPD_CACHE` | `~/.cache/slopworld` |

The defaults follow `XDG_DATA_HOME`, `XDG_CONFIG_HOME`, and `XDG_CACHE_HOME`
when set to absolute paths on Linux. On macOS, configuration and data default to
`~/Library/Application Support/slopworld`, and cache defaults to
`~/Library/Caches/slopworld`. Settings and catalogs live under `<CONFIG>`;
private session state lives under `<DATA>/sessions`. Endpoint discovery and launcher/game paths
follow these roots unless their explicit overrides below select another path.
On Unix, mode `0600` gives read and write access only to the owner.

## Daemon configuration

| Path | Override | Description |
| --- | --- | --- |
| `<CONFIG>/config.toml` | under `SLOPD_CONFIG_ROOT` | Machine settings, seeded on first run. |
| `<CONFIG>/endpoint.toml` | `SLOPD_ENDPOINT` | Effective URL and token while the daemon is running. Mode `0600`. |
| `<CONFIG>/projects/<id>.toml` | under `SLOPD_CONFIG_ROOT` | Project definitions. |
| `<CONFIG>/prompts/<name>.toml` | under `SLOPD_CONFIG_ROOT` | Personal prompt library items, one definition per file. |
| `<CONFIG>/breadcrumbs/<name>.toml` | under `SLOPD_CONFIG_ROOT` | Personal breadcrumb library items, one definition per file. |
| `<CONFIG>/file_actions/<name>.toml` | under `SLOPD_CONFIG_ROOT` | Personal file-action library items, one definition per file. |
| `<CONFIG>/shell_scripts/<name>.toml` | under `SLOPD_CONFIG_ROOT` | Personal shell-script library items, one definition per file. |
| `<CONFIG>/agent_templates/` | under `SLOPD_CONFIG_ROOT` | Personal agent templates; copy the entire directory for backup. |
| `<CONFIG>/sandbox_presets/<name>.toml` | under `SLOPD_CONFIG_ROOT` | User sandbox definitions, one definition per file. |
| `<CONFIG>/app_presets/<name>.toml` | under `SLOPD_CONFIG_ROOT` | User app definitions, one definition per file. |
| `<CONFIG>/jukebox/<name>.toml` | under `SLOPD_CONFIG_ROOT` | User-defined radio stations. SlopWorld includes no stations. |
| `<CACHE>/mounts/<project-id>/` | under `SLOPD_CACHE` | Shared managed cache data. |

## Launcher configuration

| Path | Override | Description |
| --- | --- | --- |
| `<CONFIG>/game.toml` | `--game`, then `SLOPWORLD_GAME` | Default Linux game directory, remembered after mod installation succeeds. |

The file contains one `path` string. Installing the mod into a Linux game's `Mods`
directory stores the canonical game directory path.

## Data

| Path | Override | Description |
| --- | --- | --- |
| `<DATA>/agents/<state-id>.toml` | under `SLOPD_DATA` | Durable agent definitions. |
| `<DATA>/host_shells/<id>.toml` | under `SLOPD_DATA` | Host-shell definitions and remembered directories. |
| `<DATA>/worktrees/<id>.toml` | under `SLOPD_DATA` | Registered checkout records and phases. |
| `<DATA>/tasks/<id>.toml` | under `SLOPD_DATA` | Complete task records; no runtime task journal. |
| `<DATA>/grants.toml` | under `SLOPD_DATA` | Scoped credentials, mode `0600`. |
| `<DATA>/workspace.save-journal` | under `SLOPD_DATA` | Pending transaction recovery; do not delete. |
| `<DATA>/sessions/<state-id>/` | under `SLOPD_DATA` | Per-agent private state. The daemon assigns the opaque state ID at creation. |
| `<DATA>/sessions/.trash/` | under `SLOPD_DATA` | Recoverable private-state trash. |
| `<project_path>/.worktrees/<worktree-name>/` | `project.worktree_root` | Default managed checkout path. An explicit root uses `<root>/<project-name>/<worktree-name>/`. |
| `<DATA>/jukebox.toml` | under `SLOPD_DATA` | Jukebox likes (`[[like]]` tables). |
| `<DATA>/profile` | `--profile`, then `SLOPWORLD_PROFILE` | Game profile: saves, screenshots, and `Config/`. |

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
| `<CACHE>/Player.log` | Launcher and game | `SLOPWORLD_GAME_LOG` | Unity writes Harmony and mod exceptions here. They do not appear in the terminal that launched the game. |
| `journalctl --user -u slopd` | Daemon | `SLOPWORLD_DAEMON_UNIT` for `slopctl logs` | Daemon log when it runs as a systemd user service. |

The launcher passes `-logFile` to Unity and creates the log directory before
launching. `slopctl logs game` and `just logs` use the same default and environment
override. Passing a game `-logFile` argument takes precedence for that launch; set
`SLOPWORLD_GAME_LOG` to the same path when reading it.

## tmux

The default named socket is `slopworld`. `SLOPD_TMUX_SOCKET` overrides the name;
`TMUX_TMPDIR` changes the socket directory. See
[Attaching to tmux](../terminals/terminal.md) for native Linux commands.

## Sidecar host storage

`slopcar`, Linux recipes, and macOS recipes use the same application roots.
`slopworld --sidecar` and `slopctl --sidecar` select sidecar defaults explicitly.
Setting a profile or endpoint override does not select sidecar mode.

| Path | Override | Description |
| --- | --- | --- |
| `<CONFIG>/sidecar/` | under `SLOPD_CONFIG_ROOT` | Sidecar configuration, catalogs, and `endpoint.toml`. |
| `<DATA>/sidecar/daemon/` | under `SLOPD_DATA` | Persistent container daemon state. |
| `<DATA>/sidecar/profile/` | `--profile`, then `SLOPWORLD_PROFILE` | Host game's sidecar profile. |
| `<CACHE>/sidecar/Player.log` | `SLOPWORLD_GAME_LOG` | Host game's sidecar log. |

`SLOPD_ENDPOINT` overrides descriptor discovery for clients in either mode.
The container writes its descriptor in the mounted sidecar configuration directory.
`slopcar paths endpoint` prints that host location; `paths config`, `data`, `cache`,
`profile`, and `game-log` print the other resolved host paths without starting Docker
or creating directories. The launcher and `slopctl` offer the same `paths` command;
add `--sidecar` to select these defaults.

The host config and daemon-data directories are mounted at
`/home/slop/.config/slopworld` and `/home/slop/.local/share/slopworld` inside the
container. Host root overrides select mount sources; they are not forwarded as
container paths. Container cache is temporary and disappears with the container.
The game profile and game log stay on the host.
