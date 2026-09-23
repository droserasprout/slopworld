# Settings

Open Settings with the gear icon in the top bar or the **Settings** command in the palette.
Settings opens as tabs over the terminal pane.

Settings groups controls by function: general game and daemon controls, appearance and
terminal presentation, sandbox and command definitions, integrations, keyboard, storage,
audio, RimWorld options, and credits. The [agent configuration guide](../guides/configuring-agents.md),
[sandbox guide](../guides/configuring-sandboxes.md), and
[integration reference](integrations.md) describe the fields managed by those groups.

The **Storage** page lists private agent state and shared caches. Use it to reset an agent,
restore state from trash, delete orphaned or trashed state, or empty the trash.

Worker settings are under **Settings > Agents > Workers**. Add templates to the worker
allowlist so agents can use them to create workers. New templates do not enter the allowlist
automatically. Edit the initial worker prompt on this page. New workers receive the saved
prompt. The Worker menu can use any catalog template. Allowlist changes do not affect
existing workers.

## Configuration file

The palette's **Configuration: Edit config.toml** action opens the full daemon
configuration, including fields without a Settings page. The editor hides the daemon token.
Before it saves, it parses and checks the replacement. It then writes the file atomically.

## Applying changes

Local mod and audio settings take effect immediately.
The game saves grouped settings when you close Settings.

Daemon settings pages keep edits until you select **Save**. The daemon checks and applies
the values. The page then reloads.
The daemon also reloads valid external edits to `config.toml`. If an edit is invalid, it keeps
the current settings.

Preset, agent, project, and library editors have separate **Save** actions. Running agents keep
their current sandbox until restart.
Settings that apply only at daemon startup, such as the listener address, require a restart.

Agent settings such as network, DNS, sandbox, and mounts take effect on the next start.

## Confirmation dialogs

Confirmation dialogs apply only to destructive operations:

- Stopping or removing agents and projects.
- Resetting or deleting private state.

Reversible appearance, audio, and mode changes apply without a dialog.
