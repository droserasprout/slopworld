# Supported integrations

## Agent CLIs

Command presets cover installed agent CLIs and common shells. Each has a `kind` of `agent` or
`shell`. The **Settings > Commands** page uses this field to separate the Agent and Shell
defaults. It reads both lists from the current catalog.
Each preset names software so the sandbox can supply its configuration paths, initial state,
and environment.

Shell presets have matching `*-userdata` sandbox presets. These presets are separate and opt-in.
When enabled, they expose startup and configuration files as read-only.
They expose history and data paths as read-write.
Choosing a shell preset alone does not share host dotfiles.

An agent with a raw `cmd` and no named `command` preset gets the sandbox but none
of the CLI-specific configuration.

## Usage polling

The **Settings > Integrations > Usage** page lists all quota windows in one table.
Each row has a name, icon selector, poll toggle, and optional interval.
A blank interval uses the global poll interval.
Credentials stay on **Settings > Integrations > Credentials** because those paths refer to host files.

| Provider | Credential | Notes |
| --- | --- | --- |
| Anthropic | `~/.claude/.credentials.json` (re-read each poll, never copied) | Enabled by default. |
| OpenRouter | Key file | Enable an `openrouter_balance` row to use this provider. |
| OpenAI / Codex | `~/.codex/auth.json` | Polls session and weekly windows. Enabled by default. |

Each provider has its own failure backoff and does not stall the others. A failed
source dims only its own rows in the top bar.

## Prompt summaries

The daemon can use an OpenRouter model to generate short titles for agent prompts.
The **Settings > Agents > Summaries** page controls per-CLI policies (`never`, `once`, or
`always`), minimum prompt length, and the model.
For host terminal titles and fixed labels, see [Host terminals](#host-terminals).
Set the OpenRouter key path on **Settings > Integrations > Credentials**.

The **Settings > Agents > Workers** page edits the prompt that the daemon sends when you use
`slopctl worker spawn`. This prompt is separate from agent creation. It can refer to
`$SLOPWORLD_TASK_ID`.

## Library items and errands

A library item stores a prompt or shell command for an agent. Prompt errands paste and submit
text. Shell errands run a command line.
A breadcrumb is a saved guidance block. Insert it from a terminal's context menu.

File-sidebar actions (`kind = "fa"`) appear in the Files, Git, and Find context menus.
Their `command` runs against the selected path, with `{{ absolute_path }}` and
`{{ relative_path }}` available as substitutions. Set `mode` to `"nothing"`,
`"show_result"`, or `"open_terminal"` to choose what happens after selection.
If you omit `mode`, each invocation retains its own choice. `nothing` runs without opening a result pane. The other
modes show captured output in an alert or open an interactive temporary terminal.

Manage prompts, errands, breadcrumbs, and file actions in the Library.
Manage command presets in **Settings > Commands**.
A user entry replaces the supplied entry with the same name.

## Task mailboxes

`slopctl` is the host-side CLI for delegating tasks between agents. See
[Using slopctl](../guides/slopctl.md) and [Agent collaboration](../guides/agent-collaboration.md).

## Host terminals

The add strip offers host shells at `~` or at a project directory, without an agent pawn.
Project host tabs remain after their shell stops. You can restart these tabs.
They save the last working directory and use it at the next launch.
After a reboot, saved project tabs start automatically when autostart is enabled
(the default). Otherwise, they return as stopped tabs.

The context menu offers Start, Stop, Terminal, Label, and Remove. Terminal is available
only while the pane is running. Stop ends the shell but keeps the tab and its saved
directory. Remove ends the shell and deletes the saved tab. Agent edit and duplicate
actions do not apply to host tabs.

Host tabs use the terminal application's title. Label saves a fixed title; clearing
it restores the application's title.

## Attaching from outside

The daemon's tmux server runs on a private socket. See
[Attaching from a terminal](../guides/terminal.md).
