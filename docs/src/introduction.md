# Introduction

SlopWorld is a RimWorld mod that replaces the colony simulation with AI coding agents.
Each agent operates in a tmux session inside a sandbox on the daemon host.
Each colonist on the map represents one agent.

Select a colonist to enter text in its terminal.

The project has two parts:

- **slopd** is a Rust daemon. It controls tmux sessions, the sandbox (Bubblewrap and pasta),
  the terminal emulator, quota polling, the jukebox, and `config.toml`.
  It operates as a systemd user service on Linux or in a [sidecar container](guides/sidecar.md).
  The default address for the native listener is `127.0.0.1:7717`.
- **mod** is a C# mod for RimWorld 1.6 only. It uses Harmony.
  It connects to the daemon through HTTP and WebSocket.
  It shows the daemon state in the game interface.

The daemon controls the session state. The mod does not keep separate session state.
