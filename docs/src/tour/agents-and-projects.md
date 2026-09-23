# Agents and projects

## Projects

A project is a directory and a set of shared project mounts. The daemon's `config.toml` defines projects as `[[project]]` entries.

Each project names:

- A working directory. The daemon mounts it read-write in every agent sandbox.
- Optional additional directories, each with read-only or read-write access.

Temporary projects (`temp = true`) have no directory initially.
The daemon creates one under `/tmp/slopworld/` when the agent starts.

## Agents (sessions)

An agent is a session inside a project. It has a name and a command preset for the software it runs.
It also has its own command-line, sandbox, network, DNS, resource-limit, and startup settings.

A command preset names software instead of a raw command. The sandbox uses the preset to mount
the software's configuration paths. For example, the Claude preset mounts `~/.claude` as
private storage.

Each agent specifies its network mode directly. The documented default is `private`.
DNS `resolved` follows the daemon's current resolver.
An unset resource limit means no limit.

## State

Each agent is in one of four states:

- **Down** — not running.
- **Working** — the terminal pane changed recently.
- **Waiting** — a state rule matched the bottom of the screen (e.g. a prompt).
- **Idle** — the screen has not changed for a while and no rule matched.

The daemon checks the last few non-blank terminal lines from bottom to top.
The lowest matching line determines the state.
Text outside the view cannot keep an agent in `Waiting` after it sends new output.

Two clocks track state independently.
The pane's last-change time determines when a pane without changes becomes idle.
The state-since time records when the current state started.
Tmux session metadata preserves both times through daemon restarts.

## Lifecycle

- **Start** creates the tmux session inside a Bubblewrap sandbox.
- **Stop** sends a signal to the agent's session.
- **Remove** stops the agent, then removes its configuration.
- **Reset** removes the agent's private state (tool caches, conversation history) and
  moves it to a 14-day trash directory.

Tmux and RimWorld run outside the daemon's process group, so agents keep running through daemon restarts.
The daemon caches a stopped agent's state age and restores it after restart.

## Ephemeral agents

Library errands create temporary sessions. The daemon does not write them to `config.toml`.
When the process exits, the daemon removes the session. You cannot restart it.
Project host shells keep a persistent tab. See [Host terminals](../reference/integrations.md#host-terminals).

## Titles

The daemon can use an OpenRouter model to summarize an agent's prompt. Agent title policies
are `never`, `once` (first prompt only), or `always` (follows the current task). Host terminal
titles come from the terminal application unless you set a fixed label. The daemon's agent
title override is separate from the terminal's OSC title.
