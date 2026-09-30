# Paths and files

These tables show default paths and supported overrides. Most Linux SlopWorld paths use XDG directories. Unity logs and the macOS profile use the locations shown. When an XDG variable is unset, SlopWorld uses the standard per-user directory. On Unix, mode `0600` gives read and write access only to the owner.

## Daemon configuration

| Path | Override | Description |
| --- | --- | --- |
| `~/.config/slopworld/config.toml` | `SLOPD_CONFIG` | Main daemon configuration, seeded on first run. |
| `~/.config/slopworld/endpoint.toml` | `SLOPD_ENDPOINT` | Effective URL and token while the daemon is running. Mode `0600`. |
| `~/.config/slopworld/grants.toml` | beside `SLOPD_CONFIG` | Active scoped bearer grants. Mode `0600`. |
| `~/.config/slopworld/prompts/<name>.toml` | beside `SLOPD_CONFIG` | Personal prompt library items, one definition per file. |
| `~/.config/slopworld/breadcrumbs/<name>.toml` | beside `SLOPD_CONFIG` | Personal breadcrumb library items, one definition per file. |
| `~/.config/slopworld/file_actions/<name>.toml` | beside `SLOPD_CONFIG` | Personal file-action library items, one definition per file. |
| `~/.config/slopworld/shell_scripts/<name>.toml` | beside `SLOPD_CONFIG` | Personal shell-script library items, one definition per file. |
| `~/.config/slopworld/agent_templates/generation-<id>/<name>.toml` | beside `SLOPD_CONFIG` | Personal agent templates, one definition per file; `.index.toml` selects the committed generation. |
| `~/.config/slopworld/sandbox_presets/<name>.toml` | `SLOPD_PRESETS` root | User sandbox definitions, one definition per file. |
| `~/.config/slopworld/app_presets/<name>.toml` | `SLOPD_PRESETS` root | User app definitions, one definition per file. |
| `~/.config/slopworld/tasks.toml` | beside `SLOPD_CONFIG` | Task mailbox state. |
| `~/.config/slopworld/worktrees.toml` | beside `SLOPD_CONFIG` | Registered worktrees. |
| `~/.cache/slopworld/mounts/<project-id>/` | under `SLOPD_CACHE` | Shared managed cache data. It remains after you remove a worktree. |
| `~/.cache/slopworld/prompt-summaries.toml` | under `SLOPD_CACHE` | Cached prompt titles. Mode `0600`. |
| `~/.cache/slopworld/session-activity.toml` | under `SLOPD_CACHE` | Fallback state ages when tmux metadata is unavailable. Mode `0600`. |
| `~/.cache/slopworld/.anthropic-usage-*.json` | under `SLOPD_CACHE` | Short-lived usage responses and rate-limit backoff. Mode `0600`. |

## Data

| Path | Override | Description |
| --- | --- | --- |
| `$XDG_DATA_HOME/slopworld/sessions/<state-id>/` | `SLOPD_STATE` | Per-agent private state. The daemon assigns the opaque state ID at creation. |
| `$XDG_DATA_HOME/slopworld/sessions/<state-id>/launch-plan.json` | under `SLOPD_STATE` | Sanitized latest sandbox launch plan. Mode `0600`. The daemon keeps it with persistent state and never mounts it into the guest. |
| `$XDG_DATA_HOME/slopworld/sessions/.trash/` | under `SLOPD_STATE` | Removed or reset state, reclaimed after 14 days. |
| `<project_path>/.worktrees/<worktree-name>/` | `project.worktree_root` | Default managed checkout path. An explicit root uses `<root>/<project-name>/<worktree-name>/`. |
| `~/.config/slopworld/jukebox/<name>.toml` | `SLOPD_JUKEBOX` | User-defined radio stations. SlopWorld includes no stations. |
| `$XDG_DATA_HOME/slopworld/jukebox.toml` | `XDG_DATA_HOME` | Jukebox likes (`[[like]]` tables). |
| `$XDG_DATA_HOME/slopworld/profile` | `--profile`, `SLOPCAR_PROFILE`, or `SLOPWORLD_PROFILE` | Game profile: saves, screenshots, and `Config/`. |

## Mod settings

| Path | Description |
| --- | --- |
| `<profile>/Config/SlopWorld.toml` | Mod settings. |
| `<profile>/Config/ModsConfig.xml` | Mod list, seeded by the launcher when absent. |

## Logs

| Path | Used by | Override | Description |
| --- | --- | --- | --- |
| `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log` | Linux game | Use `SLOPWORLD_GAME_LOG` for `slopctl logs`. Use `LOG` for Makefile targets. | Unity writes Harmony and mod exceptions here. They do not appear in the terminal that launched the game. |
| `journalctl --user -u slopd` | Daemon | `SLOPWORLD_DAEMON_UNIT` for `slopctl logs` | Daemon log when it runs as a systemd user service. |

## tmux

The daemon runs tmux on a private socket named `slopworld`. Connect from the host with:

```sh
tmux -L slopworld list-sessions
tmux -L slopworld attach -t SESSION_NAME
```

## Sidecar worker

Makefile targets use the `slopworld-car` paths. The `slopcar` script uses the `slopworld` paths. Both follow XDG variables when set.

| Path | Used by | Override | Description |
| --- | --- | --- | --- |
| `~/.config/slopworld-car/` | Makefile targets | `SLOPCAR_CONFIG` | Sidecar configuration and endpoint descriptor. |
| `~/.local/share/slopworld-car/` | Makefile targets | `SLOPCAR_DATA` | Sidecar state. |
| `~/.local/share/slopworld-car/profile` | Makefile targets | `SLOPCAR_PROFILE` | Sidecar game profile. |
| `~/.config/slopworld/` | `slopcar` script | `SLOPCAR_CONFIG_DIR` | Sidecar configuration and endpoint descriptor. |
| `~/.local/share/slopworld/` | `slopcar` script | `SLOPCAR_DATA_DIR` | Sidecar state. |

## macOS game profile

| Path | Used by | Override | Description |
| --- | --- | --- | --- |
| `~/Library/Application Support/SlopWorld/sidecar-profile` | macOS Makefile targets | `MAC_PROFILE` | Separate profile used by the native macOS game. |
