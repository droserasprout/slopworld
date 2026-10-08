# Agent shells

Use an agent shell to run commands with an agent's sandbox settings. For commands
outside the sandbox, use a [host shell](host-shells.md).

## Open a sandbox shell

In the **Agents** sidebar, right-click an agent and choose **Shell**.
The shell inherits the agent's sandbox presets, network mode, DNS, resource limits,
and selected project's mounts. It sees the same filesystem as the agent.
See [Input and panes](terminal-interface.md) for terminal controls.

## Choose the agent's tool shell

**Settings > Commands > Defaults > Agent shell** chooses the shell advertised to
tools inside sandboxed agent sessions through the `SHELL` environment variable.
It defaults to `bash`. Restart the agent after changing it.

This setting is separate from the **Shell** default used by shell errands.
It does not change host panes, shell errands, or explicit tool-call shell overrides.
See [Command presets](../workspace/command-presets.md) for managing available commands.

## Shell configuration

Choosing a shell preset does not share host dotfiles. The `bash`, `zsh`, `fish`,
`nu`, and `pwsh` presets each have an optional `*-userdata` sandbox preset;
`sh` has none. Enable the matching preset in the
[agent editor](../agents/configuring-agents.md#agent-settings) to include the shell's host files.

These presets expose startup and configuration files as read-only, and history
and data paths as read-write. See [Sandbox presets](../sandbox/sandbox-presets.md)
to inspect or customize their mounts.
