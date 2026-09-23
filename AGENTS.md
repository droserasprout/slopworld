# SlopWorld

SlopWorld uses RimWorld 1.6 as the interface for live coding agents. The Rust daemon,
`slopd`, controls sessions, tmux, sandboxes, and terminal emulation. The C# mod uses
Harmony to show this state in the colony and user interface. The mod and daemon
communicate through HTTP and WebSocket. `slopcar/` packages the Linux daemon for sidecar use.

## Working rules

- Use Makefile targets. Run `make` to list them. Follow the [house rules](notes/core-house-rules.md).
- Do not run the game, take screenshots, or inspect images unless the user asks.
- Keep notes to high-level guides, ownership boundaries, and non-obvious traps. Update the
  focused note when an ownership rule or behavior changes. See the [note policy](notes/README.md).

## Find the owner

Start with the relevant map. Follow its focused links. Then read the source and tests.
Do not read every note. User workflows live in the [book index](docs/src/SUMMARY.md).
A `notes/plan-*` file records unfinished work. It does not describe implemented behavior.

| Area | Starting points |
| --- | --- |
| Daemon lifecycle and services | [Source map](notes/daemon-files.md), [session state](notes/daemon-session-state.md) |
| Mod simulation and integration | [Source map](notes/mod-source-layout.md), [simulation](notes/mod-sim.md), [Harmony traps](notes/core-gotchas.md) |
| UI and terminal ownership | [Workspace](notes/ui-dynamic-layout-architecture.md), [sidebar](notes/mod-sidebar.md), [terminal](notes/mod-terminal.md) |
| Client/daemon boundary | [Client](notes/mod-client.md), [wire contract](notes/protocol-wire.md), [config ownership](notes/daemon-config-stores.md) |
| Agent access and collaboration | [Sandbox](notes/sandbox-isolation.md), [tasks](notes/agent-tasks.md), [workers](notes/daemon-workers.md) |
| Build and runtime operations | [Build](notes/build-commands.md), [paths](notes/ops-paths.md), [diagnostics](notes/ops-diagnostics.md), [sidecar status](notes/ops-macos-compatibility-status.md) |

## Delegation

Delegate only when the user clearly implies that they want you to delegate.

Use `slopctl worker spawn ...` to assign work to another agent. Keep the task ID.
Run `slopctl task wait ID` until the task ends. Do not poll `task list` or `task status`.
Do not use short timeouts.
For assigned work, run `task show ID`, then `task accept`, then `task progress`.
When work ends, run `task finish` or `task fail`.
