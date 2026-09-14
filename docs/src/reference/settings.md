# Settings

Settings is the gear icon in the top bar or the `Settings` command in the palette. It
opens a tabbed view over the terminal pane.

Settings groups controls by function: general game and daemon controls, appearance and
terminal presentation, sandbox and command definitions, integrations, keyboard, storage,
audio, RimWorld options, and credits. The [agent configuration guide](../guides/configuring-agents.md),
[sandbox guide](../guides/configuring-sandboxes.md), and
[integration reference](integrations.md) describe the fields managed by those groups.

**General > Experimental** has separate **Breadcrumbs** and **Instructions** switches.
Breadcrumb controls and generated SLOPWORLD.md instructions stay visible but greyed out
while their switch is disabled. Both default off and take effect when you press **Save**.
Individual agent preferences remain stored while disabled. Disabling Breadcrumbs cancels
pending breadcrumb injection; disabling Instructions prevents new SLOPWORLD.md mounts.
Restart running agents to remove existing mounts. The daemon enforces the same gates through
`[daemon] experimental_breadcrumbs = true` and `[daemon] experimental_instructions = true`.
Worker bootstrap settings are under **Settings > Integrations > Workers**. The editor
requires Instructions to be enabled, but spawned workers receive the saved bootstrap
prompt even when that switch is off.

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
