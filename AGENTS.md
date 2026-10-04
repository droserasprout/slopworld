# SlopWorld

SlopWorld uses RimWorld 1.6 as the interface for live coding agents. The Rust daemon,
`slopd`, controls sessions, tmux, sandboxes, and terminal emulation. The C# mod uses
Harmony to show this state in the colony and user interface. The mod and daemon
communicate through HTTP and WebSocket. `slopcar/` packages the Linux daemon for sidecar use.

## Working rules

- Use just recipes. Run `just` to list them. Follow the [house rules](notes/core-house-rules.md).
- Do not run the game, take screenshots, or inspect images unless the user asks.
- Keep notes to high-level guides, ownership boundaries, and non-obvious traps. Update the
  focused note when an ownership rule or behavior changes. See the [note policy](notes/README.md).

## Find the owner

Start with the relevant map. Follow its focused links. Then read the source and tests.
Do not read every note. User workflows live in the [book index](docs/src/SUMMARY.md).
A `priv/notes/plan-*` file tracks a change through review and merge.
It does not describe current behavior.

| Area | Starting points |
| --- | --- |
| Daemon lifecycle and services | [Source map](notes/daemon-files.md), [session state](notes/daemon-session-state.md) |
| Mod simulation and integration | [Source map](notes/mod-source-layout.md), [simulation](notes/mod-sim.md), [Harmony traps](notes/core-gotchas.md) |
| UI and terminal ownership | [Workspace](notes/ui-dynamic-layout-architecture.md), [sidebar](notes/mod-sidebar.md), [terminal](notes/mod-terminal.md) |
| Client/daemon boundary | [Client](notes/mod-client.md), [wire contract](notes/protocol-wire.md), [config ownership](notes/daemon-config-stores.md) |
| Agent access and collaboration | [Sandbox](notes/sandbox-isolation.md), [tasks](notes/agent-tasks.md), [workers](notes/daemon-workers.md) |
| Build and runtime operations | [Build](notes/build-commands.md), [paths](notes/ops-paths.md), [diagnostics](notes/ops-diagnostics.md), [sidecar architecture](notes/ops-macos-compatibility.md) |

## Rust code guidelines

Follow the patterns in the reviewed Rust files listed in `priv/BIG_REVIEW.md`:

- Give each module one clear responsibility. Start with a short `//!` ownership summary;
  name neighboring owners where the boundary matters. Keep coordination separate from
  policy, persistence, transport, and client views.
- Group state by responsibility and lifetime, as `LiveInput` and `LiveCapture` do.
  Use concrete structs and enums for plans, outcomes, and resource handoffs; keep visibility narrow.
- Make orchestration read as named stages: prepare, validate, commit, publish, clean up.
  Extract helpers around meaningful decisions or ownership boundaries.
- Make lock scope and asynchronous ownership explicit. Carry owned plans out of state locks
  before I/O; recheck run or reader identity before applying delayed work. Keep required
  operation guards through commit or rollback, and handle partial failure and cancellation.
- Keep comments beside the code they explain. Describe contracts, ordering, lifetimes, and
  non-obvious reasons; use short section comments to orient readers in longer flows.
- Binary-root tests use the binary's subdirectory; standalone `src/bin/` files become executables.
- Keep tests beside their owner in separate test modules. Name the behavior being protected;
  cover failure, recovery, stale work, and repeated cleanup. Control race ordering explicitly.

## Delegation

Delegate only when the user clearly implies that they want you to delegate.

Use `slopctl worker spawn ...` to assign work to another agent. Keep the task ID.
Run `slopctl task wait ID` until the task ends. Do not poll `task list` or `task status`.
Do not use short timeouts.
For assigned work, run `task show ID`, then `task accept`, then `task progress`.
When work ends, run `task finish` or `task fail`.
