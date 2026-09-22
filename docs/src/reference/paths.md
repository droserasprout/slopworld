# Paths and files

All paths follow XDG conventions and can be overridden with the noted environment
variables. Paths marked `0600` are readable only by the owning user.

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
| `~/.config/slopworld/agent_templates/<name>.toml` | beside `SLOPD_CONFIG` | Personal agent templates, one definition per file. |
| `~/.config/slopworld/sandbox_presets/<name>.toml` | `SLOPD_PRESETS` root | User sandbox definitions, one definition per file. |
| `~/.config/slopworld/app_presets/<name>.toml` | `SLOPD_PRESETS` root | User app definitions, one definition per file. |
| `~/.config/slopworld/tasks.toml` | beside `SLOPD_CONFIG` | Task mailbox state. |
| `~/.cache/slopworld/mounts/<project-id>/` | `SLOPD_CACHE` | Shared managed cache mounts; retained independently of worktrees. |
| `~/.cache/slopworld/prompt-summaries.toml` | `SLOPD_CACHE` | Cached prompt titles. Mode `0600`. |
| `~/.cache/slopworld/session-activity.toml` | `SLOPD_CACHE` | Fallback state ages when tmux metadata is unavailable. Mode `0600`. |
| `~/.cache/slopworld/.anthropic-usage-*.json` | `SLOPD_CACHE` | Short-lived usage responses and rate-limit backoff. Mode `0600`. |

## Data

| Path | Override | Description |
| --- | --- | --- |
| `$XDG_DATA_HOME/slopworld/sessions/<state-id>/` | `SLOPD_STATE` | Per-agent private state. The daemon assigns the opaque state id at creation. |
| `$XDG_DATA_HOME/slopworld/sessions/<state-id>/launch-plan.json` | under `SLOPD_STATE` | Sanitized latest sandbox launch plan. Mode `0600`; retained with durable state and never mounted into the guest. |
| `$XDG_DATA_HOME/slopworld/sessions/.trash/` | under `SLOPD_STATE` | Deleted or reset state, reclaimed after 14 days. |
| `~/.config/slopworld/jukebox/<name>.toml` | `SLOPD_JUKEBOX` | User-defined radio stations. No stations ship with SlopWorld. |
| `$XDG_DATA_HOME/slopworld/jukebox.toml` | `XDG_DATA_HOME` | Jukebox likes (`[[like]]` tables). |
| `$XDG_DATA_HOME/slopworld/profile` | `SLOPWORLD_PROFILE` | Game profile: saves, screenshots, `Config/`. |

## Mod settings

| Path | Description |
| --- | --- |
| `<profile>/Config/SlopWorld.toml` | Mod settings. |
| `<profile>/Config/ModsConfig.xml` | Mod list, seeded by the launcher when absent. |

## Logs

| Path | Description |
| --- | --- |
| `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Player.log` | Unity game log. Harmony and mod exceptions land here, not in the launching terminal. |
| `journalctl --user -u slopd` | Daemon log (when running as a systemd user service). |

## tmux

The daemon runs tmux on a private socket named `slopworld`. Connect from the host with:

```sh
tmux -L slopworld list-sessions
tmux -L slopworld attach -t SESSION_NAME
```

## Sidecar worker

| Path | Override | Description |
| --- | --- | --- |
| `~/.config/slopworld-car/` | `SLOPCAR_CONFIG` in Makefile workflows; `SLOPCAR_CONFIG_DIR` in `slopcar` | Sidecar configuration and endpoint descriptor. |
| `~/.local/share/slopworld-car/` | `SLOPCAR_DATA` in Makefile workflows; `SLOPCAR_DATA_DIR` in `slopcar` | Sidecar state. |

## macOS game profile

| Path | Override | Description |
| --- | --- | --- |
| `~/Library/Application Support/SlopWorld/sidecar-profile` | `MAC_PROFILE` | Separate profile used by the native macOS game. |
