# Command presets

A command preset tells SlopWorld which installed CLI to run and supplies its
configuration paths, initial state, and environment. Manage presets in
**Settings > Commands > Apps**.

<a id="agent-clis"></a>

## Choose a command

**Settings > Commands** separates the **Agent** and **Shell** defaults.
When [configuring an agent](../agents/configuring-agents.md), choose a named command preset
to include its CLI-specific configuration. An agent with only a raw `cmd` still
gets a sandbox, but none of that configuration.

## Custom presets

User app definitions go in `~/.config/slopworld/app_presets/` by default.
A definition replaces the supplied app with the same name.
See [Paths and files](../reference/paths.md) for directory overrides.

The `kind` field determines where a preset appears in Settings:

| Kind | List |
| --- | --- |
| `agent` (default when omitted) | Agent commands |
| `shell` | Shell commands |

<a id="shell-configuration"></a>
<a id="agent-shell"></a>
<a id="android-tools"></a>

## Sandbox access

Command presets identify the software to run. Sandbox presets control the host
files and tools it can access. See [Shell configuration](../terminals/agent-shells.md#shell-configuration)
for host dotfiles and [Sandbox presets](../sandbox/sandbox-presets.md) for mount
rules and additional tools.

<a id="related-workflows"></a>
<a id="usage-polling"></a>
<a id="prompt-summaries"></a>
<a id="library-items-and-errands"></a>
<a id="host-terminals"></a>
<a id="host-shells"></a>
