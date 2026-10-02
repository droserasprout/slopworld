# Integrations and commands

## Agent CLIs

Command presets cover installed agent CLIs and common shells. Each has a `kind` of `agent` or
`shell`. The **Settings > Commands** page uses this field to separate the Agent and Shell
defaults. It reads both lists from the current catalog.
Each preset names software so the sandbox can supply its configuration paths, initial state,
and environment.

The `bash`, `zsh`, `fish`, `nu`, and `pwsh` shell presets have matching
`*-userdata` sandbox presets; `sh` does not. These presets are separate and opt-in.
When enabled, they expose startup and configuration files as read-only.
They expose history and data paths as read-write.
Choosing a shell preset alone does not share host dotfiles.

An agent with a raw `cmd` and no named `command` preset gets the sandbox but none
of the CLI-specific configuration.

User app definitions in the default `~/.config/slopworld/app_presets/` directory
replace supplied apps with the same name. If `kind` is omitted, it is `agent`.
See [Paths and files](paths.md) for overrides.

### Agent shell

Right-click an agent.
Select **Shell** to open a shell inside the same sandbox.
The shell inherits the agent's sandbox presets, network mode, DNS, resource limits, and
selected project's mounts.
It sees the same filesystem as the agent. Host shells from
the project heading do not carry per-agent settings.

Settings > Commands > Defaults > **Agent shell** controls the `SHELL` environment
variable inside sandboxed agent sessions. It defaults to `bash`, independently of
the **Shell** default used by shell errands. SlopWorld resolves the selected shell
preset (or custom executable) to an absolute executable path before launch. Clients such as
Codex reject a `bash` value without an absolute path. They use the account's login shell instead.
After you change **Agent shell**, restart the agent. Explicit
tool-call shell overrides, host panes, and shell errands are unaffected.

## Android tools

The `android-dev` preset exposes installed Android and Java tools read-only, with
private Android and Gradle state. Builds write to the project. Add `android-debug`
for USB devices or an accelerated emulator; it requires GPU, X11, and Wayland
access and is marked as a host escape.

## Usage polling

The **Settings > Integrations > Usage** page lists all quota windows in one table.
Each row has a name, icon selector, poll toggle, and optional interval.
A blank interval uses the global poll interval.
Credentials stay on **Settings > Integrations > Credentials** because those paths refer to host files.

| Provider | Credential | Notes |
| --- | --- | --- |
| Anthropic | `~/.claude/.credentials.json` (re-read each poll) | Session/weekly windows and Claude balance. Enabled by default. |
| OpenRouter | Key file, or daemon `OPENROUTER_API_KEY` when the path is blank | Balance polling is off by default; enable `openrouter_balance`. |
| OpenAI / Codex | `~/.codex/auth.json` | Available account windows, potentially weekly only on free plans. Enabled by default. |

Usage polling reads host credentials; CLI presets can separately mount shared
credential files into sandboxes. Polling does not make those mounts read-only.

## Prompt summaries

The daemon can use an OpenRouter model to generate short titles for agent prompts.
The **Settings > Agents > Summaries** page controls Codex and Pi session policies (`never`, `once`, or `always`),
task policies (`never` or `once`), minimum prompt length, and the model.
The daemon sends up to 2,000 characters of an eligible prompt to OpenRouter.
The shared summary instruction is prepended when a summary is requested; prompts
below the minimum length are skipped. Task summaries use the same summary settings.
The daemon reads its OpenRouter key from the configured file or, when no file is
configured, `OPENROUTER_API_KEY`. The Pi preset forwards that variable when set;
a summary key file is not automatically shared with agents.
For host terminal titles and fixed labels, see [Host terminals](#host-terminals).
Set the OpenRouter key path on **Settings > Integrations > Credentials**.

The **Settings > Agents > Workers** page edits the prompt that the daemon sends when you use
`slopctl worker spawn`. This prompt is separate from agent creation. It can refer to
`$SLOPWORLD_TASK_ID`.

## Library items and errands

See [Library items and errands](../guides/library.md) for prompts, shell commands,
breadcrumbs, and file actions.

## Host terminals

See [Host terminals](../guides/host-terminals.md) for persistent host tabs and labels.
