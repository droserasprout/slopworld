# Settings

Settings is the gear icon in the top bar or the `Settings` command in the palette. It
opens a tabbed view over the terminal pane.

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
