# SlopWorld

RimWorld 1.6 as a frontend for live coding agents. Rust `slopd` owns sessions, tmux,
sandboxes and terminal emulation; the C#/Harmony mod mirrors them into colony and UI.
They communicate over HTTP/WebSocket. `slopcar/` packages the Linux daemon for sidecar use.

## Working rules

- Use Makefile targets; `make` lists commands. Follow [house rules](notes/core-house-rules.md).
- Do not run the game, take screenshots, or inspect images unless directly asked.
- Keep notes to high-level guides, ownership boundaries and non-obvious traps; update the
  focused note when those change. See [note policy](notes/README.md).

## Find the owner

Start with the relevant map, follow its focused links, then read source/tests. Do not load
all notes. User workflows live in the [book index](docs/src/SUMMARY.md); `notes/plan-*`
records unresolved work, not implemented behavior.

| Area | Starting points |
| --- | --- |
| Daemon lifecycle and services | [Source map](notes/daemon-files.md), [session state](notes/daemon-session-state.md) |
| Mod simulation and integration | [Source map](notes/mod-source-layout.md), [simulation](notes/mod-sim.md), [Harmony traps](notes/core-gotchas.md) |
| UI and terminal ownership | [Workspace](notes/ui-dynamic-layout-architecture.md), [sidebar](notes/mod-sidebar.md), [terminal](notes/mod-terminal.md) |
| Client/daemon boundary | [Client](notes/mod-client.md), [wire contract](notes/protocol-wire.md), [config ownership](notes/daemon-config-stores.md) |
| Agent access and collaboration | [Sandbox](notes/sandbox-isolation.md), [tasks](notes/agent-tasks.md), [workers](notes/daemon-workers.md) |
| Build and runtime operations | [Build](notes/build-commands.md), [paths](notes/ops-paths.md), [diagnostics](notes/ops-diagnostics.md), [sidecar status](notes/ops-macos-compatibility-status.md) |

## Delegation

Delegate only when asked by user implicitly

Use `slopctl spawn ...` to delegate work to other agents. Retain the ID.
Use `slopctl wait ID` until terminal; do not poll `task`/`inbox`/`status` or use short timeouts.
Assigned work follows `task ID`, `accept`, `progress`, then `finish` or `fail`.
