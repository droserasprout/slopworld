# Paths and files

All paths follow XDG conventions and can be overridden with the noted environment
variables. Paths marked `0600` are readable only by the owning user.

## Daemon configuration

| Path | Override | Description |
| --- | --- | --- |
| `~/.config/slopworld/config.toml` | `SLOPD_CONFIG` | Main daemon configuration, seeded on first run. |
| `~/.config/slopworld/endpoint.toml` | `SLOPD_ENDPOINT` | Effective URL and token while the daemon is running. Mode `0600`. |
| `~/.config/slopworld/presets/*.toml` | `SLOPD_PRESETS` | User sandbox and command presets. Replaces builtins by name. |
| `~/.config/slopworld/tasks.toml` | beside `SLOPD_CONFIG` | Task mailbox state. |
| `~/.config/slopworld/agent-templates.toml` | beside `SLOPD_CONFIG` | Personal agent templates. Mode `0600`; definitions are independent of the main config. |
| `~/.config/slopworld/prompt-summaries.toml` | beside `SLOPD_CONFIG` | Cached prompt titles. Mode `0600`. |
| `~/.config/slopworld/session-activity.toml` | beside `SLOPD_CONFIG` | Fallback state ages when tmux metadata is unavailable. Mode `0600`. |

## Data

| Path | Override | Description |
| --- | --- | --- |
| `$XDG_DATA_HOME/slopworld/sessions/<state-id>/` | `SLOPD_STATE` | Per-agent private state. The daemon assigns the opaque state id at creation. |
| `$XDG_DATA_HOME/slopworld/sessions/<state-id>/launch-plan.json` | under `SLOPD_STATE` | Sanitized latest sandbox launch plan. Mode `0600`; retained with durable state and never mounted into the guest. |
| `$XDG_DATA_HOME/slopworld/sessions/.trash/` | under `SLOPD_STATE` | Deleted or reset state, reclaimed after 14 days. |
| `$XDG_DATA_HOME/slopworld/jukebox/*.toml` | `SLOPD_JUKEBOX` | User-defined radio stations. No stations ship with SlopWorld. |
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
