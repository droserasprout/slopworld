# Introduction

SlopWorld is a RimWorld mod that replaces the colony simulation with live AI coding agents.
Each agent runs in a sandboxed tmux session on the daemon host; one colonist on the map is one
agent. Select a colonist to type at its terminal.

The project has two halves:

- **slopd** — a Rust daemon that owns tmux sessions, the sandbox (Bubblewrap + pasta),
  the terminal emulator, quota polling, the jukebox, and `config.toml`. It runs as a
  systemd user service on Linux, or in a [sidecar container](guides/sidecar.md). The
  native listener defaults to `127.0.0.1:7717`.
- **mod** — a C# RimWorld mod (Harmony, 1.6 only) that connects over HTTP and WebSocket,
  mirrors the daemon's state, and draws the interface.

The daemon is the source of truth. The mod keeps no session state of its own.
