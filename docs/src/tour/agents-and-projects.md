# Agents and projects

## Projects

A project is a directory and a set of shared project mounts. Projects are defined in the
daemon's `config.toml` as `[[project]]` entries.

Each project names:

- A working directory, mounted read-write in every agent sandbox.
- Optional additional project directories and their read-only/read-write modes.

Temporary projects (`temp = true`) have no directory; the daemon creates one under
`/tmp/slopworld/` when the agent starts.

## Agents (sessions)

An agent is a session inside a project. It has a name, a command preset for the software it
runs, and agent-owned command-line, sandbox, network, DNS, resource-limit, and startup
settings.

A command preset names a piece of software rather than a raw command. Knowing the preset
lets the sandbox hand the agent the right configuration paths — for example, Claude gets
`~/.claude` mounted privately.

An agent's network mode is always direct. The documented default is `private`; DNS `resolved`
follows the daemon's current resolver and an unset resource limit means no cap.

## State

Each agent is in one of four states:

- **Down** — not running.
- **Working** — the terminal pane changed recently.
- **Waiting** — a state rule matched the bottom of the screen (e.g. a prompt).
- **Idle** — the screen has not changed for a while and no rule matched.

State classification reads the last few non-blank lines of the terminal, walking upward.
The lowest matching line wins; what scrolled out of view cannot keep a pane in `waiting`
after the agent has moved on.

Two clocks track state independently: the pane's last-change time decides when a quiet
pane goes idle, and the state-since time records how long the agent has been in its current
state. Both survive daemon restarts through tmux session metadata.

## Lifecycle

- **Start** spawns the tmux session inside a Bubblewrap sandbox.
- **Stop** sends a signal to the session.
- **Remove** stops the agent and drops its configuration.
- **Reset** removes the agent's private state (tool caches, conversation history) and
  moves it to a 14-day trash directory.

Agents survive daemon restarts — tmux and the game run outside the daemon's process group.
A stopped agent's state age is cached and restored when the daemon comes back.

## Ephemeral agents

Library errands spawn ephemeral sessions that are never written to `config.toml`.
When the process exits, the session is removed and cannot be restarted. Project host
shells instead keep a durable tab; see [Host terminals](../reference/integrations.md#host-terminals).

## Titles

The daemon can generate short summaries of agent prompts using an OpenRouter model. Title
policies for agent CLIs are `never`, `once` (first prompt only), or `always` (follows the
current task). Host terminal titles come from the terminal application unless a fixed label is
set. An agent title is a daemon-level override separate from the terminal's own OSC title.
