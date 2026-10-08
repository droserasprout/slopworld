# Architecture

SlopWorld uses RimWorld 1.6 as the interface for coding agents. The C# mod presents
sessions in the colony and workspace; the Rust daemon, `slopd`, manages the processes
and host resources behind them.

## Components and ownership

| Component | Responsibility | Source |
| --- | --- | --- |
| Mod | RimWorld integration through Harmony, colony simulation, workspace panels, terminal rendering, and user input | [`mod/Source/SlopWorld/`](https://github.com/droserasprout/slopworld/tree/main/mod/Source/SlopWorld) |
| Daemon | Session lifecycle, projects, files, Git operations, tasks, workers, sandbox configuration, and terminal capture | [`slopd/src/`](https://github.com/droserasprout/slopworld/tree/main/slopd/src) |
| Host tools | Launching, installation, and `slopctl` access to daemon operations | [`slopd/src/bin/`](https://github.com/droserasprout/slopworld/tree/main/slopd/src/bin) |
| Shared protocol | Protobuf messages exchanged by the daemon and mod | [`shared/`](https://github.com/droserasprout/slopworld/tree/main/shared) |
| Sidecar | Linux container packaging for the daemon and its runtime dependencies | [`slopcar/`](https://github.com/droserasprout/slopworld/tree/main/slopcar) |

Within the mod, `Patches/` owns Harmony integration, `Sim/` owns simulation, and
`UI/` owns workspace presentation. `Client/SessionHub/` coordinates connections
and hands updates to the session, task, catalog, terminal, usage, and audio services.

Within the daemon, `api/` owns transport and request handling, while
`session/manager/` coordinates live sessions and related services. Configuration,
persistence, sandbox construction, Git operations, and terminal emulation have
separate owners. The [daemon source map](https://github.com/droserasprout/slopworld/blob/main/notes/daemon-files.md)
and [mod source map](https://github.com/droserasprout/slopworld/blob/main/notes/mod-source-layout.md)
identify the focused entry points for implementation work.

## Requests and live updates

The mod talks to the daemon over HTTP and WebSocket using Protobuf messages.
HTTP carries requests and responses; WebSocket carries live events and terminal
traffic. The mod maintains client views of daemon state and renders them in the
workspace and colony. Connection coordination and presentation belong to the mod;
host operations and session management belong to the daemon.

See the [API reference](../reference/api.md) for authentication and transport
contracts, and the [route inventory](../reference/api-routes.md) for available
operations.

## Sessions and terminals

The daemon manages tmux sessions and builds agent sandboxes. Its terminal capture
pipeline feeds a terminal emulator and publishes the resulting state to the mod.
The mod renders terminal cells and sends user input back through the client.

Session lifecycle, terminal display, and colony simulation are separate concerns.
For their user-facing behavior, see [Session lifecycle](../agents/session-lifecycle.md),
[Input and panes](../terminals/terminal-interface.md), and
["Gameplay"](../getting-started/gameplay.md). Sandbox access boundaries are described
in the [Security model](../sandbox/security.md).

## Native and sidecar deployment

On native Linux, the game/mod and daemon run on the host. In sidecar mode, the
native game/mod connects to a Linux container running the daemon, tmux, and agent
sandboxes. Workspace paths must match between the host and container.
The native macOS workflow lives in `mac/`; shared container tooling lives in
`slopcar/`.

Replacing the container ends its tmux processes, while persistent private state
can remain. See [Sidecar mode](../deployment/sidecar.md) and
[macOS](../deployment/macos.md) for setup and lifecycle procedures.
