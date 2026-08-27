# Integrations

SlopWorld runs any command inside a sandboxed tmux session. A command preset names the
software so the sandbox can hand it the right paths and configuration. Several integrations
get extra features.

## Supported agents

Command presets exist for Codex, Claude Code, Pi, and generic shells (bash, zsh, fish,
Nushell, pwsh). Each has a matching sandbox preset with the tool's configuration paths
mounted privately or shared as needed. Shell presets optionally expose startup files
read-only and history paths read-write.

An agent with a raw `cmd` and no named `command` preset gets the sandbox but none of the
CLI-specific wiring.

## Usage polling

Settings > Integrations > Usage keeps all quota windows in one table. Each row has a name,
icon picker, poll toggle, and optional interval; blank intervals use the global poll
interval. Credentials stay on the separate Credentials page because those paths live on the
host.

Supported providers:

- **Anthropic** — reads the OAuth token from `~/.claude/.credentials.json` (re-read each
  poll, never copied). Enabled by default.
- **OpenRouter** — polls `/api/v1/credits` with a key file. Disabled until an
  `openrouter_balance` row is enabled.
- **OpenAI / Codex** — reads `~/.codex/auth.json` and polls session and weekly windows.
  Enabled by default.

Each provider has its own failure backoff and does not stall the others. A failed source
dims only its own rows in the top bar; the clock is independent.

## Prompt summaries

The daemon can generate short titles for agent prompts and host commands using an
OpenRouter model. The Summaries settings page controls per-CLI policies (`never` / `once` /
`always`), the host-command toggle, minimum prompt length, and the model. Its OpenRouter
key is the Usage page's key file. See [Agents and projects](agents-and-projects.md) for
details.

## Shortcuts and errands

A shortcut delivers a prompt or shell command to an agent. Prompt errands paste text and
submit; shell errands run a command line. Breadcrumbs are named guidance blocks that an
agent receives alongside its first prompt. See the [Settings](../reference/settings.md)
and [Keyboard shortcuts](../reference/keyboard-shortcuts.md) references.

## Task mailboxes

`slopctl` is the host-side CLI for delegating tasks between agents. Each task has a sender,
recipient, state, body, and timestamps. Agents with a scoped grant can delegate to sessions
in their scope; the host user (`slopctl` without a session identity) can delegate to anyone.

```sh
slopctl delegate AGENT "review the auth module"
slopctl inbox
slopctl finish TASK_ID "done, see commit abc123"
```

## Host terminals

The add strip offers host shells rooted at `~` or at a project directory. Host shells are
ephemeral sidebar rows with no agent pawn. Durable host tabs survive a stopped shell and
can be restarted.

## Attaching from outside

The daemon's tmux server runs on a private socket. From any terminal on the host:

```sh
tmux -L slopworld list-sessions
tmux -L slopworld attach -t SESSION_NAME
```
