# Supported integrations

## Agent CLIs

Command presets cover the installed agent CLIs and common shells. The Settings > Commands
page lists them. Each preset names the software so the sandbox can hand it the right
configuration paths, seeded state, and environment.

Shell presets have matching `*-userdata` sandbox presets that are separate and opt-in.
When enabled, they expose startup and configuration files read-only and history and
data paths read-write. Choosing a shell preset alone does not share host dotfiles.

An agent with a raw `cmd` and no named `command` preset gets the sandbox but none
of the CLI-specific wiring.

## Usage polling

The Settings > Usage page keeps all quota windows in one table. Each row has a name,
icon picker, poll toggle, and optional interval; a blank interval uses the global poll
interval. Credentials stay on the Credentials page because those paths live on the
host.

| Provider | Credential | Notes |
| --- | --- | --- |
| Anthropic | `~/.claude/.credentials.json` (re-read each poll, never copied) | Enabled by default. |
| OpenRouter | Key file | Disabled until an `openrouter_balance` row is enabled. |
| OpenAI / Codex | `~/.codex/auth.json` | Polls session and weekly windows. Enabled by default. |

Each provider has its own failure backoff and does not stall the others. A failed
source dims only its own rows in the top bar.

## Prompt summaries

The daemon generates short titles for agent prompts and host commands using an
OpenRouter model. The Summaries settings page controls per-CLI policies (`never` /
`once` / `always`), the host-command toggle, minimum prompt length, and the model.
The OpenRouter key is the one from the Usage page.

## Instructions

Settings > Integrations > Instructions edits the generated `SLOPWORLD.md` template,
previews its rendered Markdown, edits the first-prompt discovery breadcrumb, chooses its
read-only sandbox mount path, and enables or disables discovery. The body and breadcrumb
each have an independent **Reset to default** action. The per-agent `slopworld_md` option
remains the opt-in that mounts the document; its `instructions_breadcrumb` option controls
whether that agent also receives the discovery line.

## Library items and errands

A library item delivers a prompt or shell command to an agent. Prompt errands paste text
and submit; shell errands run a command line. Breadcrumbs are named guidance blocks
that an agent receives alongside its first prompt.

File-sidebar actions (`kind = "fa"`) appear in the Files, Git, and Find context menus.
Their `command` runs against the selected path, with `{{ absolute_path }}` and
`{{ relative_path }}` available as substitutions. Set `mode` to `"nothing"`,
`"show_result"`, or `"open_terminal"` to choose what happens after selection; omitting it
keeps the per-invocation choice. `nothing` runs without opening a result pane. The other
modes show captured output in an alert or open an interactive temporary terminal.

Library item and breadcrumb presets are configured in Settings > Commands. Builtin entries
are shadowed when a user entry has the same name.

## Task mailboxes

`slopctl` is the host-side CLI for delegating tasks between agents. See
[Using slopctl](../guides/slopctl.md) and [Agent collaboration](../guides/agent-collaboration.md).

## Host terminals

The add strip offers host shells rooted at `~` or at a project directory. Host shells
are ephemeral sidebar rows with no agent pawn. Durable host tabs survive a stopped
shell and can be restarted; they store the last working directory and recreate at that
path on the next launch.

## Attaching from outside

The daemon's tmux server runs on a private socket. See
[Attaching from a terminal](../guides/terminal.md).
