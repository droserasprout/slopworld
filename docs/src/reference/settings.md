# Settings

Settings is the gear icon in the top bar or the `Settings` command in the palette. It
opens a tabbed view over the terminal pane.

Settings groups controls by function: general game and daemon controls, appearance and
terminal presentation, sandbox and command definitions, integrations, keyboard, storage,
audio, RimWorld options, and credits. The [agent configuration guide](../guides/configuring-agents.md),
[sandbox guide](../guides/configuring-sandboxes.md), and
[integration reference](integrations.md) describe the fields managed by those groups.

Worker settings are under **Settings > Integrations > Workers**. The page selects the qualified
agent templates that agents may use for worker spawning and edits the bootstrap prompt. Spawned
workers always receive the saved prompt; changing the allowlist does not alter existing workers.

## Configuration file

The palette's **Configuration: Edit config.toml** action opens the complete daemon
configuration, including fields without a Settings page. The editor redacts the daemon
token; saving parses and validates the replacement before writing it atomically.

## Applying changes

Local mod and audio changes take effect immediately; settings that are edited as a group
are written when the Settings view closes.

Daemon-backed pages stage changes until **Save**. The save validates the values, patches the
daemon, and rereads the page. Raw `config.toml` edits are parsed and replaced atomically.

Preset, agent, project, and library item editors have their own Save action. Running agents keep
their current sandbox until they are restarted; startup-only daemon values such as the
listener bind address require a daemon restart.

Network, DNS, sandbox, mount, and other agent-start settings take effect on the next start.

## Confirmation dialogs

Confirmations are reserved for destructive operations: killing or removing agents and
projects, resetting or deleting private state. Reversible appearance, audio, and mode
switches apply without a modal.
