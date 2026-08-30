# Settings

Settings is the gear icon in the top bar or the `Settings` command in the palette. It
opens a tabbed view over the terminal pane.

## Pages

| Page | Scope |
| --- | --- |
| General | Daemon connection details, Eco mode, Grandma mode, temperature units, and clock format. |
| Appearance > Interface | Global UI scale, interface scheme, fonts, cursor, and status-bar readouts. |
| Appearance > Terminal | Terminal font, size, color scheme, and cursor color. |
| Sandbox | Built-in and user sandbox presets, commands, dependencies, and their resolved fields. |
| Integrations | Credentials, quota polling, prompt summaries, and generated `SLOPWORLD.md` instructions. |
| Commands | Default agent and shell presets, pager, editor, and syntax-highlighter templates. |
| Keyboard | Rebindable SlopWorld shortcuts. Hardcoded terminal keys remain in the [keyboard reference](keyboard-shortcuts.md). |
| Storage | Private-state inventory, reset, restore, and permanent deletion for recoverable entries. |
| Audio | RimWorld volume controls and jukebox playback, recognition, likes, and history. |
| RimWorld and About | The remaining game options and SlopWorld credits and version information. |

The [agent configuration guide](../guides/configuring-agents.md),
[sandbox guide](../guides/configuring-sandboxes.md), and
[integration reference](integrations.md) describe the fields managed by those pages.

## Configuration file

The palette's **Configuration: Edit config.toml** action opens the complete daemon
configuration, including fields without a Settings page. The editor redacts the daemon
token; saving parses and validates the replacement before writing it atomically.

## Applying changes

Local mod and audio changes take effect immediately; settings that are edited as a group
are written when the Settings view closes.

Daemon-backed pages stage changes until **Save**. The save validates the values, patches the
daemon, and rereads the page. Raw `config.toml` edits are parsed and replaced atomically.

Preset, agent, project, and shortcut editors have their own Save action. Running agents keep
their current sandbox until they are restarted; startup-only daemon values such as the
listener bind address require a daemon restart.

Network, DNS, sandbox, mount, and other agent-start settings take effect on the next start.

## Confirmation dialogs

Confirmations are reserved for destructive operations: killing or removing agents and
projects, resetting or deleting private state. Reversible appearance, audio, and mode
switches apply without a modal.
