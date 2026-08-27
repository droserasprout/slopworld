# Settings

Settings is the gear icon in the top bar or the `Settings` command in the palette. It
opens a tabbed view over the terminal pane.

## Pages

**Appearance** — UI color scheme, UI font and size, terminal font, size, theme, and
cursor color. Cursor grayscale toggle. Sidebar agent-row indicators (autostart, resume,
host networking). Status-bar visibility for usage, clock position (right / center /
hidden), clock format (24-hour / 12-hour), jukebox door, and computer-core door.
Grandma mode and eco mode with dimming slider.

**Audio** — master, game, music, ambient, and UI volume sliders. Radio mute and
stop-on-exit toggles (the same values the jukebox menu exposes).

**Terminal** — terminal font, size, color scheme, and cursor-color picker. This page
edits the same values as Appearance but groups them for the pane.

**Usage** — all quota windows in one table. Each row has a name, icon picker, poll
toggle, and optional interval; a blank interval uses the global poll interval.

**Credentials** — host paths for provider credentials. Credential files stay on the host
and are re-read each poll, never copied.

**Summaries** — per-CLI title policies (`never` / `once` / `always`), host-command
title toggle, minimum prompt length, and the OpenRouter model. Uses the key from the
Usage page.

**Commands** — sandbox preset and command-preset templates. Creating, editing, and
deleting user presets. `global.toml` is shown first.

**Configuration** — the daemon's `config.toml` in a raw text editor. Connection state,
daemon version, hostname, host/sidecar runtime, mod and RimWorld versions. Layout-mode
selector. Storage inventory with reset, restore, and delete actions.

## Apply behavior

| Scope | Examples | When it takes effect |
| --- | --- | --- |
| Mod, live on interaction | Sidebar width, folds, tab, filter, usage icons, radio station, cursor choice | Immediately; written on click/release. |
| Mod, live on change | Color scheme, font, size, terminal theme, eco mode, clock format | Immediately; written when the Settings view closes. |
| RimWorld-owned | Volume, UI scale, temperature unit | Immediately; written by RimWorld's own settings lifecycle. |
| Daemon, explicit Save | Usage rows, credentials, polling, title policies, commands | Staged locally. Save validates, patches the daemon, and rereads the page. |
| Raw configuration | The complete `config.toml` text | Save parses TOML and replaces the file atomically. |
| Object editors | Presets, agents, projects, shortcuts | Each editor has its own Save action. Running agents are not rebuilt from changed defaults. |

Startup-only daemon values (the listener bind address) require a daemon restart after
saving.

## Confirmation dialogs

Confirmations are reserved for destructive operations: killing or removing agents and
projects, resetting or deleting private state. Reversible appearance, audio, and mode
switches apply without a modal.
